using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Netcode;

namespace RuntimeNetcodeRPCValidator
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private readonly Harmony _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);

        private List<Type> AlreadyPatchedNativeBehaviours { get; } =
            new List<Type>();

        internal new static ManualLogSource Logger { get; private set; }
        public static event Action NetworkManagerInitialized;
        public static event Action NetworkManagerShutdown;
        
        private void Awake()
        {
            Logger = base.Logger;
            
            NetcodeValidator.AddedNewBoundBehaviour += NetcodeValidatorOnAddedNewBoundBehaviour;
            
            _harmony.Patch(AccessTools.Method(typeof(NetworkManager), nameof(NetworkManager.Initialize)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(OnNetworkManagerInitialized)));
            _harmony.Patch(AccessTools.Method(typeof(NetworkManager), nameof(NetworkManager.Shutdown)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(OnNetworkManagerShutdown)));
        }

        private void NetcodeValidatorOnAddedNewBoundBehaviour(NetcodeValidator validator, Type netBehaviour)
        {
            if (AlreadyPatchedNativeBehaviours.Contains(netBehaviour)) return;
            AlreadyPatchedNativeBehaviours.Add(netBehaviour);

            MethodBase method = AccessTools.Method(netBehaviour, "Awake");
            if (method == null)
                method = AccessTools.Method(netBehaviour, "Start");
            if (method == null)
                method = AccessTools.Constructor(netBehaviour);
            
            Logger.LogInfo(TextHandler.RegisteredPatchForType(validator, netBehaviour, method));
            
            var hMethod = new HarmonyMethod(typeof(NetcodeValidator), nameof(NetcodeValidator.TryLoadRelatedComponentsInOrder));
            _harmony.Patch(method, postfix: hMethod);
        }
            
        protected static void OnNetworkManagerInitialized()
        {
            NetworkManagerInitialized?.Invoke();
        }

        protected static void OnNetworkManagerShutdown()
        {
            NetworkManagerShutdown?.Invoke();
        }
    }
}
