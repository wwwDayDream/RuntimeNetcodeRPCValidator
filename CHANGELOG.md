# Changelog

## v0.1.6

### Fixed

- Incorrect error logging in a few places.
- Project structure was out of control. It's now.. In control.

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