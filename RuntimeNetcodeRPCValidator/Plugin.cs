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
        public static ManualLogSource Log { get; private set; } = null!;

        private void Awake()
        {
            Log = Logger;
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

        internal static void OnNetworkBehaviourConstructed(object __instance)
        {
            if (!(__instance is NetworkBehaviour networkBehaviour))
                return;
            if (networkBehaviour.NetworkObject == null || networkBehaviour.NetworkManager == null &&
                NetworkBehaviourExtensions.LogErrorAndReturn($"NetworkBehaviour {__instance.GetType()} is trying to sync with the NetworkObject but the {(networkBehaviour.NetworkObject == null ? "NetworkObject" : "NetworkManager")} is null!", true))
                return;
            networkBehaviour.SyncWithNetworkObject();
        }
    }

    public class AlreadyPatchedException : Exception
    {
        public AlreadyPatchedException(string PluginGUID) : base(
            $"Can't patch plugin {PluginGUID} until the other instance of NetcodeValidator is Disposed of!") {}
    }
}
