# [RNV] Runtime Netcode Validator
[![Build](https://github.com/NicholasScott1337/RuntimeNetcodeRPCValidator/actions/workflows/build.yml/badge.svg)](https://github.com/NicholasScott1337/RuntimeNetcodeRPCValidator/actions/workflows/build.yml)

A [BepInEx](#version-compliance) plugin utilizing [HarmonyX](#version-compliance) to patch methods labeled with `[ServerRpc]` or `[ClientRpc]`, inside a specified type that derives from `NetworkBehaviour`, to run the same format of checks [NGO](#version-compliance) patches in during compile-time.

### F.A.Q.

- *Why is my `NetworkBehaviour` added to a pre-existing `NetworkObject` not working?*
    - RNV **used** to auto-magically patch your `NetworkBehaviour::Constructor` to call a custom method  to synchronize with the parent NetworkObject (something that registering a new NetworkObject w/ NetworkPrefab, and .Spawn()ing avoids). To prevent confusion this is now a [manual process](#pre-existing-networkobject) but is still possible.
- *What does Runtime Netcode Validator mean?*
  - In simplest terms, 'Netcode Validation' is a way to describe the process of verifying the associated information that makes a RPC(Remote Procedure Call) transmit it's information across the network and then doing that transmission. This is universal, the `Runtime` is what sets this apart, as it does it's "code insertion" at runtime.
- *How does this differ from what [NGO](#version-compliance) (or implementations of the NGO IL emitter such as Weaver) does to my RPC methods?*
  - This utilizes [HarmonyX](#version-compliance) to patch your methods at runtime and add this 'validation' check in the form of a custom method with as little overhead as possible (along with other QoL [NGO](#version-compliance) features). This comes with some benefits, like adding custom serialization (discussed later), as well as allowing you to un-patch your RPC methods.
- *What are the __implications__ of runtime patching?*
  - Arguments could be made about the memory overhead that patching causes but this should be minimal and no worse than if you were to patch any other method. The actual check itself is designed to be no more intrusive than the [NGO](#version-compliance) checks and can be manually reviewed as the method that is called (Determining if *your* rpc should proceed or if we should transmit across the network) is under [NetworkBehaviourExtensions::MethodPatchInternal](https://github.com/NicholasScott1337/RuntimeNetcodeRPCValidator/blob/main/RuntimeNetcodeRPCValidator/NetworkBehaviourExtensions.cs#L77)
- *What can I put in the RPC parameters?*
  - Typically [NGO](#version-compliance) only allows you to send anything that implements `INetworkSerializable` which usually is fine; However if you want a simple struct or class and writing a whole `INetworkSerializable` implementation method just for a few variables is too much, then you can mark the object with a `[System.Serializable]` attribute and RNV will handle serializing it over the network as well as your normal `INetworkSerializable` parameters.  

## Table of Contents
- [Getting Started](#getting-started)
- [Examples](#examples)
- [Notes](#notes)
- [Versioning](#version-compliance)
- [Acknowledgments](#acknowledgments)
- [Contributing](#contributing)
- [Contact](#contact)

## Getting Started


- Reference Runtime Netcode RPC Validator by installing the NuGet package from the terminal (in your projects directory):

    `dotnet add package NicholaScott.BepInEx.RuntimeNetcodeRPCValidator` 

- Add a BepInDependency  attribute to your `BaseUnityPlugin`.

    `[BepInDependency(RuntimeNetcodeRPCValidator.MyPluginInfo.PLUGIN_GUID, RuntimeNetcodeRPCValidator.MyPluginInfo.PLUGIN_VERSION)]`

- **Instantiate NetcodeValidator**: Create and maintain a reference to an instance of `NetcodeValidator` and call `NetcodeValidator.PatchAll()`. If, and only if, you wish to revert any patches applied you can call `Dispose()`, or `UnpatchSelf()` if you want to keep the instance for re-patching.

- **Define and Use RPCs**: Ensure your Remote Procedure Calls on your NetworkBehaviours have the correct attribute and end their name with ServerRpc/ClientRpc.

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
            
            netcodeValidator.BindToPreExistingObjectByBehaviour<PluginNetworkingInstance, Terminal>();
        }
    }
}
```


```csharp
// Example of using Server or Client RPCs. Naming conventions require the method to end with the corresponding attribute name.
namespace SomePlugin {
    public class PluginNetworkingInstance : NetworkBehaviour {
        [ServerRpc]
        public void SendUsDataServerRpc() {
            // Log the received name
            Debug.Log(name);
            // Tell all clients what the sender told us
            TellAllOtherClientsClientRpc(NetworkBehaviourExtensions.LastSenderId, name);
        }
        [ClientRpc]
        public void TellAllOtherClientsClientRpc(ulong senderId, string name) {
            Debug.Log(StartOfRound.Instance.allPlayerScripts.First(playerController => playerController.actualClientId == senderId).playerUsername + " is now " + name);
        }
        [ClientRpc]
        public void RunClientRpc() {
            // Send to the server what our preferred name is, f.e.
            SendPreferredNameServerRpc("Nicki");
        }
        private void Awake()
        {
            if (!IsHost) // Any clients should ask for sync of something :shrug:
                StartCoroutine(WaitForSomeTime());
        }

        private IEnumerator WaitForSomeTime()
        {
            // We need to wait because sending an RPC before a NetworkObject is spawned results in errors.
            yield return new WaitUntil(() => NetworkObject.IsSpawned);
        
            // Tell all clients to run this method.
            SendUsDataServerRpc();
        } 
    }
}
```

### Notes

Utilize the `NetworkBehaviourExtensions.LastSenderId` property to retrieve the ID of the last RPC sender. This will always be `NetworkManager.ServerClientId` on the clients.

### Pre-Existing NetworkObject
So you don't wanna make a prefab eh? Don't feel like registering it with the network? Afraid of what might come? Fear no more, as you can bind your NetworkBehaviour to a pre-existing (native) NetworkBehaviour utilizing a method anytime before `NetworkManager` is initialized. Generally this would be in your Plugins Awake, right after you create and patch with your `NetcodeValidator`. See the [Examples](#examples) above for usage and below for a detailed signature.
```csharp
public void NetcodeValidator::BindToPreExistingObjectByBehaviour<TCustomBehaviour, TNativeBehaviour>()
```

## Version Compliance
- [Networking For GameObjects (NGO)](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/tree/develop) No version issues reported.
- [Built for BepInEx 5.4.2100](https://github.com/BepInEx/BepInEx) but no version issues reported.
- [HarmonyX packaged w/ BepInEx](https://github.com/BepInEx/HarmonyX/wiki) but considering to drop back to MonoMod implementations.

## Acknowledgments

- [@Lordfirespeed](https://www.discordapp.com/users/290259615059279883) for invaluable support and insights throughout the development.

## Contributing

We welcome contributions! If you would like to help improve the RNV, please submit pull requests, and report bugs or suggestions in the issues section of this repository.

## Contact

Discord: [@Day](https://discordapp.com/users/160901181692968971)