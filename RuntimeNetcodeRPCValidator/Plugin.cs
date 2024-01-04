using System;
using System.Collections;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace RuntimeNetcodeRPCValidator
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private readonly Harmony harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        public static ManualLogSource LogSource { get; private set; } = null!;

        public static event Action NetworkManagerInitialized;
        public static event Action NetworkManagerShutdown;
            
        private void Awake()
        {
            LogSource = Logger;
            harmony.Patch(AccessTools.Method(typeof(NetworkManager), nameof(NetworkManager.Initialize)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(OnNetworkManagerInitialized)));
            harmony.Patch(AccessTools.Method(typeof(NetworkManager), nameof(NetworkManager.Shutdown)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(OnNetworkManagerShutdown)));
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
