using System;
using System.Collections;
using BepInEx.Logging;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using Logger = UnityEngine.Logger;

namespace LethalCompanyVileVendingMachine;

public class VileVendingMachineClient : MonoBehaviour
{
    private ManualLogSource _mls;
    private string _vendingMachineId;
    
    #pragma warning disable 0649
    [Header("Audio")] [Space(5f)] 
    [SerializeField] private AudioSource creatureVoiceSource;
    [SerializeField] private AudioSource creatureSfxSource;

    [Header("Transforms")] [Space(5f)]
    [SerializeField] private Transform handBone;
    [SerializeField] private Transform itemHolder;

    [Header("Controllers")] [Space(5f)] 
    [SerializeField] private VileVendingMachineNetcodeController netcodeController;
    [SerializeField] private Animator animator;
    [SerializeField] private InteractTrigger triggerScript;
    #pragma warning restore 0649

    public static readonly int ArmAccept = Animator.StringToHash("accept");
    public static readonly int ArmGrab = Animator.StringToHash("grab");
    public static readonly int ArmRetract = Animator.StringToHash("retract");
    public static readonly int Grind = Animator.StringToHash("grind");
    public static readonly int Dispense = Animator.StringToHash("dispense");
    
    private const string StartDepositText = "Place item: [E]";
    private const string DepositText = "Placing item...";
    private const string NoItemsText = "No items to place...";

    private bool _itemInHand;

    private PlayerControllerB _targetPlayer;

    private NetworkObject _cola;

    private void OnEnable()
    {
        netcodeController.OnInitializeConfigValues += HandleInitializeConfigValues;
        netcodeController.OnUpdateVendingMachineIdentifier += HandleUpdateVendingMachineIdentifier;
        netcodeController.OnDoAnimation += SetTrigger;
        netcodeController.OnPlayerDiscardHeldObject += HandlePlayerDiscardHeldItem;
        netcodeController.OnChangeAnimationParameterBool += SetBool;
        netcodeController.OnPlaceItemInHand += HandlePlaceItemInHand;
        netcodeController.OnChangeTargetPlayer += HandleChangeTargetPlayer;
        netcodeController.OnUpdateColaNetworkObjectReference += HandleUpdateColaNetworkObjectReference;
    }

    private void OnDestroy()
    {
        netcodeController.OnInitializeConfigValues -= HandleInitializeConfigValues;
        netcodeController.OnUpdateVendingMachineIdentifier -= HandleUpdateVendingMachineIdentifier;
        netcodeController.OnDoAnimation -= SetTrigger;
        netcodeController.OnPlayerDiscardHeldObject -= HandlePlayerDiscardHeldItem;
        netcodeController.OnChangeAnimationParameterBool -= SetBool;
        netcodeController.OnPlaceItemInHand -= HandlePlaceItemInHand;
        netcodeController.OnChangeTargetPlayer -= HandleChangeTargetPlayer;
        netcodeController.OnUpdateColaNetworkObjectReference -= HandleUpdateColaNetworkObjectReference;
    }

    private void Start()
    {
        _mls = BepInEx.Logging.Logger.CreateLogSource(
            $"{VileVendingMachinePlugin.ModGuid} | Volatile Vending Machine {_vendingMachineId}");
    }

    private void Update()
    {
        UpdateInteractTriggers();
    }
    
    private void HandlePlayerDiscardHeldItem(string recievedVendingMachineId, int playerClientId)
    {
        if (_vendingMachineId != recievedVendingMachineId) return;
        _itemInHand = true;
        if (GameNetworkManager.Instance.localPlayerController != StartOfRound.Instance.allPlayerScripts[playerClientId])
            return;
        
        PlayerControllerB player = GameNetworkManager.Instance.localPlayerController;
        GrabbableObject item = player.currentlyHeldObjectServer;
        if (item == null) item = player.currentlyHeldObject;
        if (item == null) item = player.currentlyGrabbingObject;
        // netcodeController.UpdateServerHeldItemCopyServerRpc(_vendingMachineId, item.GetComponent<NetworkObject>());

        Vector3 placePosition = itemHolder.GetComponent<Transform>().position;
        placePosition.y += item.itemProperties.verticalOffset;
        placePosition = itemHolder.InverseTransformPoint(placePosition);
        
        player.DiscardHeldObject(true, GetComponent<NetworkObject>(), placePosition, false);
        netcodeController.PlaceItemInHandServerRpc(_vendingMachineId, item.GetComponent<NetworkObject>(), placePosition);
    }

    private void HandlePlaceItemInHand(string recievedVendingMachineId, NetworkObjectReference networkObjectReference,
        Vector3 position)
    {
        if (_vendingMachineId != recievedVendingMachineId) return;
        if (!networkObjectReference.TryGet(out NetworkObject networkObject)) return;
        GrabbableObject item = networkObject.GetComponent<GrabbableObject>();
        
        item.isHeldByEnemy = true;
        item.grabbable = false;
        item.grabbableToEnemies = false;
        _itemInHand = true;
        
        item.EnablePhysics(false);
        item.transform.SetParent(itemHolder);
        
        item.transform.position = position;
        
        // This is correct but the rotation of the item holder just overrides it
        item.transform.rotation = item.floorYRot == -1
            ? Quaternion.Euler(
                item.itemProperties.restingRotation.x, 
                item.itemProperties.restingRotation.y,
                item.itemProperties.restingRotation.z)
            
            : Quaternion.Euler(item.itemProperties.restingRotation.x,
                item.floorYRot + item.itemProperties.floorYOffset + 90f, 
                item.itemProperties.restingRotation.z);
    }

