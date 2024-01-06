using System;
using System.Reflection;
using Unity.Netcode;

namespace RuntimeNetcodeRPCValidator
{
    internal static class TextHandler
    {
        private const string NoNetworkManagerPresentToSendRpcConst =
            "NetworkBehaviour {0} tried to send a RPC but the NetworkManager is non-existant!";
        
        private const string MethodLacksAttributeConst =
            "Can't patch method {0}.{1} because it lacks a [{2}] attribute.";

        private const string MethodLacksSuffixConst =
            "Can't patch method {0}.{1} because it's name doesn't end with '{2}'!";

        private const string SuccessfullyPatchedTypeConst =
            "Patched {0} ServerRPC{1} & {2} ClientRPC{3} on NetworkBehaviour {4}.";

        private const string NotOwnerOfNetworkObjectConst =
            "{0} tried to run ServerRPC {1} but not the owner of NetworkObject {2}";

        private const string CantRunClientRpcFromClientConst =
            "Tried to run ClientRpc {0} but we're not a host! " +
            "You should only call ClientRpc(s) from inside a ServerRpc " +
            "OR if you've checked you're on the server with IsHost!";
        
        private const string CantRunServerRpcAsClientConst = 
            "Received message to run ServerRPC {0}.{1} but we're a client!";

        private const string MethodPatchedButLacksAttributesConst =
            "Rpc Method {0} has been patched to attempt networking but lacks any RpcAttributes! " +
            "This should never happen!";
        
        private const string MethodPatchedAndNetworkCalledButLacksAttributesConst =
            "Rpc Method {0} has been patched && even received a network call to execute but lacks any RpcAttributes! " +
            "This should never happen! Something is VERY fucky!!!";

        private const string RpcCalledBeforeObjectSpawnedConst =
            "An RPC called on a NetworkObject that is not in the spawned objects list." +
            " Please make sure the NetworkObject is spawned before calling RPCs.";

        private const string NetworkCalledNonExistentMethodConst =
            "NetworkBehaviour {0} received RPC {1}" +
            " but that method doesn't exist on {2}!";

        private const string ObjectNotSerializableConst =
            "[Network] Parameter ({0} {1}) is not marked [Serializable] nor does it implement INetworkSerializable!";

        private const string InconsistentParameterCountConst =
            "[Network] NetworkBehaviour received a RPC {0} " +
            "but the number of parameters sent {1} != MethodInfo param count {2}";
        
        private const string PluginTriedToBindToPreExistingObjectTooLateConst = 
            "Plugin '{0}' tried to bind {1} to {2} but it's too late! " +
            "Make sure you bind to any pre-existing NetworkObjects before " +
            "NetworkManager.IsListening || IsConnectedClient.";
        
        private const string RegisteredPatchForTypeConst = 
            "Successfully registered first patch for type {0}.{1} | Triggered by {2}";
        
        private const string CustomComponentAddedToExistingObjectConst = 
            "Successfully added {0} to {1} via {2}. Triggered by plugin {3}";
        
        internal static string NoNetworkManagerPresentToSendRpc(NetworkBehaviour networkBehaviour) =>
            string.Format(NoNetworkManagerPresentToSendRpcConst, networkBehaviour.NetworkBehaviourId);
        
        internal static string MethodLacksRpcAttribute(MethodInfo method) =>
            string.Format(MethodLacksAttributeConst, method.DeclaringType?.Name, method.Name,
                method.Name.EndsWith("ServerRpc") ? "ServerRpc" : "ClientRpc");

        internal static string MethodLacksSuffix(MethodBase method) =>
            string.Format(MethodLacksSuffixConst, method.DeclaringType?.Name, method.Name,
                method.GetCustomAttribute<ServerRpcAttribute>() != null ? "ServerRpc" : "ClientRpc");

        internal static string SuccessfullyPatchedType(Type networkType, int serverRpcCount, int clientRpcCount) =>
            string.Format(SuccessfullyPatchedTypeConst, serverRpcCount, serverRpcCount == 1 ? "" : "s",
                clientRpcCount, clientRpcCount == 1 ? "" : "s", networkType.Name);

        internal static string NotOwnerOfNetworkObject(string whoIsNotOwner, MethodBase method, NetworkObject networkObject) =>
            string.Format(NotOwnerOfNetworkObjectConst, whoIsNotOwner, method.Name, networkObject.NetworkObjectId);

        internal static string CantRunClientRpcFromClient(MethodBase method) =>
            string.Format(CantRunClientRpcFromClientConst, method.Name);
        
        internal static string CantRunServerRpcAsClient(MethodBase method) =>
            string.Format(CantRunServerRpcAsClientConst, method.DeclaringType?.Name, method.Name);

        internal static string MethodPatchedButLacksAttributes(MethodBase method) =>
            string.Format(MethodPatchedButLacksAttributesConst, method.Name);

        internal static string MethodPatchedAndNetworkCalledButLacksAttributes(MethodBase method) =>
            string.Format(MethodPatchedAndNetworkCalledButLacksAttributesConst, method.Name);

        internal static string RpcCalledBeforeObjectSpawned() => 
            RpcCalledBeforeObjectSpawnedConst;

        internal static string NetworkCalledNonExistentMethod(NetworkBehaviour networkBehaviour, string rpcName) =>
            string.Format(NetworkCalledNonExistentMethodConst, 
                networkBehaviour.NetworkBehaviourId, rpcName, networkBehaviour.GetType().Name);

        internal static string ObjectNotSerializable(ParameterInfo paramInfo) =>
            string.Format(ObjectNotSerializableConst, paramInfo.ParameterType.Name, paramInfo.Name);

        internal static string InconsistentParameterCount(MethodBase method, int paramsSent) =>
            string.Format(InconsistentParameterCountConst, method.Name, paramsSent, method.GetParameters().Length);

        internal static string PluginTriedToBindToPreExistingObjectTooLate(NetcodeValidator netcodeValidator, 
            Type from, Type to) =>
            string.Format(PluginTriedToBindToPreExistingObjectTooLateConst, netcodeValidator.PluginGuid, 
                from.Name, to.Name);
        internal static string RegisteredPatchForType(NetcodeValidator validator, Type netBehaviour, MethodBase method) =>
            string.Format(RegisteredPatchForTypeConst, netBehaviour.Name, method.Name, validator.PluginGuid);

        internal static string CustomComponentAddedToExistingObject(
            (NetcodeValidator validator, Type custom, Type native) it,
            MethodBase methodBase) =>
            string.Format(CustomComponentAddedToExistingObjectConst, it.custom.Name, it.native.Name, methodBase.Name,
                it.validator.PluginGuid);
    }
}