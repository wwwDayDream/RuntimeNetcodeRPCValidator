using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RuntimeNetcodeRPCValidator
{
    public static class NetworkBehaviourExtensions
    {
        private const BindingFlags BindingAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private enum RpcState {AwaitingMessage, MessageReceived}

        private static readonly Dictionary<(ulong netObjId, ushort netBehId), RpcState> NetRPCStates =
            new Dictionary<(ulong netObjId, ushort netBehId), RpcState>();
        private static readonly Dictionary<(ulong netObjId, ushort netBehId), FastBufferReader> NetRPCData =
            new Dictionary<(ulong netObjId, ushort netBehId), FastBufferReader>();
        private static readonly Dictionary<(ulong netObjId, ushort netBehId), object[]> NetRPCParams =
            new Dictionary<(ulong netObjId, ushort netBehId), object[]>();
        private static readonly Dictionary<(ulong netObjId, ushort netBehId), ulong> NetRPCSender =
            new Dictionary<(ulong netObjId, ushort netBehId), ulong>();
        private static RpcState GetNetworkState(this NetworkBehaviour behaviour)
        {
            var key = (behaviour.NetworkObjectId, behaviour.NetworkBehaviourId);
            if (NetRPCStates.TryGetValue(key, out var val))
            {
                NetRPCStates[key] = RpcState.AwaitingMessage;
                return val;
            }
            NetRPCStates.Add(key, RpcState.AwaitingMessage);
            return NetRPCStates[key];
        }
        private static FastBufferReader GetNetworkData(this NetworkBehaviour behaviour) => NetRPCData.GetValueOrDefault((behaviour.NetworkObjectId, behaviour.NetworkBehaviourId));

        /// <summary>
        /// Retrieves the last remote procedure call (RPC) sender for the given network behaviour.
        /// </summary>
        /// <param name="networkBehaviour">The network behaviour for which to retrieve the last RPC sender.</param>
        /// <returns>The ID of the last RPC sender.</returns>
        public static ulong LastRPCSender(this NetworkBehaviour networkBehaviour)
        {
            var key = (networkBehaviour.NetworkObjectId, networkBehaviour.NetworkBehaviourId);
            if (!NetRPCSender.TryGetValue(key, out _))
                NetRPCSender.Add(key, NetworkManager.ServerClientId);
            return NetRPCSender[key];
        }
        /// <summary>
        /// Registers the network behaviour with the parent network object.
        /// </summary>
        /// <param name="networkBehaviour">The network behaviour to register.</param>
        private static void VerifyAsRegisteredWithNetworkObject(this NetworkBehaviour networkBehaviour)
        {
            if (networkBehaviour.NetworkObject.ChildNetworkBehaviours.Contains(networkBehaviour))
                return;
            
            networkBehaviour.NetworkObject.ChildNetworkBehaviours.Add(networkBehaviour);
            networkBehaviour.UpdateNetworkProperties();
        }
        
        private static bool ValidateRPCExecution(this NetworkBehaviour networkBehaviour, string rpcName)
        {
            var method = networkBehaviour.GetType().GetMethod(rpcName, BindingAll);
            if (method == null)
            {
                Debug.LogError($"[Network] NetworkBehaviour {networkBehaviour.NetworkBehaviourId} tried to validate {rpcName} but that method doesn't exists on {networkBehaviour.GetType()}!");
                return false;
            }
            var methodAttributes = method.GetCustomAttributes(false);
            var rpcState = networkBehaviour.GetNetworkState();
            var rpcData = networkBehaviour.GetNetworkData();
            
            if (Array.Exists(methodAttributes, o => o.GetType() == typeof(ServerRpcAttribute)))
            {
                var rpcAttribute = method.GetCustomAttribute<ServerRpcAttribute>();
                switch (rpcState)
                {
                    case RpcState.AwaitingMessage:
                        if (rpcAttribute.RequireOwnership &&
                            (networkBehaviour.OwnerClientId != NetworkManager.Singleton.LocalClientId))
                            Debug.LogError($"[Network] Tried to run ServerRPC {rpcName} but we're not the owner of NetworkObject {networkBehaviour.NetworkObjectId}");
                        else
                            networkBehaviour.SendRPC(method, rpcAttribute);
                        return false;
                    case RpcState.MessageReceived:
                        if (networkBehaviour.IsServer || networkBehaviour.IsHost)
                        {
                            return networkBehaviour.ProcessRPC(method, rpcData);
                        }
                        Debug.LogError($"[Network] Received message to run ServerRPC {rpcName} but we're a client!");
                        return false;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            
            if (Array.Exists(methodAttributes, o => o.GetType() == typeof(ClientRpcAttribute)))
            {
                var rpcAttribute = method.GetCustomAttribute<ClientRpcAttribute>();
                switch (rpcState)
                {
                    case RpcState.AwaitingMessage:
                        networkBehaviour.SendRPC(method, rpcAttribute);
                        return false;
                    case RpcState.MessageReceived:
                        return networkBehaviour.ProcessRPC(method, rpcData);
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            
            Debug.LogError($"[Network] NetworkBehaviour {networkBehaviour.NetworkBehaviourId} tried to validate {rpcName}, " +
                           $"and while it exists, the method is not marked with a corresponding RPCAttribute. Please prefix with ServerRPC or ClientRPC!");
            return false;
        }
        private static void PrepareRPCParamsForSending(NetworkBehaviour networkBehaviour, object[] paramArray)
        {
            var key = (networkBehaviour.NetworkObjectId, networkBehaviour.NetworkBehaviourId);
            if (!NetRPCParams.TryGetValue(key, out _))
                NetRPCParams.Add(key, paramArray);
            else
                NetRPCParams[key] = paramArray;
        }
        private static void SendRPC(this NetworkBehaviour networkBehaviour, MethodInfo method, RpcAttribute attribute)
        {
            var key = (networkBehaviour.NetworkObjectId, networkBehaviour.NetworkBehaviourId);
            if (!NetRPCParams.TryGetValue(key, out _))
                NetRPCParams.Add(key, method.GetParameters().Select(param => param.DefaultValue).ToArray());
            var writer = new FastBufferWriter(1024, Allocator.Temp);
            writer.WriteValueSafe(networkBehaviour.NetworkObjectId);
            writer.WriteValueSafe(networkBehaviour.NetworkBehaviourId);
            writer.WriteValueSafe(method.Name);
            
            writer.WriteValueSafe(method.GetParameters().Count());
            var idx = 0;
            method.GetParameters().Do(info =>
            {
                var formatter = new BinaryFormatter();
                using var stream = new MemoryStream();
                formatter.Serialize(stream, NetRPCParams[key][idx]);
                var allBytes = stream.ToArray();
                idx++;
                
                writer.WriteValueSafe(allBytes.Length);
                writer.WriteBytes(allBytes);
            });
            
            var delivery = attribute.Delivery == RpcDelivery.Reliable
                ? NetworkDelivery.Reliable
                : NetworkDelivery.Unreliable;
            
            switch (attribute)
            {
                case ServerRpcAttribute _ when method.DeclaringType != null:
                    // Debug.Log($"[Network] NetworkBehaviour {networkBehaviour.NetworkBehaviourId} sending ServerRPC {method.Name} to server on channel 'Net.{method.DeclaringType.Name}'");
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage($"Net.{method.DeclaringType.Name}", NetworkManager.ServerClientId, writer, delivery);
                    break;
                case ClientRpcAttribute _ when method.DeclaringType != null:
                    // Debug.Log($"[Network] NetworkBehaviour {networkBehaviour.NetworkBehaviourId} sending ClientRPC {method.Name} to all clients on channel 'Net.{method.DeclaringType.Name}'");
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll($"Net.{method.DeclaringType.Name}", writer, delivery);
                    break;
                case ServerRpcAttribute _:
                case ClientRpcAttribute _:
                    Debug.LogError($"[Network] NetworkBehaviour {networkBehaviour.NetworkBehaviourId} tried to send a RPC but the declaring type was null. This should never happen!");
                    break;
            }
        }
        private static bool ProcessRPC(this NetworkBehaviour networkBehaviour, MethodInfo method, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int paramCount);
            if (paramCount != method.GetParameters().Count())
            {
                Debug.LogError($"[Network] NetworkBehaviour {networkBehaviour.NetworkBehaviourId} received an RPC" +
                               $" but the number of parameters sent {paramCount} != MethodInfo param count {method.GetParameters().Length}");
                return false;
            }

            var paramValues = new object[paramCount];
            for (var i = 0; i < paramCount; i++)
            {
                reader.ReadValueSafe(out int byteLength);
                var serializedData = new byte[byteLength];
                reader.ReadBytes(ref serializedData, byteLength);
                
                using var stream = new MemoryStream(serializedData);
                stream.Seek(0, 0);
                var formatter = new BinaryFormatter();
                paramValues[i] = formatter.Deserialize(stream);
            }
            
            var key = (networkBehaviour.NetworkObjectId, networkBehaviour.NetworkBehaviourId);
            if (!NetRPCParams.TryAdd(key, paramValues))
                NetRPCParams[key] = paramValues;
            return true;
        }

        private static bool MethodPatchInternal(NetworkBehaviour instance, MethodBase original, object[] args)
        {
            VerifyAsRegisteredWithNetworkObject(instance);
            PrepareRPCParamsForSending(instance, args);
            var retValue = instance.ValidateRPCExecution(original.Name);
            if (!retValue) return false;
            var netRPCParam = NetRPCParams[(instance.NetworkObjectId, instance.NetworkBehaviourId)];
            for (var j = 0; j < netRPCParam.Length; j++)
            {
                args[j] = netRPCParam[j];
            }
            return true;    
        }        
        
        internal static void ReceiveNetworkMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong networkObjectId);
            reader.ReadValueSafe(out ushort networkBehaviourId);
            reader.ReadValueSafe(out string rpcName);

            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId,
                    out var networkObject))
            {
                Debug.LogError("An RPC called on a NetworkObject that is not in the spawned objects list. Please make sure the NetworkObject is spawned before calling RPCs.");
                return;
            }
            
            var networkBehaviour = networkObject.GetNetworkBehaviourAtOrderIndex(networkBehaviourId);
            var method = networkBehaviour.GetType().GetMethod(rpcName, BindingAll);
            if (method == null)
            {
                Debug.LogError($"[Network] NetworkBehaviour {networkBehaviour.NetworkBehaviourId} received RPC {rpcName} but that method doesn't exist on {networkBehaviour.GetType()}!");
                return;
            }

            var serverAttribute = method.GetCustomAttribute<ServerRpcAttribute>();
            if (serverAttribute != null)
            {
                if (!networkBehaviour.IsHost && !networkBehaviour.IsServer)
                {
                    Debug.LogError($"[Network] NetworkBehaviour {networkBehaviour.NetworkBehaviourId} received ServerRPC {rpcName} but we're a client!");
                    return;
                }
                if (serverAttribute.RequireOwnership && sender != networkObject.OwnerClientId)
                {
                    Debug.LogError($"[Network] NetworkBehaviour {networkBehaviour.NetworkBehaviourId} received ServerRPC but the sender wasn't the owner!");
                    return;
                }
            }

            var dictionaryKey = (networkObject.NetworkObjectId, networkBehaviour.NetworkBehaviourId);
            NetRPCStates[dictionaryKey] = RpcState.MessageReceived;
            NetRPCData[dictionaryKey] = reader;
            NetRPCSender[dictionaryKey] = sender;
            
            object[] methodParams = null;
            if (method.GetParameters().Any())
                methodParams = new object[method.GetParameters().Length];
            
            method.Invoke(networkBehaviour, methodParams);
        }

        internal static bool MethodPatch0(NetworkBehaviour __instance, MethodBase __originalMethod)
        {
            return MethodPatchInternal(__instance, __originalMethod, Array.Empty<object>());
        }
        internal static bool MethodPatch1(NetworkBehaviour __instance, MethodBase __originalMethod, ref object __0)
        {
            var args = new[] { __0 };
            var retVal = MethodPatchInternal(__instance, __originalMethod, args);
            __0 = args[0];
            return retVal;
        }
        internal static bool MethodPatch2(NetworkBehaviour __instance, MethodBase __originalMethod, ref object __0, ref object __1)
        {
            var args = new[] { __0, __1 };
            var retVal = MethodPatchInternal(__instance, __originalMethod, args);
            __0 = args[0];
            __1 = args[1];
            return retVal;
        }
        internal static bool MethodPatch3(NetworkBehaviour __instance, MethodBase __originalMethod, ref object __0, ref object __1, ref object __2)
        {
            var args = new[] { __0, __1, __2 };
            var retVal = MethodPatchInternal(__instance, __originalMethod, args);
            __0 = args[0];
            __1 = args[1];
            __2 = args[2];
            return retVal;
        }
        internal static bool MethodPatch4(NetworkBehaviour __instance, MethodBase __originalMethod, ref object __0, ref object __1, ref object __2, ref object __3)
        {
            var args = new[] { __0, __1, __2, __3 };
            var retVal = MethodPatchInternal(__instance, __originalMethod, args);
            __0 = args[0];
            __1 = args[1];
            __2 = args[2];
            __3 = args[3];
            return retVal;
        }
        internal static bool MethodPatch5(NetworkBehaviour __instance, MethodBase __originalMethod, ref object __0, ref object __1, ref object __2, ref object __3, ref object __4)
        {
            var args = new[] { __0, __1, __2, __3, __4 };
            var retVal = MethodPatchInternal(__instance, __originalMethod, args);
            __0 = args[0];
            __1 = args[1];
            __2 = args[2];
            __3 = args[3];
            __4 = args[4];
            return retVal;
        }
        internal static bool MethodPatch6(NetworkBehaviour __instance, MethodBase __originalMethod, ref object __0, ref object __1, ref object __2, ref object __3, ref object __4, ref object __5)
        {
            var args = new[] { __0, __1, __2, __3, __4, __5 };
            var retVal = MethodPatchInternal(__instance, __originalMethod, args);
            __0 = args[0];
            __1 = args[1];
            __2 = args[2];
            __3 = args[3];
            __4 = args[4];
            __5 = args[5];
            return retVal;
        }
    }
}