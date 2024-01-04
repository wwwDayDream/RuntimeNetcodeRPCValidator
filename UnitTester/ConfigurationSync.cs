using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace UnitTester
{
    public class ConfigurationSync : NetworkBehaviour
    {
        /// <summary>
        /// This won't work because it lacks a <see cref="ServerRpcAttribute"/>
        /// </summary>
        [ServerRpc]
        private void NoSuffix() {}
        /// <summary>
        /// This won't work because it has the wrong suffix (or wrong attribute, I'll let you decide).
        /// </summary>
        [ClientRpc]
        private void WrongSuffixServerRpc() {}

        /// <summary>
        /// Only owner clients of the NetworkObject can run this method.
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        private void MustBeOwnerServerRpc()
        {
            Plugin.LogSource.LogInfo($"{nameof(MustBeOwnerServerRpc)} received from {RuntimeNetcodeRPCValidator.NetworkBehaviourExtensions.LastSenderId}");
        }

        /// <summary>
        /// Any client can run this method.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void NotRequiredOwnershipServerRpc()
        {
            Plugin.LogSource.LogInfo($"{nameof(NotRequiredOwnershipServerRpc)} received from {RuntimeNetcodeRPCValidator.NetworkBehaviourExtensions.LastSenderId}");
        }

        /// <summary>
        /// Here is the unit testing for owner checks && some error validation.
        /// </summary>
        [ClientRpc]
        private void ValidClientRpc()
        {
            Plugin.LogSource.LogInfo("Received servers request to try to ping back.");
            NotRequiredOwnershipServerRpc();
            MustBeOwnerServerRpc();
            Plugin.LogSource.LogInfo("Testing attempting to run ClientRpc from a Client.");
            if (!IsHost || !IsServer)
                ValidClientRpc();
        }
        
        /// <summary>
        /// A Server Remote Procedure Call available to any connected client.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void MasterServerRpc()
        {
            Plugin.LogSource.LogInfo($"{nameof(MasterServerRpc)} received from {RuntimeNetcodeRPCValidator.NetworkBehaviourExtensions.LastSenderId}");
            ValidClientRpc();
        }
        
        /// <summary>
        /// We start a Coroutine and wait because the NetworkObject is not spawned yet.
        /// </summary>
        private void Awake()
        {
            StartCoroutine(WaitForSomeTime());
        }

        private IEnumerator WaitForSomeTime()
        {
            var startTime = Time.fixedUnscaledTime;
            Plugin.LogSource.LogInfo("Waiting for some time...");
            yield return new WaitUntil(() => NetworkObject.IsSpawned);
            Plugin.LogSource.LogInfo($"Waited for {Time.fixedUnscaledTime - startTime} seconds. Running {nameof(MasterServerRpc)} now.");
            MasterServerRpc();
        } 
    }
}