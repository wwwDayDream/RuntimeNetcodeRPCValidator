# Changelog

## v0.2.0

### Changed

- The final changes to the `NetcodeValidator` class occur w/ this update. This will be the final form, the only change on your end is a `new NetcodeValidator(YourPluginGUID)` instead of `new NetcodeValidator(YourPlugin)`. This is to be more in line with how Harmony instances are handled and to make things easier on the back-end. This includes a few new methods listed below following the pattern of the Harmony class.
- NetcodeValidator.Patch(Type type)
- NetcodeValidator.Patch(Assembly assembly)
- NetcodeValidator.PatchAll()

## v0.1.8

### Fixed

- Pretty big README.md fix!

## v0.1.7

### Fixed

- Incorrect error logging in a few places.
- Project structure was out of control. It's now.. In control.
- Git fixes.

## v0.1.1

### Added

- Introduced a new `LogErrorAndReturn` method in `NetworkBehaviourExtensions.cs` to streamline error logging.
- Implemented a new network logger in `Plugin.cs`.

### Changed

- Reorganized error reporting in `NetworkBehaviourExtensions.cs`, now utilizing `LogErrorAndReturn` to improve coding practices.
- Pruned extraneous variables in `RpcData` struct.
- Modified package dependencies in `.csproj` file, removing unnecessary ones and adding a supplemental package reference.

## v0.1.0

### Removed

- **Imports**: The namespaces `System.Collections.Generic, System.IO, System.Runtime.Serialization.Formatters.Binary, and HarmonyLib` have been removed.
- **Data structures**: The dictionaries `NetRPCStates, NetRPCData, NetRPCParams, NetRPCSender` have been removed which were being used to track RPC state, data, params, and senders.
- **Methods**: The methods `GetNetworkState, GetNetworkData, LastRPCSender, VerifyAsRegisteredWithNetworkObject, ValidateRPCExecution, PrepareRPCParamsForSending, SendRPC, ProcessRPC` have been removed.

### Added

- **Classes**: New inner class `RpcData` has been added.
- **Enumerators**: An Enumerator `RpcSource` has been added to the RpcData class and `RpcState` has been moved inside `RpcData`. Enumerator `RpcState` values changed from`[AwaitingMessage, MessageReceived]` to `[FromUser, FromNetworking]`.
- **Methods**: The method `LastSenderId` has been added to get the last SenderId from RpcData.
- **MethodPatchInternal Updates**: The logic of MethodPatchInternal has been changed quite significantly.