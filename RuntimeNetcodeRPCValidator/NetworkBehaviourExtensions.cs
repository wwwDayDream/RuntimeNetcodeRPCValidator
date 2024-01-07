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

        private static RpcState RpcSource;


        public static ClientRpcParams CreateSendToFromReceived(this ServerRpcParams senderId) =>
            new ClientRpcParams()
                { Send = new ClientRpcSendParams()
                {
                    TargetClientIds = new[] { senderId.Receive.SenderClientId }
                } };
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
                Plugin.Logger.LogError(
                    TextHandler.NotOwnerOfNetworkObject(
                        state == RpcState.FromUser ? "We" : "Client", 
                        method, networkBehaviour.NetworkObject));
                return false;
            }
            if (state == RpcState.FromUser && isClientRpcAttr &&
                !(NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost))
            {
                Plugin.Logger.LogError(TextHandler.CantRunClientRpcFromClient(method));
                return false;
            }
            if (state == RpcState.FromUser && !isServerRpcAttr && !isClientRpcAttr)
            {
                Plugin.Logger.LogError(TextHandler.MethodPatchedButLacksAttributes(method));
                return false;
            }
            if (state == RpcState.FromNetworking && !isServerRpcAttr && !isClientRpcAttr)
            {
                Plugin.Logger.LogError(TextHandler.MethodPatchedAndNetworkCalledButLacksAttributes(method));
                return false;
            }
            if (state == RpcState.FromNetworking && isServerRpcAttr &&
                !(networkBehaviour.IsServer || networkBehaviour.IsHost))
            {
                Plugin.Logger.LogError(TextHandler.CantRunServerRpcAsClient(method));
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
                Plugin.Logger.LogError(TextHandler.NoNetworkManagerPresentToSendRpc(networkBehaviour));
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
                .Append(method.DeclaringType.Name).ToString();
            var delivery = rpcAttribute.Delivery == RpcDelivery.Reliable
                ? NetworkDelivery.Reliable
                : NetworkDelivery.Unreliable;
            
            if (rpcAttribute is ServerRpcAttribute)
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                    messageChannel, NetworkManager.ServerClientId, writer, delivery);
            else
            {
                var paramsToWorkWith = method.GetParameters();
                var isLastItemClientRpcAttr = paramsToWorkWith.Length > 0 &&
                    paramsToWorkWith[paramsToWorkWith.Length - 1].ParameterType == typeof(ClientRpcParams);
                
                if (isLastItemClientRpcAttr)
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                        messageChannel, ((ClientRpcParams)args[args.Length - 1]).Send.TargetClientIds, writer, delivery);
                else
                    NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(
                    messageChannel, writer, delivery);
            }

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
                Plugin.Logger.LogError(TextHandler.RpcCalledBeforeObjectSpawned());
                return;
            }

            var networkBehaviour = networkObject.GetNetworkBehaviourAtOrderIndex(networkBehaviourId);
            const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var method = networkBehaviour.GetType().GetMethod(rpcName, bindingFlags);
            if (method == null)
            {
                Plugin.Logger.LogError(TextHandler.NetworkCalledNonExistentMethod(networkBehaviour, rpcName));
                return;
            }

            if (!ValidateRPCMethod(networkBehaviour, method, RpcState.FromNetworking, out var rpcAttribute))
                return;

            RpcSource = RpcState.FromNetworking;
            
            var paramsToWorkWith = method.GetParameters();
            var isLastItemServerRpcAttr = rpcAttribute is ServerRpcAttribute && 
                                          paramsToWorkWith.Length > 0 &&
                                          paramsToWorkWith[paramsToWorkWith.Length - 1].ParameterType == typeof(ServerRpcParams);

            object[] methodParams = null;
            if (paramsToWorkWith.Length > 0)
                methodParams = new object[paramsToWorkWith.Length];
            
            reader.ReadMethodInfoAndParameters(method.DeclaringType, ref methodParams);

            if (isLastItemServerRpcAttr)
                methodParams[methodParams.Length - 1] = new ServerRpcParams()
                {
                    Receive = new ServerRpcReceiveParams()
                    {
                        SenderClientId = sender
                    }
                };
            
            method.Invoke(networkBehaviour, methodParams);
        }

        internal static bool MethodPatch(NetworkBehaviour __instance, MethodBase __originalMethod, object[] __args)
        {
            return MethodPatchInternal(__instance, __originalMethod, __args);
        }
    }
}