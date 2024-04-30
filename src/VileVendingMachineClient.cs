using System.Collections;
using BepInEx.Logging;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.VFX;
using Logger = UnityEngine.Logger;

namespace LethalCompanyVileVendingMachine;

public class VileVendingMachineClient : MonoBehaviour
{
    private ManualLogSource _mls;
    private string _vendingMachineId;

    private enum AudioClipTypes
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
    
    private const string StartPlaceItemText = "Place item: [E]";
    private const string PlaceItemText = "Placing item...";
    private const string NoItemsText = "No items to place...";
    
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

    [Header("Colliders and Transforms")] [Space(5f)]
    [SerializeField] private BoxCollider centreCollider;
    [SerializeField] private BoxCollider frontCollider;
    [SerializeField] private BoxCollider backCollider;
    [SerializeField] private BoxCollider floorCollider;
    [SerializeField] private BoxCollider enemyCollider;
    [SerializeField] private Transform handBone;
    [SerializeField] private Transform itemHolder;
    [SerializeField] private Transform eye;
    [SerializeField] private Transform colaPlaceholder;

    [Header("Controllers")] [Space(5f)] 
    [SerializeField] private VileVendingMachineNetcodeController netcodeController;
    [SerializeField] private Animator animator;
    [SerializeField] private InteractTrigger triggerScript;
    [SerializeField] private SkinnedMeshRenderer renderer;
    [SerializeField] private VisualEffect materializeVfx;
    #pragma warning restore 0649

    private bool _isItemOnHand;
    private bool _increasingFearLevel;

    private PlayerControllerB _targetPlayer;
    