    private IEnumerator KillTargetPlayer()
    {
        if (GameNetworkManager.Instance.localPlayerController == _targetPlayer)
        {
            HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
            yield return new WaitForSeconds(0.05f);
            _targetPlayer.KillPlayer(Vector3.zero, true, CauseOfDeath.Crushing, 0);
        }

        yield return new WaitUntil(() => _targetPlayer.deadBody != null);
        if (_targetPlayer.deadBody != null)
        {
            _targetPlayer.deadBody.attachedTo = handBone.transform;
            _targetPlayer.deadBody.attachedLimb = _targetPlayer.deadBody.bodyParts[5];
            _targetPlayer.deadBody.matchPositionExactly = true;

            foreach (Rigidbody body in _targetPlayer.deadBody.bodyParts)
                body.GetComponent<Collider>().excludeLayers = ~0;
        }

        yield return new WaitForSeconds(1.5f);
        if (_targetPlayer.deadBody != null)
        {
            _targetPlayer.deadBody.attachedTo = null;
            _targetPlayer.deadBody.attachedLimb = null;
            _targetPlayer.deadBody.matchPositionExactly = false;
            _targetPlayer.deadBody.transform.GetChild(0).gameObject.SetActive(false);
            _targetPlayer.deadBody = null;
        }
    }

    public void OnAnimationEventEnableColaPhysics()
    {
        _itemInHand = false;
        _cola.transform.SetParent(null);
        Rigidbody colaRigidbody = _cola.GetComponent<Rigidbody>();
        colaRigidbody.isKinematic = false;
        colaRigidbody.AddForce(transform.forward * 5f, ForceMode.Impulse);
        colaRigidbody.AddTorque(transform.right * 20f, ForceMode.Impulse);
    }

    public void OnAnimationEventSpawnCola()
    {
        // if (!NetworkManager.Singleton.IsClient || !netcodeController.IsOwner) return;
        // netcodeController.SpawnColaServerRpc(_vendingMachineId);
    }

    private void UpdateInteractTriggers()
    {
        if (GameNetworkManager.Instance.localPlayerController == null)
        {
            SetInteractTriggers(false, NoItemsText);
            return;
        }

        if (_itemInHand)
        {
            SetInteractTriggers(false, "");
            return;
        }
    
        if (!GameNetworkManager.Instance.localPlayerController.isHoldingObject)
        {
            SetInteractTriggers(false, NoItemsText);
            return;
        }
    
        SetInteractTriggers(true, StartDepositText);
    }
    
    private void SetInteractTriggers(bool interactable = false, string hoverTip = StartDepositText)
    {
        triggerScript.interactable = interactable;
        if (interactable) triggerScript.hoverTip = hoverTip;
        else triggerScript.disabledHoverTip = hoverTip;
    }

    public void OnAnimationEventKillPlayer()
    {
        StartCoroutine(KillTargetPlayer());
    }
    
    public void OnAnimationEventDespawnHeldItem()
    {
        if (!NetworkManager.Singleton.IsClient || !netcodeController.IsOwner) return;
        netcodeController.DespawnHeldItemServerRpc(_vendingMachineId);
    }

    private void HandleUpdateColaNetworkObjectReference(string recievedVendingMachineId,
        NetworkObjectReference colaNetworkObjectReference)
    {
        if (_vendingMachineId != recievedVendingMachineId) return;
        if (!colaNetworkObjectReference.TryGet(out NetworkObject colaNetworkObject)) return;
        _cola = colaNetworkObject;
    }
    
    private void HandleChangeTargetPlayer(string recievedVendingMachineId, int playerClientId)
    {
        if (_vendingMachineId != recievedVendingMachineId) return;
        if (playerClientId == -69420)
        {
            _targetPlayer = null;
            return;
        }

        _targetPlayer = StartOfRound.Instance.allPlayerScripts[playerClientId];
    }
    
    private void HandleInitializeConfigValues(string recievedVendingMachineId)
    {
        if (_vendingMachineId != recievedVendingMachineId) return;
    }

    private void SetBool(string recievedVendingMachineId, int parameter, bool value)
    {
        if (_vendingMachineId != recievedVendingMachineId) return;
        animator.SetBool(parameter, value);
    }

    private void SetTrigger(string recievedVendingMachineId, int parameter)
    {
        if (_vendingMachineId != recievedVendingMachineId) return;
        animator.SetTrigger(parameter);
    }

    private void HandleUpdateVendingMachineIdentifier(string recievedVendingMachineId)
    {
        _vendingMachineId = recievedVendingMachineId;
    }

    private void LogDebug(string msg)
    {
        #if DEBUG
        _mls?.LogInfo(msg);
        #endif
    }
}