using System;
using BepInEx.Logging;
using Unity.Netcode;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace LethalCompanyVileVendingMachine;

public class VileVendingMachineNetcodeController : NetworkBehaviour
{
    private ManualLogSource _mls;

    public event Action<string> OnInitializeConfigValues;
    public event Action<string> OnUpdateVendingMachineIdentifier;
    public event Action<string, int> OnDoAnimation;
    public event Action<string, int> OnPlayerDiscardHeldObject;
    public event Action<string, int, bool> OnChangeAnimationParameterBool;
    public event Action<string, NetworkObjectReference, Vector3> OnPlaceItemInHand;
    public event Action<string> OnDespawnHeldItem;
    public event Action<string, NetworkObjectReference> OnUpdateServerHeldItemCopy;
    public event Action<string, int> OnChangeTargetPlayer;
    public event Action<string> OnSpawnCola;
    public event Action<string, NetworkObjectReference> OnUpdateColaNetworkObjectReference;
    

    private void Start()
    {
        _mls = Logger.CreateLogSource(
            $"{VileVendingMachinePlugin.ModGuid} | Volatile Vending Machine Netcode Controller");
    }

    [ServerRpc(RequireOwnership = false)]
    public void SpawnColaServerRpc(string recievedVendingMachineId)
    {
        OnSpawnCola?.Invoke(recievedVendingMachineId);
    }

    [ClientRpc]
    public void UpdateColaNetworkObjectReferenceClientRpc(string recievedVendingMachineId,
        NetworkObjectReference colaNetworkObjectReference)
    {
        OnUpdateColaNetworkObjectReference?.Invoke(recievedVendingMachineId, colaNetworkObjectReference);
    }

    [ServerRpc(RequireOwnership = false)]
    public void UpdateServerHeldItemCopyServerRpc(string recievedVendingMachineId,
        NetworkObjectReference networkObjectReference)
    {
        OnUpdateServerHeldItemCopy?.Invoke(recievedVendingMachineId, networkObjectReference);
    }

    [ServerRpc(RequireOwnership = false)]
    public void PlaceItemInHandServerRpc(string recievedVendingMachineId, NetworkObjectReference networkObjectReference, Vector3 position)
    {
        PlaceItemInHandClientRpc(recievedVendingMachineId, networkObjectReference, position);
    }

    [ServerRpc(RequireOwnership = false)]
    public void DespawnHeldItemServerRpc(string recievedVendingMachineId)
    {
        OnDespawnHeldItem?.Invoke(recievedVendingMachineId);
    }

    [ClientRpc]
    public void ChangeTargetPlayerClientRpc(string recievedVendingMachineId, int playerClientId)
    {
        OnChangeTargetPlayer?.Invoke(recievedVendingMachineId, playerClientId);
    }
    
    [ClientRpc]
    public void PlaceItemInHandClientRpc(string recievedVendingMachineId, NetworkObjectReference networkObjectReference, Vector3 position)
    {
        OnPlaceItemInHand?.Invoke(recievedVendingMachineId, networkObjectReference, position);
    }
    
    [ClientRpc]
    public void PlayerDiscardHeldObjectClientRpc(string recievedVendingMachineId, int playerClientId)
    {
        OnPlayerDiscardHeldObject?.Invoke(recievedVendingMachineId, playerClientId);
    }

    [ClientRpc]
    public void InitializeConfigValuesClientRpc(string recievedVendingMachineId)
    {
        OnInitializeConfigValues?.Invoke(recievedVendingMachineId);
    }

    [ClientRpc]
    public void UpdateVendingMachineIdClientRpc(string recievedVendingMachineId)
    {
        OnUpdateVendingMachineIdentifier?.Invoke(recievedVendingMachineId);
    }
    
    [ClientRpc]
    public void ChangeAnimationParameterBoolClientRpc(string recievedVendingMachineId, int animationId, bool value)
    {
        OnChangeAnimationParameterBool?.Invoke(recievedVendingMachineId, animationId, value);
    }

    [ClientRpc]
    public void DoAnimationClientRpc(string recievedVendingMachineId, int animationId)
    {
        OnDoAnimation?.Invoke(recievedVendingMachineId, animationId);
    }

    private void LogDebug(string msg)
    {
        #if DEBUG
        _mls?.LogInfo(msg);
        #endif
    }
}