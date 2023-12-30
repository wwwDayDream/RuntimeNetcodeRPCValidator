using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Unity.Netcode;
using Debug = UnityEngine.Debug;

namespace RuntimeNetcodeRPCValidator
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private Harmony harmony = new Harmony(PluginInfo.GUID);

        private void Awake()
        {
            harmony.Patch(AccessTools.Method(typeof(NetworkManager), nameof(NetworkManager.Initialize)),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(OnNetworkManagerInitialized))));
        }

        private static void OnNetworkManagerInitialized()
        {
            NetcodeValidator.PatchImmediate = true;
            foreach (var plugin in NetcodeValidator.RegisteredPlugins)
            {
                plugin.Value.TryPatch();
            }
        }

        private static void OnNetworkManagerShutdown()
        {
            NetcodeValidator.PatchImmediate = false;
            foreach (var plugin in NetcodeValidator.RegisteredPlugins)
            {
                plugin.Value.Unpatch();
            }
        }
    }

    public class AlreadyPatchedException : Exception
    {
        public AlreadyPatchedException(string PluginGUID) : base(
            $"Can't patch plugin {PluginGUID} until the other instance of NetcodeValidator is Disposed of!") {}
    }
    public struct NetcodeValidator : IDisposable
    {
        internal static bool PatchImmediate = false;
        
        internal static readonly Dictionary<string, NetcodeValidator> RegisteredPlugins = new Dictionary<string, NetcodeValidator>();

        private readonly BaseUnityPlugin plugin;
        private readonly Harmony patcher;
        private bool patched;
        private string[] customMessageHandlers;
        internal void TryPatch()
        {
            if (!PatchImmediate) return;
            var allTypes = plugin.GetType().Assembly.GetTypes().Where(type =>
                type.BaseType == typeof(NetworkBehaviour)).ToArray();
            customMessageHandlers = new string[allTypes.Length];
            for (var i = 0; i < allTypes.Length; i++)
            {
                customMessageHandlers[i] = $"Net.{allTypes[i].Name}";
                NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(customMessageHandlers[i],
                    NetworkBehaviourExtensions.ReceiveNetworkMessage);
            }

            foreach (var method in allTypes.SelectMany(type =>
                         type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)))
            {
                var nameEndsWithServer = method.Name.EndsWith("ServerRpc");
                var nameEndsWithClient = method.Name.EndsWith("ClientRpc");
                var hasServerAttr = method.GetCustomAttributes<ServerRpcAttribute>().Any();
                var hasClientAttr = method.GetCustomAttributes<ClientRpcAttribute>().Any();
                switch (hasServerAttr)
                {
                    case false when !hasClientAttr && !nameEndsWithServer && !nameEndsWithClient:
                        continue;
                    case false when !hasClientAttr:
                        Debug.LogError($"[Network] Can't patch method {method.DeclaringType?.Name}.{method.Name} because it lacks a [{(nameEndsWithClient ? "Client" : "Server")}Rpc] attribute.");
                        continue;
                }

                if ((hasServerAttr && !nameEndsWithServer) ||
                    (hasClientAttr && !nameEndsWithClient))
                {
                    Debug.LogError($"[Network] Can't patch method {method.DeclaringType?.Name}.{method.Name} because it doesn't end with {(method.GetCustomAttribute<ServerRpcAttribute>() != null ? "Server" : "Client")}Rpc.");
                    continue;
                }
                HarmonyMethod harmonyMethod = null;
                switch (method.GetParameters().Length)
                {
                    case 0:
                        harmonyMethod = new HarmonyMethod(AccessTools.Method(typeof(NetworkBehaviourExtensions),
                            nameof(NetworkBehaviourExtensions.MethodPatch0)));
                        break;
                    case 1:
                        harmonyMethod = new HarmonyMethod(AccessTools.Method(typeof(NetworkBehaviourExtensions),
                            nameof(NetworkBehaviourExtensions.MethodPatch1)));
                        break;
                    case 2:
                        harmonyMethod = new HarmonyMethod(AccessTools.Method(typeof(NetworkBehaviourExtensions),
                            nameof(NetworkBehaviourExtensions.MethodPatch2)));
                        break;
                    case 3:
                        harmonyMethod = new HarmonyMethod(AccessTools.Method(typeof(NetworkBehaviourExtensions),
                            nameof(NetworkBehaviourExtensions.MethodPatch3)));
                        break;
                    case 4:
                        harmonyMethod = new HarmonyMethod(AccessTools.Method(typeof(NetworkBehaviourExtensions),
                            nameof(NetworkBehaviourExtensions.MethodPatch4)));
                        break;
                    case 5:
                        harmonyMethod = new HarmonyMethod(AccessTools.Method(typeof(NetworkBehaviourExtensions),
                            nameof(NetworkBehaviourExtensions.MethodPatch5)));
                        break;
                    case 6:
                        harmonyMethod = new HarmonyMethod(AccessTools.Method(typeof(NetworkBehaviourExtensions),
                            nameof(NetworkBehaviourExtensions.MethodPatch6)));
                        break;
                    default:
                        Debug.LogError($"[Network] Can't patch method {method.DeclaringType?.Name}.{method.Name} as we don't support that many arguments. Please convert them to a [Serializable] object.");
                        continue;
                }
                Debug.Log($"[Network] Patching {method.DeclaringType?.Name}.{method.Name} as {(method.GetCustomAttribute<ServerRpcAttribute>() != null ? "Server" : "Client")}Rpc.");
                if (harmonyMethod != null)
                    patcher.Patch(method, prefix: harmonyMethod);
            }

            patched = true;
        }

        public NetcodeValidator(BaseUnityPlugin plugin)
        {
            var pluginGuid = plugin.Info.Metadata.GUID;
            
            if (RegisteredPlugins.ContainsKey(pluginGuid))
                throw new AlreadyPatchedException(pluginGuid);
            
            this.plugin = plugin;
            patched = false;
            customMessageHandlers = Array.Empty<string>();
            patcher = new Harmony(PluginInfo.GUID + "." + pluginGuid);
            
            RegisteredPlugins.Add(pluginGuid, this);
            
            TryPatch();
        }

        public void Unpatch()
        {
            foreach (var t in customMessageHandlers)
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(t);
            customMessageHandlers = Array.Empty<string>();
            if (patched)
                patcher.UnpatchSelf();
        }
        public void Dispose()
        {
            Unpatch();
        }
    }
}
