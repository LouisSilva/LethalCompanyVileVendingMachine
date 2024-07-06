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
    public event Action<string, int, bool> OnChangeAnimationParameterBool;
    public event Action<string, NetworkObjectReference, Vector3> OnPlaceItemInHand;
    public event Action<string> OnDespawnHeldItem;
    public event Action<string, ulong> OnChangeTargetPlayer;
    public event Action<string, NetworkObjectReference, int> OnUpdateColaNetworkObjectReference;
    public event Action<string, bool> OnSetMeshEnabled;
    public event Action<string, Vector3, Quaternion> OnPlayMaterializeVfx;
    public event Action<string, int, bool> OnPlayCreatureSfx;
    public event Action<string> OnIncreaseFearLevelWhenPlayerBlended;
    public event Action<string, bool> OnSetIsItemOnHand;
    public event Action<string> OnStartAcceptItemAnimation;

    private void Start()
    {
        _mls = Logger.CreateLogSource(
            $"{VileVendingMachinePlugin.ModGuid} | Vile Vending Machine Netcode Controller");
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void StartAcceptItemAnimationServerRpc(string receivedVendingMachineId)
    {
        OnStartAcceptItemAnimation?.Invoke(receivedVendingMachineId);
    }

    [ClientRpc]
    public void SetIsItemOnHandClientRpc(string receivedVendingMachineId, bool isItemOnHand)
    {
        OnSetIsItemOnHand?.Invoke(receivedVendingMachineId, isItemOnHand);
    }

    [ClientRpc]
    public void IncreaseFearLevelWhenPlayerBlendedClientRpc(string receivedVendingMachineId)
    {
        OnIncreaseFearLevelWhenPlayerBlended?.Invoke(receivedVendingMachineId);
    }
    
    [ClientRpc]
    public void PlaySfxClientRpc(string receivedVendingMachineId, int audioClipType, bool interrupt = true)
    {
        OnPlayCreatureSfx?.Invoke(receivedVendingMachineId, audioClipType, interrupt);
    }

    [ClientRpc]
    public void PlayMaterializeVfxClientRpc(string receivedVendingMachineId, Vector3 finalPosition, Quaternion finalRotation)
    {
        OnPlayMaterializeVfx?.Invoke(receivedVendingMachineId, finalPosition, finalRotation);
    }

    [ClientRpc]
    public void SetMeshEnabledClientRpc(string receivedVendingMachineId, bool meshEnabled)
    {
        OnSetMeshEnabled?.Invoke(receivedVendingMachineId, meshEnabled);
    }

    [ClientRpc]
    public void UpdateColaNetworkObjectReferenceClientRpc(string receivedVendingMachineId,
        NetworkObjectReference colaNetworkObjectReference, int colaValue)
    {
        OnUpdateColaNetworkObjectReference?.Invoke(receivedVendingMachineId, colaNetworkObjectReference, colaValue);
    }

    [ServerRpc(RequireOwnership = false)]
    public void PlaceItemInHandServerRpc(string receivedVendingMachineId, NetworkObjectReference networkObjectReference, Vector3 position)
    {
        PlaceItemInHandClientRpc(receivedVendingMachineId, networkObjectReference, position);
    }

    [ServerRpc(RequireOwnership = false)]
    public void DespawnHeldItemServerRpc(string receivedVendingMachineId)
    {
        OnDespawnHeldItem?.Invoke(receivedVendingMachineId);
    }

    [ServerRpc(RequireOwnership = false)]
    public void ChangeTargetPlayerServerRpc(string receivedVendingMachineId, ulong playerClientId)
    {
        ChangeTargetPlayerClientRpc(receivedVendingMachineId, playerClientId);
    }

    [ClientRpc]
    private void ChangeTargetPlayerClientRpc(string receivedVendingMachineId, ulong playerClientId)
    {
        OnChangeTargetPlayer?.Invoke(receivedVendingMachineId, playerClientId);
    }
    
    [ClientRpc]
    public void PlaceItemInHandClientRpc(string receivedVendingMachineId, NetworkObjectReference networkObjectReference, Vector3 position)
    {
        OnPlaceItemInHand?.Invoke(receivedVendingMachineId, networkObjectReference, position);
    }

    [ClientRpc]
    public void InitializeConfigValuesClientRpc(string receivedVendingMachineId)
    {
        OnInitializeConfigValues?.Invoke(receivedVendingMachineId);
    }

    [ClientRpc]
    public void SyncVendingMachineIdClientRpc(string receivedVendingMachineId)
    {
        OnUpdateVendingMachineIdentifier?.Invoke(receivedVendingMachineId);
    }
    
    [ClientRpc]
    public void ChangeAnimationParameterBoolClientRpc(string receivedVendingMachineId, int animationId, bool value)
    {
        OnChangeAnimationParameterBool?.Invoke(receivedVendingMachineId, animationId, value);
    }

    [ClientRpc]
    public void DoAnimationClientRpc(string receivedVendingMachineId, int animationId)
    {
        OnDoAnimation?.Invoke(receivedVendingMachineId, animationId);
    }

    private void LogDebug(string msg)
    {
        #if DEBUG
        _mls?.LogInfo(msg);
        #endif
    }
}