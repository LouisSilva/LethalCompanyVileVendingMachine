using System;
using System.Diagnostics.CodeAnalysis;
using BepInEx.Logging;
using Unity.Netcode;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace LethalCompanyVileVendingMachine;

[SuppressMessage("ReSharper", "Unity.RedundantHideInInspectorAttribute")]
public class VileVendingMachineNetcodeController : NetworkBehaviour
{
    private ManualLogSource _mls;

    [HideInInspector] public readonly NetworkVariable<ulong> TargetPlayerClientId = new();
    [HideInInspector] public readonly NetworkVariable<bool> IsItemOnHand = new();
    [HideInInspector] public readonly NetworkVariable<bool> MeshEnabled = new();
    
    public event Action<string> OnInitializeConfigValues;
    public event Action<string> OnUpdateVendingMachineIdentifier;
    public event Action<string, int> OnSetAnimationTrigger;
    public event Action<string, ulong> OnPlaceItemInHand;
    public event Action<string> OnDespawnHeldItem;
    public event Action<string, NetworkObjectReference, int> OnUpdateColaNetworkObjectReference;
    public event Action<string, Vector3, Quaternion> OnPlayMaterializeVfx;
    public event Action<string, int, bool> OnPlayCreatureSfx;
    public event Action<string> OnIncreaseFearLevelWhenPlayerBlended;
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
    public void UpdateColaNetworkObjectReferenceClientRpc(string receivedVendingMachineId,
        NetworkObjectReference colaNetworkObjectReference, int colaValue)
    {
        OnUpdateColaNetworkObjectReference?.Invoke(receivedVendingMachineId, colaNetworkObjectReference, colaValue);
    }

    [ServerRpc(RequireOwnership = false)]
    public void PlaceItemInHandServerRpc(string receivedVendingMachineId, ulong targetPlayerId)
    {
        PlaceItemInHandClientRpc(receivedVendingMachineId, targetPlayerId);
    }
    
    [ClientRpc]
    public void PlaceItemInHandClientRpc(string receivedVendingMachineId, ulong targetPlayerId)
    {
        OnPlaceItemInHand?.Invoke(receivedVendingMachineId, targetPlayerId);
    }

    [ServerRpc(RequireOwnership = false)]
    public void DespawnHeldItemServerRpc(string receivedVendingMachineId)
    {
        OnDespawnHeldItem?.Invoke(receivedVendingMachineId);
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
    public void SetAnimationTriggerClientRpc(string receivedVendingMachineId, int animationId)
    {
        OnSetAnimationTrigger?.Invoke(receivedVendingMachineId, animationId);
    }

    private void LogDebug(string msg)
    {
        #if DEBUG
        _mls?.LogInfo(msg);
        #endif
    }
}