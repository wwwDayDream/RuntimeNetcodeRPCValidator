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
        public enum RpcState
        {
            FromUser,
            FromNetworking
        }

        /// <summary>
        /// Retrieves the last remote procedure call (RPC) sender.
        /// </summary>
        public static ulong LastSenderId { get; private set; }

        private static RpcState RpcSource;
        
        public static void SyncWithNetworkObject(this NetworkBehaviour networkBehaviour)
        {
            if (!networkBehaviour.NetworkObject.ChildNetworkBehaviours.Contains(networkBehaviour))
                networkBehaviour.NetworkObject.ChildNetworkBehaviours.Add(networkBehaviour);
            networkBehaviour.UpdateNetworkProperties();
        }
        
        private static bool MethodPatchInternal(NetworkBehaviour networkBehaviour, MethodBase original, object[] args)
        {
            if (!NetworkManager.Singleton ||
                !(NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsConnectedClient))
            {
                Plugin.LogSource.LogError($"NetworkBehaviour {networkBehaviour.NetworkBehaviourId} tried to send a RPC but the NetworkManager {(NetworkManager.Singleton ? "isn't active!" : "is null!")}");
                return false;
            }

            var rpcSource = RpcSource;
            RpcSource = RpcState.FromUser;
            RpcAttribute rpcAttribute = (RpcAttribute)original.GetCustomAttribute<ServerRpcAttribute>() ?? original.GetCustomAttribute<ClientRpcAttribute>();

            switch (rpcSource)
            {
                case RpcState.FromUser when rpcAttribute is ServerRpcAttribute { RequireOwnership: true } &&
                                            networkBehaviour.OwnerClientId != NetworkManager.Singleton.LocalClientId:
                {
                    Plugin.LogSource.LogError(
                        $"Tried to run ServerRPC {original.Name} but we're not the owner of NetworkObject {networkBehaviour.NetworkObjectId}");
                    return false;
                }
                case RpcState.FromUser when rpcAttribute is ClientRpcAttribute &&
                                            !(NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost):
                {
                    Plugin.LogSource.LogError(
                        $"Tried to run ClientRpc {original.Name} but we're not a host!" +
                        " You should only call ClientRpc(s) from inside a ServerRpc OR if you've checked with IsHost!");
                    return false;
                }
                case RpcState.FromNetworking when rpcAttribute is ServerRpcAttribute &&
                                                  !(networkBehaviour.IsServer || networkBehaviour.IsHost):
                {
                    Plugin.LogSource.LogError($"Received message to run ServerRPC {original.DeclaringType}.{original.Name} but we're a client!");
                    return false;
                }
                case RpcState.FromUser:
                    var writer = new FastBufferWriter((original.GetParameters().Length + 1) * 128, Allocator.Temp);
                    writer.WriteValueSafe(networkBehaviour.NetworkObjectId);
                    writer.WriteValueSafe(networkBehaviour.NetworkBehaviourId);

                    writer.WriteMethodInfoAndParameters(original, args);

                    var delivery = rpcAttribute.Delivery == RpcDelivery.Reliable
                        ? NetworkDelivery.Reliable
                        : NetworkDelivery.Unreliable;
            
                    if (rpcAttribute is ServerRpcAttribute)
                        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage($"{NetcodeValidator.TypeCustomMessageHandlerPrefix}.{original.DeclaringType!.Name}", NetworkManager.ServerClientId, writer, delivery);
                    else
                        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll($"{NetcodeValidator.TypeCustomMessageHandlerPrefix}.{original.DeclaringType!.Name}", writer, delivery);

                    return false;
                case RpcState.FromNetworking when rpcAttribute != null:
                    return true;
                default:
                {
                    Plugin.LogSource.LogError($"NetworkBehaviour {networkBehaviour.NetworkBehaviourId} tried to validate {original.DeclaringType}.{original.Name}, " +
                                              "and while it exists, the method is not marked with a corresponding RPCAttribute. Please prefix with ServerRPC or ClientRPC!");
                    return false;
                }
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
                    out var networkObject))
            {
                Plugin.LogSource.LogError("An RPC called on a NetworkObject that is not in the spawned objects list." +
                                          " Please make sure the NetworkObject is spawned before calling RPCs.");
                return;
            }


            var networkBehaviour = networkObject!.GetNetworkBehaviourAtOrderIndex(networkBehaviourId);
            var method = networkBehaviour.GetType().GetMethod(rpcName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                Plugin.LogSource.LogError(
                    $"NetworkBehaviour {networkBehaviour.NetworkBehaviourId} received RPC {rpcName}" +
                    $" but that method doesn't exist on {networkBehaviour.GetType()}!");
                return;
            }

            var serverAttribute = method!.GetCustomAttribute<ServerRpcAttribute>();
            if (serverAttribute != null && !networkBehaviour.IsHost && !networkBehaviour.IsServer)
            {
                Plugin.LogSource.LogError(
                    $"NetworkBehaviour {networkBehaviour.NetworkBehaviourId} received ServerRPC {rpcName} but we're a client!");
                return;
            }

            if ((serverAttribute?.RequireOwnership ?? false) && sender != networkObject.OwnerClientId)
            {
                Plugin.LogSource.LogError(
                    $"NetworkBehaviour {networkBehaviour.NetworkBehaviourId} received ServerRPC but the sender wasn't the owner!");
                return;
            }
            
            RpcSource = RpcState.FromNetworking;
            LastSenderId = sender;
            
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