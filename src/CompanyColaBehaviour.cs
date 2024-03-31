using System;
using BepInEx.Logging;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace LethalCompanyVileVendingMachine;

public class CompanyColaBehaviour : PhysicsProp
{
    private ManualLogSource _mls;

    private string _colaId;

    private bool _isFalling;
    private float _lastPositionY;
    private float _positionStableCounter;
    [SerializeField] private float stabilityThreshold = 0.005f;
    [SerializeField] private float requiredStableTime = 0.2f;

    public AudioSource colaAudioSource;
    public AudioClip[] colaAudioClips;

    public bool isPartOfVendingMachine;
    public bool isPhysicsEnabled;

    public override void Start()
    {
        _colaId = Guid.NewGuid().ToString();
        _mls = Logger.CreateLogSource($"{VileVendingMachinePlugin.ModGuid} | Company Cola {_colaId}");
        
        propColliders = gameObject.GetComponentsInChildren<Collider>();
        originalScale = transform.localScale;
        fallTime = 1f;
        hasHitGround = true;
        reachedFloorTarget = true;
        targetFloorPosition = transform.localPosition;
        _lastPositionY = transform.position.y;
        
        if(RoundManager.Instance.mapPropsContainer != null)
            radarIcon = Instantiate(StartOfRound.Instance.itemRadarIconPrefab, RoundManager.Instance.mapPropsContainer.transform).transform;

        if (IsOwner) HoarderBugAI.grabbableObjectsInMap.Add(gameObject);
        foreach (MeshRenderer componentsInChild in gameObject.GetComponentsInChildren<MeshRenderer>())
            componentsInChild.renderingLayerMask = 1U;
        foreach (SkinnedMeshRenderer componentsInChild in gameObject.GetComponentsInChildren<SkinnedMeshRenderer>())
            componentsInChild.renderingLayerMask = 1U;
    }

    public override void Update()
    {
        if (isHeld && isPartOfVendingMachine) isPartOfVendingMachine = false;
        if (isPartOfVendingMachine) return;
        base.Update();
    }

    public override void LateUpdate()
    {
        transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
        
        if (isPartOfVendingMachine) return;
        base.LateUpdate();
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;
        if (!isPhysicsEnabled) return;
        
        float yPos = transform.position.y;
        if (yPos < _lastPositionY)
        {
            _isFalling = true;
            _positionStableCounter = 0.0f;
        }
        else if (Mathf.Abs(yPos - _lastPositionY) <= stabilityThreshold)
        {
            _positionStableCounter += Time.fixedDeltaTime;
        }

        _lastPositionY = yPos;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsOwner) return;
        if (collision.collider.tag is "Player" or "PhysicsProp" or "PlayerRagdoll" or "Enemy") return;
        if (!_isFalling || !(_positionStableCounter >= requiredStableTime)) return;
        
        PlayDropSFX();
        _isFalling = false;
    }

    public override void EquipItem()
    {
        base.EquipItem();
        isPartOfVendingMachine = false;
        isPhysicsEnabled = false;
    }

    public override void GrabItem()
    {
        base.GrabItem();
        isPartOfVendingMachine = false;
        isPhysicsEnabled = false;
        if (IsOwner) Destroy(GetComponent<Rigidbody>());
        if (IsOwner) Destroy(GetComponent<ScanNodeProperties>());
    }

    public void UpdateScrapValue(int value)
    {
        // Two scan node property scripts are needed, because the rigidbody somehow makes the scan node gameobject "hidden"
        ScanNodeProperties scanNode1 = GetComponent<ScanNodeProperties>();
        if (scanNode1 != null)
        {
            scanNode1.scrapValue = value;
            scanNode1.subText = $"Value: {value}";
        }
        
        ScanNodeProperties scanNode2 = GetComponentInChildren<ScanNodeProperties>();
        if (scanNode2 != null)
        {
            scanNode2.scrapValue = value;
            scanNode2.subText = $"Value: {value}";
        }
        
        SetScrapValue(value);
    }

    private void LogDebug(string msg)
    {
        #if DEBUG
        if (!IsOwner) return;
        _mls.LogInfo(msg);
        #endif
    }
}