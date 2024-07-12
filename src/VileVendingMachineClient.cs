using System.Collections;
using BepInEx.Logging;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.VFX;

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
    private const string CannotPlaceItemText = "No space to place item...";
    
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
    
    [Header("Visuals")] [Space(5f)]
    [SerializeField] private Animator animator;
    [SerializeField] private SkinnedMeshRenderer bodyRenderer;
    [SerializeField] private MeshRenderer colaPlaceholderRenderer;
    [SerializeField] private VisualEffect materializeVfx;
    [SerializeField] private GameObject scanNode;

    [Header("Controllers")] [Space(5f)] 
    [SerializeField] private VileVendingMachineNetcodeController netcodeController;
    [SerializeField] private InteractTrigger triggerScript;
    #pragma warning restore 0649
    
    private bool _increasingFearLevel;

    private readonly NullableObject<PlayerControllerB> _targetPlayer = null;
    
    private CompanyColaBehaviour _currentColaBehaviour;

    private void OnEnable()
    {
        netcodeController.OnInitializeConfigValues += HandleInitializeConfigValues;
        netcodeController.OnUpdateVendingMachineIdentifier += HandleUpdateVendingMachineIdentifier;
        netcodeController.OnSetAnimationTrigger += HandleSetAnimationTrigger;
        netcodeController.OnPlaceItemInHand += HandlePlaceItemInHand;
        netcodeController.OnUpdateColaNetworkObjectReference += HandleUpdateColaNetworkObjectReference;
        netcodeController.OnPlayMaterializeVfx += HandlePlayMaterializeVfx;
        netcodeController.OnPlayCreatureSfx += HandlePlayCreatureSfx;
        netcodeController.OnIncreaseFearLevelWhenPlayerBlended += HandleIncreaseFearLevelWhenPlayerBlended;
        
        netcodeController.TargetPlayerClientId.OnValueChanged += HandleTargetPlayerChanged;
        netcodeController.MeshEnabled.OnValueChanged += HandleSetMeshEnabled;
    }
    
    private void OnDisable()
    {
        netcodeController.OnInitializeConfigValues -= HandleInitializeConfigValues;
        netcodeController.OnUpdateVendingMachineIdentifier -= HandleUpdateVendingMachineIdentifier;
        netcodeController.OnSetAnimationTrigger -= HandleSetAnimationTrigger;
        netcodeController.OnPlaceItemInHand -= HandlePlaceItemInHand;
        netcodeController.OnUpdateColaNetworkObjectReference -= HandleUpdateColaNetworkObjectReference;
        netcodeController.OnPlayMaterializeVfx -= HandlePlayMaterializeVfx;
        netcodeController.OnPlayCreatureSfx -= HandlePlayCreatureSfx;
        netcodeController.OnIncreaseFearLevelWhenPlayerBlended -= HandleIncreaseFearLevelWhenPlayerBlended;
        
        netcodeController.TargetPlayerClientId.OnValueChanged -= HandleTargetPlayerChanged;
        netcodeController.MeshEnabled.OnValueChanged += HandleSetMeshEnabled;
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
        colaPlaceholderRenderer.enabled = true;
        #else
        colaPlaceholderRenderer.enabled = false;
        #endif
    }

    private void Update()
    {
        UpdateInteractTriggers();
    }
    
    private void InteractVendingMachine(PlayerControllerB playerInteractor)
    {
        if (playerInteractor == null) return;
        if (netcodeController.IsItemOnHand.Value) return;
        if (!playerInteractor.isHoldingObject) return;
        if (GameNetworkManager.Instance.localPlayerController == playerInteractor) netcodeController.PlaceItemInHandServerRpc(_vendingMachineId, playerInteractor.actualClientId);
    }

    private void HandlePlaceItemInHand(string receivedVendingMachineId, ulong targetPlayerId)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;
        StartCoroutine(PlaceItemInHand(targetPlayerId));
    }

    private IEnumerator PlaceItemInHand(ulong targetPlayerId)
    {
        if (netcodeController.IsOwner)
            netcodeController.TargetPlayerClientId.Value = targetPlayerId;
        
        // Make the player discard their held item onto the vending machine's hand
        PlayerControllerB player = StartOfRound.Instance.allPlayerScripts[targetPlayerId];
        if (player == null)
        {
            LogDebug($"There is no player with ID: {targetPlayerId}. Cannot place item onto vending machine's hand.");
            yield break;
        }

        GrabbableObject item = player.currentlyHeldObjectServer;
        Vector3 placePosition = itemHolder.transform.position;
        placePosition.y += item.itemProperties.verticalOffset;
        placePosition = itemHolder.InverseTransformPoint(placePosition);

        if (GameNetworkManager.Instance.localPlayerController == player)
        {
            player.DiscardHeldObject(true, GetComponent<NetworkObject>(), placePosition, false);
        }

        // Make sure the discard held object stuff has fully synced
        yield return new WaitForSeconds(0.25f);
        yield return null;

        item.isHeldByEnemy = true;
        item.grabbable = false;
        item.grabbableToEnemies = false;
        if (netcodeController.IsOwner) netcodeController.IsItemOnHand.Value = true;
        yield return null;
        
        item.EnablePhysics(false);
        item.transform.SetParent(itemHolder);
        item.transform.position = placePosition;

        item.transform.rotation = item.floorYRot == -1
            ? Quaternion.Euler(
                item.itemProperties.restingRotation.x,
                item.itemProperties.restingRotation.y,
                item.itemProperties.restingRotation.z)

            : Quaternion.Euler(
                item.itemProperties.restingRotation.x,
                item.floorYRot + item.itemProperties.floorYOffset + 90f,
                item.itemProperties.restingRotation.z);

        yield return null;
        if (GameNetworkManager.Instance.localPlayerController == player) netcodeController.StartAcceptItemAnimationServerRpc(_vendingMachineId);
    }
    
    private void HandleIncreaseFearLevelWhenPlayerBlended(string receivedVendingMachineId)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;

        _increasingFearLevel = true;
        LogDebug("Starting coroutine for increasing fear.");
        StartCoroutine(IncreaseFearLevelForArmGrab());
    }

    private IEnumerator IncreaseFearLevelForArmGrab()
    {
        while (_increasingFearLevel)
        {
            if (GameNetworkManager.Instance.localPlayerController.HasLineOfSightToPosition(eye.position, 50f, 30, 3f))
            {
                LogDebug("Increasing fear level.");
                GameNetworkManager.Instance.localPlayerController.JumpToFearLevel(1);
                GameNetworkManager.Instance.localPlayerController.IncreaseFearLevelOverTime(0.8f);
            }

            yield return new WaitForSeconds(0.15f);
        }
    }

    private IEnumerator KillTargetPlayer()
    {
        if (!_targetPlayer.IsNotNull)
        {
            _mls.LogError("Tried to kill target player, but the target player variable is null.");
            yield break;
        }
        
        LogDebug($"Killing player: {_targetPlayer.Value.name}.");
        
        // Shake peoples screens if they are near
        if (HUDManager.Instance.localPlayer == _targetPlayer.Value) HUDManager.Instance.ShakeCamera(ScreenShakeType.Big);
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
        _targetPlayer.Value.KillPlayer(Vector3.zero, true, CauseOfDeath.Crushing);

        yield return new WaitUntil(() => _targetPlayer.Value.deadBody != null);
        _increasingFearLevel = false;
        if (_targetPlayer.Value.deadBody != null)
        {
            _targetPlayer.Value.deadBody.attachedTo = handBone.transform;
            _targetPlayer.Value.deadBody.attachedLimb = _targetPlayer.Value.deadBody.bodyParts[5];
            _targetPlayer.Value.deadBody.matchPositionExactly = true;

            foreach (Rigidbody body in _targetPlayer.Value.deadBody.bodyParts)
                body.GetComponent<Collider>().excludeLayers = ~0;
        }

        yield return new WaitForSeconds(1.5f);
        if (_targetPlayer.Value.deadBody != null)
        {
            _targetPlayer.Value.deadBody.attachedTo = null;
            _targetPlayer.Value.deadBody.attachedLimb = null;
            _targetPlayer.Value.deadBody.matchPositionExactly = false;
            _targetPlayer.Value.deadBody.transform.GetChild(0).gameObject.SetActive(false);
            _targetPlayer.Value.deadBody = null;
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
        if (netcodeController.IsOwner) netcodeController.IsItemOnHand.Value = false;
        
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
        if (netcodeController.IsItemOnHand.Value)
        {
            SetInteractTriggers(false, CannotPlaceItemText);
            return;
        }
    
        if (!GameNetworkManager.Instance.localPlayerController.isHoldingObject)
        {
            SetInteractTriggers(false, NoItemsText);
            return;
        }
    
        SetInteractTriggers(true, PlaceItemText);
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
        if (netcodeController.MeshEnabled.Value && netcodeController.IsOwner)
            netcodeController.MeshEnabled.Value = false;
        transform.position = finalPosition;
        transform.rotation = finalRotation;
        
        Destroy(frontCollider.gameObject);
        Destroy(backCollider.gameObject);
        Destroy(floorCollider.gameObject);
        Destroy(centreCollider.gameObject);
        
        enemyCollider.gameObject.SetActive(true);
        materializeVfx.SendEvent("PlayMaterialize");

        if (!netcodeController.IsOwner) yield break;
        yield return new WaitForSeconds(3);
        netcodeController.MeshEnabled.Value = true;
    }

    private void HandleSetMeshEnabled(bool oldValue, bool newValue)
    {
        bodyRenderer.enabled = newValue;
        scanNode.SetActive(newValue);
    }

    private void HandleUpdateColaNetworkObjectReference(string receivedVendingMachineId,
        NetworkObjectReference colaNetworkObjectReference, int colaValue)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;
        if (!colaNetworkObjectReference.TryGet(out NetworkObject colaNetworkObject)) return;
        LogDebug("Cola network object reference was not null.");

        _currentColaBehaviour = colaNetworkObject.GetComponent<CompanyColaBehaviour>();
        _currentColaBehaviour.isPartOfVendingMachine = true;
        _currentColaBehaviour.transform.position = colaPlaceholder.transform.position;
        _currentColaBehaviour.transform.rotation = colaPlaceholder.transform.rotation;
        _currentColaBehaviour.transform.SetParent(colaPlaceholder, false);
        _currentColaBehaviour.UpdateScrapValue(colaValue);
        _currentColaBehaviour.grabbableToEnemies = false;
        _currentColaBehaviour.fallTime = 1f;
        
        LogDebug($"Cola parent is: {_currentColaBehaviour.transform.parent.name}.");
    }
    
    private void HandleInitializeConfigValues(string receivedVendingMachineId)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;

        creatureVoiceSource.volume = VileVendingMachineConfig.Instance.SoundEffectsVolume.Value;
        creatureSfxSource.volume = VileVendingMachineConfig.Instance.SoundEffectsVolume.Value;
    }

    private void HandleSetAnimationTrigger(string receivedVendingMachineId, int parameter)
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
            _mls.LogError($"Vending machine audio clip index '{audioClipType}' is null.");
            return;
        }
        
        PlaySfx(audioClip, interrupt);
    }

    private void PlaySfx(AudioClip clip, bool interrupt = true)
    {
        if (clip == null)
        {
            _mls.LogError($"Vending machine audio clip is null, could not play it.");
            return;
        }
        
        LogDebug($"Playing audio clip: {clip.name}.");
        
        if (interrupt) creatureSfxSource.Stop(true);
        creatureSfxSource.PlayOneShot(clip);
        WalkieTalkie.TransmitOneShotAudio(creatureSfxSource, clip, creatureSfxSource.volume);
    }
    
    private void HandleTargetPlayerChanged(ulong oldValue, ulong newValue)
    {
        _targetPlayer.Value = newValue == 69420 ? null : StartOfRound.Instance.allPlayerScripts[newValue];
        LogDebug(_targetPlayer.IsNotNull
            ? $"Changed target player to {_targetPlayer.Value!.playerUsername}"
            : "Changed target player to null");
    }

    private void HandleUpdateVendingMachineIdentifier(string receivedVendingMachineId)
    {
        _vendingMachineId = receivedVendingMachineId;
        _mls?.Dispose();
        _mls = BepInEx.Logging.Logger.CreateLogSource(
            $"{VileVendingMachinePlugin.ModGuid} | Vile Vending Machine Client {_vendingMachineId}");
        
        LogDebug("Successfully synced vending machine identifier.");
    }

    private void LogDebug(string msg)
    {
        #if DEBUG
        _mls?.LogInfo(msg);
        #endif
    }
}