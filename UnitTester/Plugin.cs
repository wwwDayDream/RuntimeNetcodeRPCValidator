using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace UnitTester
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency(RuntimeNetcodeRPCValidator.MyPluginInfo.PLUGIN_GUID, RuntimeNetcodeRPCValidator.MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private Harmony Patcher { get; set; }
        private RuntimeNetcodeRPCValidator.NetcodeValidator NetcodeValidator { get; set; }

        public static ManualLogSource LogSource { get; private set; }
        
        private void Awake()
        {
            LogSource = Logger;
            
            Patcher = new Harmony(MyPluginInfo.PLUGIN_GUID);
            NetcodeValidator = new RuntimeNetcodeRPCValidator.NetcodeValidator(MyPluginInfo.PLUGIN_GUID);

            NetcodeValidator.PatchAll();
            NetcodeValidator.BindToPreExistingObjectByBehaviour<ConfigurationSync, Terminal>(
                RuntimeNetcodeRPCValidator.NetcodeValidator.InsertionPoint.Awake);
        }
    }
}