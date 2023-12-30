# Runtime Unity Netcode Patcher


This plugin offers an easy-to-use solution for Netcode's NetworkBehaviour class, streamlining the approach to networking mods with Server and Client RPCs. By leveraging the CustomMessagingHandler of Netcode, it simplifies the modding process, allowing for easier implementation of RPCs without the need for complex compile-time DLL patching. Designed as a BepInPlugin, it adheres to the same attribute and name restrictions as the standard netcode patcher, making it a user-friendly and effective dependency for enhancing your modding projects.


## Table of Contents
- [Getting Started](#getting-started)
- [Prerequisites](#prerequisites)
- [Notes](#notes)
- [Built With](#built-with)
- [Acknowledgments](#acknowledgments)
- [Contributing](#contributing)
- [License](#license)

## Getting Started

To integrate Runtime Unity Netcode Patcher in your Unity project, follow these steps:

1. **Reference the Output DLL**: Include the output DLL in your project and add an `[BepInDependency(RuntimeNetcodeRPCValidator.PluginInfo.GUID)]` attribute to your `[BepInPlugin]`.
2. **Instantiate NetcodeValidator**: Create and maintain a reference to an instance of `NetcodeValidator`. When you wish to revert any patches applied, simply call `Dispose()` on the instance. A new instance can then be created to reapply the netcode patching.
3. **Define and Use RPCs**: Ensure your Remote Procedure Calls on your NetworkBehaviours have the correct attribute and end their name with ServerRpc/ClientRpc.

```csharp
// Example of using NetcodeValidator
NetcodeValidator myValidator = new NetcodeValidator(...);
// ...
// Dispose when plugin unloads
myValidator.Dispose();
```

### Prerequisites

Ensure you have the following components within the environment:

- **[Unity's Netcode for GameObjects (NGO)](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects)**: For handling networked entities and communications.
- **[Harmony](https://github.com/pardeike/Harmony)**: A powerful library for patching, replacing and decorating .NET and Mono methods during runtime.

### Notes

Utilize the `NetworkBehaviour.LastRPCSender()`, accessible with `this.` inside a network behaviour instance, method to retrieve the ID of the last RPC sender. This will always be `NetworkManager.ServerClientId` on the clients.


### Built With

- [Harmony](https://github.com/pardeike/Harmony) - For runtime method patching.
- [Unity's Netcode for GameObjects (NGO)](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects) - For robust networking in Unity.

## Acknowledgments

- [@Lordfirespeed](https://www.discordapp.com/users/290259615059279883) for invaluable support and insights throughout the development.

## Contributing

We welcome contributions! If you would like to help improve the Runtime Unity Netcode Patcher, please submit pull requests, and report bugs or suggestions in the issues section of this repository.

## License

This project is licensed under the [MIT License](LICENSE.md) - see the LICENSE file for details.
