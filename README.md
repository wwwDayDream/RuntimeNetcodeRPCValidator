# Runtime Unity Netcode Patcher
[![Build](https://github.com/NicholasScott1337/RuntimeNetcodeRPCValidator/actions/workflows/build.yml/badge.svg)](https://github.com/NicholasScott1337/RuntimeNetcodeRPCValidator/actions/workflows/build.yml)

This plugin offers an easy-to-use solution for Netcode's NetworkBehaviour class, streamlining the approach to networking mods with Server and Client RPCs. By utilizing the CustomMessagingHandler of Netcode, it networks RPCs and their System.Serializable (Marked with [Serializable]) or INetworkSerializable parameters. While this is currently only in the Lethal Company directory, it can be expanded to other games upon request. Please reach out on Discord or via an issue here on Github for questions or contact.


## Table of Contents
- [Getting Started](#getting-started)
- [Examples](#examples)
- [Prerequisites](#prerequisites)
- [Notes](#notes)
- [Built With](#built-with)
- [Acknowledgments](#acknowledgments)
- [Contributing](#contributing)
- [Contact](#contact)

## Getting Started

To integrate Runtime Unity Netcode Patcher in your Unity project, follow these steps:

1. **Reference Runtime Netcode RPC Validator**: Either by utilizing a NuGet package inside visual studio `dotnet add package NicholaScott.BepInEx.RuntimeNetcodeRPCValidator --version 0.2.0` and add an `[BepInDependency(RuntimeNetcodeRPCValidator.MyPluginInfo.PLUGIN_GUID, RuntimeNetcodeRPCValidator.MyPluginInfo.PLUGIN_VERSION)]` attribute to your `[BepInPlugin]`.
2. **Instantiate NetcodeValidator**: Create and maintain a reference to an instance of `NetcodeValidator` and call `NetcodeValidator.PatchAll()`. When you wish to revert any patches applied call `Dispose()`, or `UnpatchSelf()` if you want to keep the instance for re-patching.
3. **Define and Use RPCs**: Ensure your Remote Procedure Calls on your NetworkBehaviours have the correct attribute and end their name with ServerRpc/ClientRpc.

### Examples

For more robust examples check the [Github Repo](https://github.com/NicholasScott1337/RuntimeNetcodeRPCValidator/tree/main/UnitTester) of the UnitTester plugin, which is used during development to verify codebase.

```csharp
// Example of using NetcodeValidator
namespace SomePlugin {
    [BepInPlugin("My.Plugin.Guid", "My Plugin Name", "0.1.1")]
    [BepInDependency(RuntimeNetcodeRPCValidator.MyPluginInfo.PLUGIN_GUID, RuntimeNetcodeRPCValidator.MyPluginInfo.PLUGIN_VERSION)]
    public class MyPlugin : BaseUnityPlugin {
        private NetcodeValidator netcodeValidator;
        
        private void Awake()
        {
            netcodeValidator = new NetcodeValidator("My.Plugin.Guid");
            netcodeValidator.PatchAll();
        }
        
        // [[OPTIONAL DISPOSE TO UNPATCH]]
        private void OnDestroy()
        {
            netcodeValidator.Dispose();
        }
    }
}
```


```csharp
// Example of using Server or Client RPCs. Naming conventions require the method to end with the corresponding attribute name.
namespace SomePlugin {
    // This assumes you've declared a BaseUnityPlugin and Harmony instance elsewhere. Including the previous snippet about NetcodeValidator.
    [HarmonyPatch(typeof(Terminal), "Start")]
    private static class Patch {
        [HarmonyPrefix]
        private static void AddToTerminalObject(Terminal __instance) {
            __instance.gameObject.AddComponent<PluginNetworkingInstance>();
        }
    }
    public class PluginNetworkingInstance : NetworkBehaviour {
        [ServerRpc]
        public void SendPreferredNameServerRpc(string name) {
            // Log the received name
            Debug.Log(name);
            // Tell all clients what the sender told us
            TellAllOtherClients(NetworkBehaviourExtensions.LastSenderId, name);
        }
        [ClientRpc]
        public void TellAllOtherClients(ulong senderId, string name) {
            Debug.Log(StartOfRound.Instance.allPlayerScripts.First(playerController => playerController.actualClientId == senderId).playerUsername + " is now " + name);
        }
        [ClientRpc]
        public void RunClientRpc() {
            // Send to the server what our preferred name is, f.e.
            SendPreferredNameServerRpc("Nicki");
        }
        private void Awake()
        {
            // Are we a server instance?
            if (IsHost)
                StartCoroutine(WaitForSomeTime());
        }

        private IEnumerator WaitForSomeTime()
        {
            // We need to wait because sending an RPC before a NetworkObject is spawned results in errors.
            yield return new WaitUntil(() => NetworkObject.IsSpawned);
        
            // Tell all clients to run this method.
            RunClientRpc();
        } 
    }
}
```

### Prerequisites

Ensure you have the following components within the environment:

- **[Unity's Netcode for GameObjects (NGO)](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects)**: For handling networked entities and communications.
- **[Harmony](https://github.com/pardeike/Harmony)**: A powerful library for patching, replacing and decorating .NET and Mono methods during runtime.

### Notes

Utilize the `NetworkBehaviourExtensions.LastSenderId` property to retrieve the ID of the last RPC sender. This will always be `NetworkManager.ServerClientId` on the clients.


### Built With

- [Harmony](https://github.com/pardeike/Harmony) - For runtime method patching.
- [Unity's Netcode for GameObjects (NGO)](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects) - For robust networking in Unity.

## Acknowledgments

- [@Lordfirespeed](https://www.discordapp.com/users/290259615059279883) for invaluable support and insights throughout the development.

## Contributing

We welcome contributions! If you would like to help improve the Runtime Unity Netcode Patcher, please submit pull requests, and report bugs or suggestions in the issues section of this repository.

## Contact

Discord: [www.day.dream](https://discordapp.com/users/160901181692968971)