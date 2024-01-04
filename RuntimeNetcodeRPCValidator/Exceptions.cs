using System;

namespace RuntimeNetcodeRPCValidator
{
    public class AlreadyRegisteredException : Exception
    {
        public AlreadyRegisteredException(string PluginGUID) : base(
            $"Can't register plugin {PluginGUID} until the other instance of NetcodeValidator is Disposed of!") {}
    }

    public class InvalidPluginGuidException : Exception
    {

        public InvalidPluginGuidException(string pluginGUID) : base(
            $"Can't patch plugin {pluginGUID} because it doesn't exist!")
        {
        }
    }

    public class NotNetworkBehaviourException : Exception
    {
        public NotNetworkBehaviourException(Type type) : 
            base($"Netcode Runtime RPC Validator tried to NetcodeValidator.Patch type {type.Name} that doesn't inherit from NetworkBehaviour!") {}
    }

    public class MustCallFromDeclaredTypeException : Exception
    {
        public MustCallFromDeclaredTypeException() : 
            base($"Netcode Runtime RPC Validator tried to run NetcodeValidator.PatchAll from a delegate! You must call PatchAll from a declared type.") {}
    }
}