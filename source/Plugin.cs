using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RuntimeNetcodeRPCValidator
{
    public struct NetcodeValidator : IDisposable
    {
        private static readonly List<Type> AlreadyRegistered = new List<Type>();
        public static readonly List<NetcodeValidator> Validators = new List<NetcodeValidator>();
        private Harmony Patcher { get; set; }
        private string[] CustomMessageHandlers { get; set; }
        private readonly Type pluginType;
        private List<(MethodBase original, HarmonyMethod patch, bool prefix)> Patches { get; set; }

        public NetcodeValidator(BaseUnityPlugin plugin)
        {
            pluginType = plugin.GetType();
            if (AlreadyRegistered.Contains(pluginType))
                throw new AlreadyPatchedException(plugin.Info.Metadata.GUID);
            AlreadyRegistered.Add(pluginType);
            
            Patcher = new Harmony(plugin.Info.Metadata.GUID + PluginInfo.GUID);
            
            var allTypes = pluginType.Assembly.GetTypes().Where(type =>
                type.BaseType == typeof(NetworkBehaviour)).ToArray();
            CustomMessageHandlers = new string[allTypes.Length];
            Patches = new List<(MethodBase original, HarmonyMethod patch, bool prefix)>();
            for (var i = 0; i < allTypes.Length; i++)
            {
                CustomMessageHandlers[i] = $"Net.{allTypes[i].Name}";

                Patches.Add((AccessTools.Constructor(allTypes[i]), new HarmonyMethod(typeof(Plugin), nameof(Plugin.UpdateNetworkProperties)), false));
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
                Debug.Log($"[Network] Patching {method.DeclaringType?.Name}.{method.Name} as {(method.GetCustomAttribute<ServerRpcAttribute>() != null ? "Server" : "Client")}Rpc.");
                Patches.Add((method, new HarmonyMethod(typeof(NetworkBehaviourExtensions), nameof(NetworkBehaviourExtensions.MethodPatch)), true));
            }
            Validators.Add(this);
        }

        public void PatchAll()
        {
            foreach (var (original, patch, prefix) in Patches)
                Patcher.Patch(original, prefix: prefix ? patch : null, postfix: !prefix ? patch : null);
        }
        // ReSharper disable once MemberCanBePrivate.Global
        public void UnpatchSelf()
        {
            Patcher.UnpatchSelf();
        }

        internal void NetworkManagerInitialized()
        {
            foreach (var customMessageHandler in CustomMessageHandlers)
                NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(customMessageHandler,
                    NetworkBehaviourExtensions.ReceiveNetworkMessage);
        }

        internal void NetworkManagerShutdown()
        {
            foreach (var customMessageHandler in CustomMessageHandlers)
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(customMessageHandler);
        }

        public void Dispose()
        {
            AlreadyRegistered.Remove(pluginType);
            Validators.Remove(this);
            if (NetworkManager.Singleton)
                foreach (var customMessageHandler in CustomMessageHandlers)
                    NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(customMessageHandler);

            if (Patcher.GetPatchedMethods().Any())
                UnpatchSelf();
        }
    } 
    
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private readonly Harmony harmony = new Harmony(PluginInfo.GUID);
        public new static ManualLogSource Logger { get; private set; } = null!;

        private void Awake()
        {
            Logger = Logger;
            harmony.Patch(AccessTools.Method(typeof(NetworkManager), nameof(NetworkManager.Initialize)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(OnNetworkManagerInitialized)));
            harmony.Patch(AccessTools.Method(typeof(NetworkManager), nameof(NetworkManager.Shutdown)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(OnNetworkManagerShutdown)));
        }

        private static void OnNetworkManagerInitialized()
        {
            foreach (var netcodeValidator in NetcodeValidator.Validators)
                netcodeValidator.NetworkManagerInitialized();
        }

        private static void OnNetworkManagerShutdown()
        {
            foreach (var netcodeValidator in NetcodeValidator.Validators)
                netcodeValidator.NetworkManagerShutdown();
        }

        internal static void UpdateNetworkProperties(object __instance)
        {
            (__instance as NetworkBehaviour)?.StartCoroutine(YieldUntilIsSpawned((NetworkBehaviour)__instance));
        }

        private static IEnumerator YieldUntilIsSpawned(NetworkBehaviour networkBehaviour)
        {
            yield return new WaitUntil(() => networkBehaviour.NetworkObject.IsSpawned);
            Debug.Log("Verifying as registered with network object.");
            
            if (!networkBehaviour.NetworkObject.ChildNetworkBehaviours.Contains(networkBehaviour))
                networkBehaviour.NetworkObject.ChildNetworkBehaviours.Add(networkBehaviour);
            
            networkBehaviour.UpdateNetworkProperties();
        }
    }

    public class AlreadyPatchedException : Exception
    {
        public AlreadyPatchedException(string PluginGUID) : base(
            $"Can't patch plugin {PluginGUID} until the other instance of NetcodeValidator is Disposed of!") {}
    }
}
