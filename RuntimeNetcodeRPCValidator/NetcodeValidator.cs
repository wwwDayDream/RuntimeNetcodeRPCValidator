using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Unity.Netcode;

namespace RuntimeNetcodeRPCValidator
{
    public sealed class NetcodeValidator : IDisposable
    {
        private static readonly List<string> AlreadyRegistered = new List<string>();
        public const string TypeCustomMessageHandlerPrefix = "Net";
        
        private List<string> CustomMessageHandlers { get; }
        private Harmony Patcher { get; }
        private string PluginGuid { get; }
        private event Action<string> AddedNewCustomMessageHandler;

        public NetcodeValidator(string pluginGuid)
        {
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(pluginGuid, out _))
                throw new InvalidPluginGuidException(pluginGuid);
            if (AlreadyRegistered.Contains(pluginGuid))
                throw new AlreadyRegisteredException(pluginGuid);
            AlreadyRegistered.Add(pluginGuid);

            PluginGuid = pluginGuid;
            CustomMessageHandlers = new List<string>();
            Patcher = new Harmony(pluginGuid + MyPluginInfo.PLUGIN_GUID);

            Plugin.NetworkManagerInitialized += NetworkManagerInitialized;
            Plugin.NetworkManagerShutdown += NetworkManagerShutdown;
        }
        
        internal static void OnNetworkBehaviourConstructed(object __instance)
        {
            if (!(__instance is NetworkBehaviour networkBehaviour))
                return;
            if (networkBehaviour.NetworkObject == null || networkBehaviour.NetworkManager == null)
            {
                Plugin.LogSource.LogError(
                    $"NetworkBehaviour {__instance.GetType()} is trying to sync with the NetworkObject but the {(networkBehaviour.NetworkObject == null ? "NetworkObject" : "NetworkManager")} is null!");
                return;
            }
            networkBehaviour.SyncWithNetworkObject();
        }

        private void Patch(MethodInfo rpcMethod)
        {
            var isServerRpc = rpcMethod.GetCustomAttributes<ServerRpcAttribute>().Any();
            var isClientRpc = rpcMethod.GetCustomAttributes<ClientRpcAttribute>().Any();
            var endsWithServerRpc = rpcMethod.Name.EndsWith("ServerRpc");
            var endsWithClientRpc = rpcMethod.Name.EndsWith("ClientRpc");
            if (!isClientRpc && !isServerRpc && !endsWithClientRpc && !endsWithServerRpc)
                return;
            if (!isServerRpc && endsWithServerRpc)
            {
                Plugin.LogSource.LogError($"Can't patch method {rpcMethod.DeclaringType?.Name}.{rpcMethod.Name} because it lacks a [ServerRpc] attribute.");
                return;
            }
            if (!isClientRpc && endsWithClientRpc)
            {
                Plugin.LogSource.LogError($"Can't patch method {rpcMethod.DeclaringType?.Name}.{rpcMethod.Name} because it lacks a [ClientRpc] attribute.");
                return;
            }
            if (isServerRpc && !endsWithServerRpc)
            {
                Plugin.LogSource.LogError($"Can't patch method {rpcMethod.DeclaringType?.Name}.{rpcMethod.Name} because it's name doesn't end with 'ServerRpc'!");
                return;
            }
            if (isClientRpc && !endsWithClientRpc)
            {
                Plugin.LogSource.LogError($"Can't patch method {rpcMethod.DeclaringType?.Name}.{rpcMethod.Name} because it's name doesn't end with 'ClientRpc'!");
                return;
            }
                
            Plugin.LogSource.LogInfo($"Patching {rpcMethod.DeclaringType?.Name}.{rpcMethod.Name} as {(isServerRpc ? "Server" : "Client")}Rpc.");
            Patcher.Patch(rpcMethod,
                new HarmonyMethod(typeof(NetworkBehaviourExtensions),
                    nameof(NetworkBehaviourExtensions.MethodPatch)));
        }

        /// <summary>
        /// Applies dynamic patches to the specified NetworkBehaviour.
        /// </summary>
        /// <param name="networkBehaviour">The type of NetworkBehaviour to patch</param>
        /// <exception cref="NotNetworkBehaviourException">Thrown when the specified type is not derived from NetworkBehaviour</exception>
        public void Patch(Type networkBehaviour)
        {
            if (networkBehaviour.BaseType != typeof(NetworkBehaviour))
                throw new NotNetworkBehaviourException(networkBehaviour);

            Patcher.Patch(
                AccessTools.Constructor(networkBehaviour),
                new HarmonyMethod(typeof(NetcodeValidator), nameof(OnNetworkBehaviourConstructed)));
            
            CustomMessageHandlers.Add($"{TypeCustomMessageHandlerPrefix}.{networkBehaviour.Name}");
            OnAddedNewCustomMessageHandler(CustomMessageHandlers.Last());

            foreach (var method in networkBehaviour.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                               BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                Patch(method);
        }

        /// <summary>
        /// Patches all types in the given assembly that inherit from NetworkBehaviour.
        /// </summary>
        /// <param name="assembly">The assembly to patch.</param>
        public void Patch(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
                if (type.BaseType == typeof(NetworkBehaviour))
                    Patch(type);
        }

        /// <summary>
        /// Patches all methods by iterating through the assembly of the calling method's DeclaredType..
        /// </summary>
        /// <exception cref="MustCallFromDeclaredTypeException">Thrown when the method is not called from a declared type.</exception>
        public void PatchAll()
        {
            var assembly = new StackTrace().GetFrame(1).GetMethod().ReflectedType?.Assembly;
            if (assembly == null)
                throw new MustCallFromDeclaredTypeException();
            Patch(assembly);
        }

        public void UnpatchSelf()
        {
            Patcher.UnpatchSelf();
        }

        private static void RegisterMessageHandlerWithNetworkManager(string handler) =>
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(handler,
                NetworkBehaviourExtensions.ReceiveNetworkMessage);

        private void NetworkManagerInitialized()
        {
            AddedNewCustomMessageHandler += RegisterMessageHandlerWithNetworkManager;
            
            foreach (var customMessageHandler in CustomMessageHandlers)
                RegisterMessageHandlerWithNetworkManager(customMessageHandler);
        }

        private void NetworkManagerShutdown()
        {
            AddedNewCustomMessageHandler -= RegisterMessageHandlerWithNetworkManager;
            foreach (var customMessageHandler in CustomMessageHandlers)
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(customMessageHandler);
        }

        public void Dispose()
        {
            Plugin.NetworkManagerInitialized -= NetworkManagerInitialized;
            Plugin.NetworkManagerShutdown -= NetworkManagerShutdown;
            
            AlreadyRegistered.Remove(PluginGuid);
            
            if (NetworkManager.Singleton)
                NetworkManagerShutdown();

            if (Patcher.GetPatchedMethods().Any())
                UnpatchSelf();
        }

        private void OnAddedNewCustomMessageHandler(string obj)
        {
            AddedNewCustomMessageHandler?.Invoke(obj);
        }
    }
}