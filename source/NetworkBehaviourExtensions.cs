using System;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RuntimeNetcodeRPCValidator
{
    public static class NetworkBehaviourExtensions
    {

        private static class RpcData
        {
            public enum RpcState
            {
                FromUser = 0,
                FromNetworking = 1
            }

            private static RpcState received;
            internal static RpcState RpcSource
            {
                get
                {
                    var ret = received;
                    received = RpcState.FromUser;
                    return ret;
                }
                set => received = value;
            }
            public static ulong SenderId;
        }

        private static T LogErrorAndReturn<T>(string error, T ret = default)
        {
            Plugin.Logger.LogError(error);
            return ret;
        }
        /// <summary>
        /// Retrieves the last remote procedure call (RPC) sender.
        /// </summary>
        public static ulong LastSenderId => RpcData.SenderId; 
        
        private static bool MethodPatchInternal(NetworkBehaviour networkBehaviour, MethodBase original, object[] args)
        {
            if (!NetworkManager.Singleton || !(NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsConnectedClient))
                return LogErrorAndReturn<bool>($"NetworkBehaviour {networkBehaviour.NetworkBehaviourId} tried to send a RPC but the NetworkManager {(NetworkManager.Singleton ? "isn't active!" : "is null!")}");
            
            var rpcSource = RpcData.RpcSource;
            
            RpcAttribute rpcAttribute = original.GetCustomAttribute<ServerRpcAttribute>();
            rpcAttribute ??= original.GetCustomAttribute<ClientRpcAttribute>();

            switch (rpcSource)
            {
                case RpcData.RpcState.FromUser when (rpcAttribute is ServerRpcAttribute {RequireOwnership: true} && 
                                                               networkBehaviour.OwnerClientId != NetworkManager.Singleton.LocalClientId):
                    return LogErrorAndReturn<bool>($"Tried to run ServerRPC {original.Name} but we're not the owner of NetworkObject {networkBehaviour.NetworkObjectId}");
                case RpcData.RpcState.FromUser:
                    if (original.DeclaringType == null)
                        return LogErrorAndReturn<bool>($"NetworkBehaviour {networkBehaviour.NetworkBehaviourId} tried to send a RPC but the declaring type was null. This should never happen!");
                    
                    var writer = new FastBufferWriter((original.GetParameters().Length + 1) * 128, Allocator.Temp);
                    writer.WriteValueSafe(networkBehaviour.NetworkObjectId);
                    writer.WriteValueSafe(networkBehaviour.NetworkBehaviourId);

                    writer.WriteMethodInfoAndParameters(original, args);

                    var delivery = rpcAttribute.Delivery == RpcDelivery.Reliable
                        ? NetworkDelivery.Reliable
                        : NetworkDelivery.Unreliable;
            
                    if (rpcAttribute is ServerRpcAttribute)
                        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage($"Net.{original.DeclaringType.Name}", NetworkManager.ServerClientId, writer, delivery);
                    else
                        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll($"Net.{original.DeclaringType.Name}", writer, delivery);

                    return false;
                case RpcData.RpcState.FromNetworking when rpcAttribute is ServerRpcAttribute && !(networkBehaviour.IsServer || networkBehaviour.IsHost):
                    return LogErrorAndReturn<bool>($"Received message to run ServerRPC {original.DeclaringType}.{original.Name} but we're a client!");
                case RpcData.RpcState.FromNetworking when rpcAttribute != null:
                    return true;
                default:
                    return LogErrorAndReturn<bool>($"NetworkBehaviour {networkBehaviour.NetworkBehaviourId} tried to validate {original.DeclaringType}.{original.Name}, " +
                                                    "and while it exists, the method is not marked with a corresponding RPCAttribute. Please prefix with ServerRPC or ClientRPC!");
            }
        }        
        
        internal static void ReceiveNetworkMessage(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out ulong networkObjectId);
            reader.ReadValueSafe(out ushort networkBehaviourId);
            var backStep = reader.Position;
            reader.ReadValueSafe(out string rpcName);
            reader.Seek(backStep);

            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId,
                    out var networkObject) && LogErrorAndReturn(
                    "An RPC called on a NetworkObject that is not in the spawned objects list. Please make sure the NetworkObject is spawned before calling RPCs.",
                    true))
                return;
            
            
            var networkBehaviour = networkObject!.GetNetworkBehaviourAtOrderIndex(networkBehaviourId);
            var method = networkBehaviour.GetType().GetMethod(rpcName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null &&
                LogErrorAndReturn(
                    $"NetworkBehaviour {networkBehaviour.NetworkBehaviourId} received RPC {rpcName} but that method doesn't exist on {networkBehaviour.GetType()}!",
                    true))
                return;

            var serverAttribute = method!.GetCustomAttribute<ServerRpcAttribute>();
            if (serverAttribute != null && !networkBehaviour.IsHost && !networkBehaviour.IsServer &&
                LogErrorAndReturn(
                    $"NetworkBehaviour {networkBehaviour.NetworkBehaviourId} received ServerRPC {rpcName} but we're a client!",
                    true)) 
                return;

            if ((serverAttribute?.RequireOwnership ?? false) && sender != networkObject.OwnerClientId &&
                LogErrorAndReturn(
                    $"NetworkBehaviour {networkBehaviour.NetworkBehaviourId} received ServerRPC but the sender wasn't the owner!",
                    true)) 
                return;
            
            RpcData.RpcSource = RpcData.RpcState.FromNetworking;
            RpcData.SenderId = sender;
            
            object[] methodParams = null;
            if (method.GetParameters().Any())
                methodParams = new object[method.GetParameters().Length];
            
            reader.ReadMethodInfoAndParameters(method.DeclaringType, ref methodParams);
            method.Invoke(networkBehaviour, methodParams);
        }

        internal static bool MethodPatch(NetworkBehaviour __instance, MethodBase __originalMethod, object[] __args)
        {
            return MethodPatchInternal(__instance, __originalMethod, __args);
        }
    }
}