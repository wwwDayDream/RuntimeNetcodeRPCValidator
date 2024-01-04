using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Collections;
using Unity.Netcode;

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

        private static bool ValidateRPCMethod(NetworkBehaviour networkBehaviour, 
            MethodBase method, RpcState state, out RpcAttribute rpcAttribute)
        {
            var isServerRpcAttr = method.GetCustomAttributes<ServerRpcAttribute>().Any();
            var isClientRpcAttr = method.GetCustomAttributes<ClientRpcAttribute>().Any();
            var requiresOwnership = isServerRpcAttr && 
                                    method.GetCustomAttribute<ServerRpcAttribute>().RequireOwnership;

            rpcAttribute = isServerRpcAttr
                ? (RpcAttribute)method.GetCustomAttribute<ServerRpcAttribute>()
                : method.GetCustomAttribute<ClientRpcAttribute>();
            
            if (requiresOwnership && networkBehaviour.OwnerClientId != NetworkManager.Singleton.LocalClientId)
            {
                Plugin.LogSource.LogError(
                    TextHandler.NotOwnerOfNetworkObject(
                        state == RpcState.FromUser ? "We" : "Client " + LastSenderId, 
                        method, networkBehaviour.NetworkObject));
                return false;
            }
            if (state == RpcState.FromUser && isClientRpcAttr &&
                !(NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost))
            {
                Plugin.LogSource.LogError(TextHandler.CantRunClientRpcFromClient(method));
                return false;
            }
            if (state == RpcState.FromUser && !isServerRpcAttr && !isClientRpcAttr)
            {
                Plugin.LogSource.LogError(TextHandler.MethodPatchedButLacksAttributes(method));
                return false;
            }
            if (state == RpcState.FromNetworking && !isServerRpcAttr && !isClientRpcAttr)
            {
                Plugin.LogSource.LogError(TextHandler.MethodPatchedAndNetworkCalledButLacksAttributes(method));
                return false;
            }
            if (state == RpcState.FromNetworking && isServerRpcAttr &&
                !(networkBehaviour.IsServer || networkBehaviour.IsHost))
            {
                Plugin.LogSource.LogError(TextHandler.CantRunServerRpcAsClient(method));
                return false;
            }
            
            return true;
        }
        
        private static bool MethodPatchInternal(NetworkBehaviour networkBehaviour, MethodBase method, 
            object[] args)
        {
            if (!NetworkManager.Singleton ||
                !(NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsConnectedClient))
            {
                Plugin.LogSource.LogError(TextHandler.NoNetworkManagerPresentToSendRpc(networkBehaviour));
                return false;
            }

            var rpcSource = RpcSource;
            RpcSource = RpcState.FromUser;

            if (rpcSource == RpcState.FromNetworking) return true;
            
            if (!ValidateRPCMethod(networkBehaviour, method, rpcSource, out var rpcAttribute))
                return false;
                    
            var writer = new FastBufferWriter((method.GetParameters().Length + 1) * 128, Allocator.Temp);
            writer.WriteValueSafe(networkBehaviour.NetworkObjectId);
            writer.WriteValueSafe(networkBehaviour.NetworkBehaviourId);

            writer.WriteMethodInfoAndParameters(method, args);

            var messageChannel = new StringBuilder(NetcodeValidator.TypeCustomMessageHandlerPrefix)
                .Append(".")
                .Append(method.DeclaringType!.Name).ToString();
            var delivery = rpcAttribute.Delivery == RpcDelivery.Reliable
                ? NetworkDelivery.Reliable
                : NetworkDelivery.Unreliable;
            
            
            
            if (rpcAttribute is ServerRpcAttribute)
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                    messageChannel, NetworkManager.ServerClientId, writer, delivery);
            else
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(
                    messageChannel, writer, delivery);

            return false;

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
                Plugin.LogSource.LogError(TextHandler.RpcCalledBeforeObjectSpawned());
                return;
            }

            var networkBehaviour = networkObject!.GetNetworkBehaviourAtOrderIndex(networkBehaviourId);
            const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var method = networkBehaviour.GetType().GetMethod(rpcName, bindingFlags);
            if (method == null)
            {
                Plugin.LogSource.LogError(TextHandler.NetworkCalledNonExistentMethod(networkBehaviour, rpcName));
                return;
            }

            if (!ValidateRPCMethod(networkBehaviour, method, RpcState.FromNetworking, out _))
                return;
            
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