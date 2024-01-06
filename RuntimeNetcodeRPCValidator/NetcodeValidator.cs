using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Unity.Netcode;

namespace RuntimeNetcodeRPCValidator
{
    public sealed class NetcodeValidator : IDisposable
    {
        private static List<(NetcodeValidator validator, Type custom, Type native)> BoundNetworkObjects { get; } =
            new List<(NetcodeValidator, Type, Type)>();
        internal static event Action<NetcodeValidator, Type> AddedNewBoundBehaviour;
        
        private static readonly List<string> AlreadyRegistered = new List<string>();
        internal const string TypeCustomMessageHandlerPrefix = "Net";
        
        private List<string> CustomMessageHandlers { get; }
        private Harmony Patcher { get; }
        public string PluginGuid { get; }
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
        

        internal static void TryLoadRelatedComponentsInOrder(NetworkBehaviour __instance, MethodBase __originalMethod)
        {
            var items = BoundNetworkObjects.Where(obj =>
                    obj.native == __originalMethod.DeclaringType)
                .OrderBy(it => it.validator.PluginGuid);
            
            foreach (var it in items)
            {
                Plugin.Logger.LogInfo(TextHandler.CustomComponentAddedToExistingObject(it, __originalMethod));
                (__instance.gameObject.AddComponent(it.custom) as NetworkBehaviour).SyncWithNetworkObject();
            }
        }

        private bool Patch(MethodInfo rpcMethod, out bool isServerRpc, out bool isClientRpc)
        {
            isServerRpc = rpcMethod.GetCustomAttributes<ServerRpcAttribute>().Any();
            isClientRpc = rpcMethod.GetCustomAttributes<ClientRpcAttribute>().Any();
            var endsWithServerRpc = rpcMethod.Name.EndsWith("ServerRpc");
            var endsWithClientRpc = rpcMethod.Name.EndsWith("ClientRpc");
            if (!isClientRpc && !isServerRpc && !endsWithClientRpc && !endsWithServerRpc)
                return false;
            if ((!isServerRpc && endsWithServerRpc) || (!isClientRpc && endsWithClientRpc))
            {
                Plugin.Logger.LogError(TextHandler.MethodLacksRpcAttribute(rpcMethod));
                return false;
            }
            if ((isServerRpc && !endsWithServerRpc) || (isClientRpc && !endsWithClientRpc))
            {
                Plugin.Logger.LogError(TextHandler.MethodLacksSuffix(rpcMethod));
                return false;
            }
            Patcher.Patch(rpcMethod,
                new HarmonyMethod(typeof(NetworkBehaviourExtensions),
                    nameof(NetworkBehaviourExtensions.MethodPatch)));
            return true;
        }

        public void BindToPreExistingObjectByBehaviour<TCustomBehaviour, TNativeBehaviour>() 
            where TCustomBehaviour : NetworkBehaviour where TNativeBehaviour : NetworkBehaviour
        {
            if (NetworkManager.Singleton &&
                (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsConnectedClient))
            {
                Plugin.Logger.LogError(
                    TextHandler.PluginTriedToBindToPreExistingObjectTooLate(this, 
                        typeof(TCustomBehaviour), typeof(TNativeBehaviour)));
                return;
            }
            
            OnAddedNewBoundBehaviour(this, typeof(TCustomBehaviour), typeof(TNativeBehaviour));
        }
        /// <summary>
        /// Applies dynamic patches to the specified NetworkBehaviour.
        /// </summary>
        /// <param name="netBehaviourTyped">The type of NetworkBehaviour to patch</param>
        /// <exception cref="NotNetworkBehaviourException">Thrown when the specified type is not derived from NetworkBehaviour</exception>
        public void Patch(Type netBehaviourTyped)
        {
            if (netBehaviourTyped.BaseType != typeof(NetworkBehaviour))
                throw new NotNetworkBehaviourException(netBehaviourTyped);

            OnAddedNewCustomMessageHandler($"{TypeCustomMessageHandlerPrefix}.{netBehaviourTyped.Name}");

            var serverRPCsPatched = 0;
            var clientRPCsPatched = 0;
            foreach (var method in netBehaviourTyped.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                               BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!Patch(method, out var isServerRpc, out var isClientRpc)) continue;
                serverRPCsPatched += isServerRpc ? 1 : 0;
                clientRPCsPatched += isClientRpc ? 1 : 0;
            }
            
            Plugin.Logger.LogInfo(TextHandler.SuccessfullyPatchedType(netBehaviourTyped, serverRPCsPatched, clientRPCsPatched));
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
            Plugin.Logger.LogInfo(TextHandler.PluginUnpatchedAllRPCs(this));
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
            CustomMessageHandlers.Add(obj);
            AddedNewCustomMessageHandler?.Invoke(obj);
        }

        private static void OnAddedNewBoundBehaviour(NetcodeValidator validator, Type custom, Type native)
        {
            BoundNetworkObjects.Add((validator, custom, native));
            AddedNewBoundBehaviour?.Invoke(validator, native);
        }
    }
}