    private CompanyColaBehaviour _currentColaBehaviour;

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
        netcodeController.OnSetIsItemOnHand += HandleSetIsItemOnHand;
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
        netcodeController.OnSetIsItemOnHand -= HandleSetIsItemOnHand;
    }

    private void Start()
    {
        _mls = BepInEx.Logging.Logger.CreateLogSource(
            $"{VileVendingMachinePlugin.ModGuid} | Vile Vending Machine Client {_vendingMachineId}");
        
        triggerScript = GetComponentInChildren<InteractTrigger>();
        triggerScript.onInteract.AddListener(InteractVendingMachine);
        triggerScript.tag = nameof(InteractTrigger);
        triggerScript.interactCooldown = false;
        triggerScript.cooldownTime = 0;
        
        #if DEBUG
        colaPlaceholder.GetComponent<MeshRenderer>().enabled = true;
        #endif
    }

    private void Update()
    {
        UpdateInteractTriggers();
    }
    
    private void InteractVendingMachine(PlayerControllerB playerInteractor)
    {
        if (!playerInteractor.isHoldingObject) return;
        PlaceItemInHand(ref playerInteractor);
    }
    
    private void PlaceItemInHand(ref PlayerControllerB playerInteractor)
    {
        if (_isItemOnHand) return;
        if (GameNetworkManager.Instance == null) return;

        _isItemOnHand = true;
        
        if (GameNetworkManager.Instance.localPlayerController == playerInteractor) netcodeController.ChangeTargetPlayerServerRpc(_vendingMachineId, playerInteractor.actualClientId);
        HandlePlayerDiscardHeldItem(_vendingMachineId, playerInteractor.actualClientId);
        if (GameNetworkManager.Instance.localPlayerController == playerInteractor) netcodeController.StartAcceptItemAnimationServerRpc(_vendingMachineId);
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
    
    private void HandlePlayerDiscardHeldItem(string receivedVendingMachineId, ulong playerClientId)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;
        _isItemOnHand = true;

        PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[playerClientId];
        GrabbableObject item = player.currentlyHeldObjectServer;
        
        if (item == null) item = player.currentlyHeldObject;
        if (item == null) item = player.currentlyGrabbingObject;

        Vector3 placePosition = itemHolder.GetComponent<Transform>().position;
        placePosition.y += item.itemProperties.verticalOffset;
        placePosition = itemHolder.InverseTransformPoint(placePosition);
        
        player.DiscardHeldObject(true, GetComponent<NetworkObject>(), placePosition, false);
        if (GameNetworkManager.Instance.localPlayerController == player) netcodeController.PlaceItemInHandServerRpc(_vendingMachineId, item.GetComponent<NetworkObject>(), placePosition);
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
        _isItemOnHand = true;
        
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
        LogDebug($"Killing player: {_targetPlayer.name}");
        
        // Shake peoples screens if they are near
        if (HUDManager.Instance.localPlayer == _targetPlayer) HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);
        else
        {
            // If player is very near
            if (Vector3.Distance(HUDManager.Instance.localPlayer.transform.position,
                    transform.position) < 2f) {HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);}
            
            // If player is kinda near ish
            else if (Vector3.Distance(HUDManager.Instance.localPlayer.transform.position,
                            transform.position) < 8f) HUDManager.Instance.ShakeCamera(ScreenShakeType.Small);
        }
        
        yield return new WaitForSeconds(0.05f);
        _targetPlayer.KillPlayer(Vector3.zero, true, CauseOfDeath.Crushing);

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
        _isItemOnHand = false;
        
        _currentColaBehaviour.grabbable = true;
        _currentColaBehaviour.grabbableToEnemies = true;
        
        _currentColaBehaviour.transform.SetParent(null);
        Rigidbody colaRigidbody = _currentColaBehaviour.GetComponent<Rigidbody>();
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

        if (_isItemOnHand)
        {
            SetInteractTriggers(false, "");
            return;
        }
    
        if (!GameNetworkManager.Instance.localPlayerController.isHoldingObject)
        {
            SetInteractTriggers(false, NoItemsText);
            return;
        }
    
        SetInteractTriggers(true);
    }
    
    private void SetInteractTriggers(bool interactable = false, string hoverTip = StartPlaceItemText)
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
        
        Destroy(frontCollider.gameObject);
        Destroy(backCollider.gameObject);
        Destroy(floorCollider.gameObject);
        Destroy(centreCollider.gameObject);
        
        enemyCollider.gameObject.SetActive(true);
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
        LogDebug("Cola network object reference was not null");

        _currentColaBehaviour = colaNetworkObject.GetComponent<CompanyColaBehaviour>();
        _currentColaBehaviour.isPartOfVendingMachine = true;
        _currentColaBehaviour.transform.position = colaPlaceholder.transform.position;
        _currentColaBehaviour.transform.rotation = colaPlaceholder.transform.rotation;
        _currentColaBehaviour.transform.SetParent(colaPlaceholder, false);
        _currentColaBehaviour.UpdateScrapValue(colaValue);
        _currentColaBehaviour.grabbableToEnemies = false;
        _currentColaBehaviour.fallTime = 1f;
        
        LogDebug($"Cola parent is: {_currentColaBehaviour.transform.parent.name}");
    }

    private void HandleSetIsItemOnHand(string receivedVendingMachineId, bool isItemOnHand)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;
        _isItemOnHand = isItemOnHand;
    }
    
    private void HandleChangeTargetPlayer(string receivedVendingMachineId, ulong playerClientId)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;
        if (playerClientId == 69420)
        {
            _targetPlayer = null;
            LogDebug("Target player is now null");
            return;
        }
        
        _targetPlayer = StartOfRound.Instance.allPlayerScripts[playerClientId];
        LogDebug($"Target player is now {_targetPlayer.playerUsername}, ishost?: {_targetPlayer.IsHost}");
    }
    
    private void HandleInitializeConfigValues(string receivedVendingMachineId)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;

        creatureVoiceSource.volume = VileVendingMachineConfig.Instance.SoundEffectsVolume.Value;
        creatureSfxSource.volume = VileVendingMachineConfig.Instance.SoundEffectsVolume.Value;
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

    private void HandleUpdateVendingMachineIdentifier(string receivedVendingMachineId)
    {
        _vendingMachineId = receivedVendingMachineId;
        _mls?.Dispose();
        _mls = BepInEx.Logging.Logger.CreateLogSource(
            $"{VileVendingMachinePlugin.ModGuid} | Vile Vending Machine {_vendingMachineId}");
        
        LogDebug("Successfully synced vending machine identifier");
    }

    private void LogDebug(string msg)
    {
        #if DEBUG
        _mls?.LogInfo(msg);
        #endif
    }
}