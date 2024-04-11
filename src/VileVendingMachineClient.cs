using System.Collections;
using BepInEx.Logging;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.VFX;
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
    public AudioClip dropCanSfx;
    public AudioClip blendSfx;
    public AudioClip flapCreakFullSfx;
    public AudioClip crunchSfx;
    public AudioClip flapCreakOpenSfx;
    public AudioClip flapCreakCloseSfx;

    [Header("Transforms")] [Space(5f)]
    [SerializeField] private Transform handBone;
    [SerializeField] private Transform itemHolder;
    [SerializeField] private Transform eye;

    [Header("Controllers")] [Space(5f)] 
    [SerializeField] private VileVendingMachineNetcodeController netcodeController;
    [SerializeField] private Animator animator;
    [SerializeField] private InteractTrigger triggerScript;
    [SerializeField] private SkinnedMeshRenderer renderer;
    [SerializeField] private VisualEffect materializeVfx;
    #pragma warning restore 0649

    public enum AudioClipTypes
    {
        DropCan,
        Blend,
        FlapCreak,
        Crunch,
    }

    public static readonly int ArmAccept = Animator.StringToHash("accept");
    public static readonly int ArmGrab = Animator.StringToHash("grab");
    public static readonly int ArmRetract = Animator.StringToHash("retract");
    public static readonly int Grind = Animator.StringToHash("grind");
    public static readonly int Dispense = Animator.StringToHash("dispense");
    
    private const string StartDepositText = "Place item: [E]";
    private const string DepositText = "Placing item...";
    private const string NoItemsText = "No items to place...";

    private bool _itemInHand;
    private bool _increasingFearLevel;

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
        netcodeController.OnSetMeshEnabled += HandleSetMeshEnabled;
        netcodeController.OnPlayMaterializeVfx += HandlePlayMaterializeVfx;
        netcodeController.OnPlayCreatureSfx += HandlePlayCreatureSfx;
        netcodeController.OnIncreaseFearLevelWhenPlayerBlended += HandleIncreaseFearLevelWhenPlayerBlended;
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
        netcodeController.OnSetMeshEnabled -= HandleSetMeshEnabled;
        netcodeController.OnPlayMaterializeVfx -= HandlePlayMaterializeVfx;
        netcodeController.OnPlayCreatureSfx -= HandlePlayCreatureSfx;
        netcodeController.OnIncreaseFearLevelWhenPlayerBlended -= HandleIncreaseFearLevelWhenPlayerBlended;
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

    private void HandleIncreaseFearLevelWhenPlayerBlended(string receivedVendingMachineId)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;

        _increasingFearLevel = true;
        LogDebug("Starting coroutine for increasing fear");
        StartCoroutine(IncreaseFearLevelForArmGrab());
    }

    private IEnumerator IncreaseFearLevelForArmGrab()
    {
        while (_increasingFearLevel)
        {
            if (GameNetworkManager.Instance.localPlayerController.HasLineOfSightToPosition(eye.position, 50f, 30, 3f))
            {
                LogDebug("Increasing fear level");
                GameNetworkManager.Instance.localPlayerController.JumpToFearLevel(1);
                GameNetworkManager.Instance.localPlayerController.IncreaseFearLevelOverTime(0.8f);
            }

            yield return new WaitForSeconds(0.1f);
        }
    }

    private void HandlePlayCreatureSfx(string receivedVendingMachineId, int audioClipType, bool interrupt = true)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;

        AudioClip audioClip = audioClipType switch
        {
            (int)AudioClipTypes.DropCan => dropCanSfx,
            (int)AudioClipTypes.Blend => blendSfx,
            (int)AudioClipTypes.FlapCreak => flapCreakFullSfx,
            (int)AudioClipTypes.Crunch => crunchSfx,
            _ => null
        };

        if (audioClip == null)
        {
            _mls.LogError($"Vending machine audio clip index '{audioClipType}' is null");
            return;
        }
        
        PlaySfx(audioClip, interrupt);
    }

    private void PlaySfx(AudioClip clip, bool interrupt = true)
    {
        if (clip == null)
        {
            _mls.LogError($"Vending machine audio clip is null");
            return;
        }
        
        LogDebug($"Playing audio clip: {clip.name}");
        
        if (interrupt) creatureSfxSource.Stop(true);
        creatureSfxSource.PlayOneShot(clip);
        WalkieTalkie.TransmitOneShotAudio(creatureSfxSource, clip, creatureSfxSource.volume);
    }
    
    private void HandlePlayerDiscardHeldItem(string receivedVendingMachineId, int playerClientId)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;
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

    private void HandlePlaceItemInHand(string receivedVendingMachineId, NetworkObjectReference networkObjectReference,
        Vector3 position)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;
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
            HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);
            yield return new WaitForSeconds(0.05f);
            _targetPlayer.KillPlayer(Vector3.zero, true, CauseOfDeath.Crushing, 0);
        }

        yield return new WaitUntil(() => _targetPlayer.deadBody != null);
        _increasingFearLevel = false;
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

    public void OnAnimationEventIncreaseFearLevelWhenPlayerBlended()
    {
        HandleIncreaseFearLevelWhenPlayerBlended(_vendingMachineId);
    }
    
    public void OnAnimationEventStopFearIncreasing()
    {
        _increasingFearLevel = false;
    }

    public void OnAnimationEventPlayFlapCreak(int type)
    {
        switch (type)
        {
            case 0:
                PlaySfx(flapCreakOpenSfx);
                break;
            case 1:
                PlaySfx(flapCreakCloseSfx);
                break;
        }
    }

    public void OnAnimationEventEnableColaPhysics()
    {
        PlaySfx(flapCreakFullSfx);
        _itemInHand = false;
        
        CompanyColaBehaviour cola = _cola.GetComponent<CompanyColaBehaviour>();
        cola.grabbable = true;
        cola.grabbableToEnemies = true;
        
        _cola.transform.SetParent(null);
        Rigidbody colaRigidbody = _cola.GetComponent<Rigidbody>();
        colaRigidbody.isKinematic = false;
        colaRigidbody.AddForce(transform.forward * 5f, ForceMode.Impulse);
        colaRigidbody.AddTorque(transform.right * 20f, ForceMode.Impulse);
    }

    public void OnAnimationEventSpawnCola()
    {
        PlaySfx(crunchSfx, false);
    }

    public void OnAnimationEventPlayBlendSfx()
    {
        PlaySfx(blendSfx);
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

    private void HandlePlayMaterializeVfx(string receivedVendingMachineId, Vector3 finalPosition, Quaternion finalRotation)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;
        StartCoroutine(PlayMaterializeVfx(finalPosition, finalRotation));
    }

    private IEnumerator PlayMaterializeVfx(Vector3 finalPosition, Quaternion finalRotation)
    {
        renderer.enabled = false;
        transform.position = finalPosition;
        transform.rotation = finalRotation;
        
        materializeVfx.SendEvent("PlayMaterialize");
        yield return new WaitForSeconds(3);
        renderer.enabled = true;
    }

    private void HandleSetMeshEnabled(string receivedVendingMachineId, bool meshEnabled)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;
        renderer.enabled = meshEnabled;
    }

    private void HandleUpdateColaNetworkObjectReference(string receivedVendingMachineId,
        NetworkObjectReference colaNetworkObjectReference, int colaValue)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;
        if (!colaNetworkObjectReference.TryGet(out NetworkObject colaNetworkObject)) return;
        _cola = colaNetworkObject;

        CompanyColaBehaviour colaBehaviour = _cola.GetComponent<CompanyColaBehaviour>();
        colaBehaviour.UpdateScrapValue(colaValue);
    }
    
    private void HandleChangeTargetPlayer(string receivedVendingMachineId, int playerClientId)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;
        if (playerClientId == -69420)
        {
            _targetPlayer = null;
            return;
        }

        _targetPlayer = StartOfRound.Instance.allPlayerScripts[playerClientId];
    }
    
    private void HandleInitializeConfigValues(string receivedVendingMachineId)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;
    }

    private void SetBool(string receivedVendingMachineId, int parameter, bool value)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;
        animator.SetBool(parameter, value);
    }

    private void SetTrigger(string receivedVendingMachineId, int parameter)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;
        animator.SetTrigger(parameter);
    }

    private void HandleUpdateVendingMachineIdentifier(string receivedVendingMachineId)
    {
        _vendingMachineId = receivedVendingMachineId;
    }

    private void LogDebug(string msg)
    {
        #if DEBUG
        _mls?.LogInfo(msg);
        #endif
    }
}