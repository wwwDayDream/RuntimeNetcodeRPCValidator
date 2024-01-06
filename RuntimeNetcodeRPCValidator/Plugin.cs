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

        private List<(Type, NetcodeValidator.InsertionPoint)> AlreadyPatchedNativeBehaviours { get; } =
            new List<(Type, NetcodeValidator.InsertionPoint)>();

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

        private void NetcodeValidatorOnAddedNewBoundBehaviour(NetcodeValidator validator, Type netBehaviour, NetcodeValidator.InsertionPoint insertAt)
        {
            var asItem = (netBehaviour, insertAt);
            if (AlreadyPatchedNativeBehaviours.Contains(asItem)) return;
            AlreadyPatchedNativeBehaviours.Add(asItem);

            MethodBase method = null;

            switch (insertAt)
            {
                case NetcodeValidator.InsertionPoint.Awake:
                    method = AccessTools.Method(netBehaviour, "Awake");
                    break;
                case NetcodeValidator.InsertionPoint.Start:
                    method = AccessTools.Method(netBehaviour, "Start");
                    break;
                case NetcodeValidator.InsertionPoint.Constructor:
                    method = AccessTools.Constructor(netBehaviour);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(insertAt), insertAt, null);
            }
            
            if (method == null)
            {
                Logger.LogError(TextHandler.PluginTriedToBindToNonExistentMethod(validator, netBehaviour, insertAt));
                return;
            }

            Logger.LogInfo(TextHandler.RegisteredPatchForType(validator, netBehaviour, insertAt));
            
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
