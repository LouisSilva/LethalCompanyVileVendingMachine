﻿using System;
using BepInEx.Logging;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace LethalCompanyVileVendingMachine;

public class CompanyColaBehaviour : PhysicsProp
{
    private ManualLogSource _mls;
    private string _colaId;
    
    [SerializeField] private float stabilityThreshold = 0.005f;
    [SerializeField] private float requiredStableTime = 0.2f;

    public AudioSource colaAudioSource;
    public AudioClip[] colaAudioClips;

    #pragma warning disable 0649
    [SerializeField] private ScanNodeProperties innerScanNode;
    [SerializeField] private ScanNodeProperties outerScanNode;
    #pragma warning restore 0649
    
    public bool isPartOfVendingMachine;
    public bool isPhysicsEnabled;
    
    private bool _isFalling;
    private bool _hasBeenPickedUp;
    
    private float _lastPositionY;
    private float _positionStableCounter;

    private void Awake()
    {
        _colaId = Guid.NewGuid().ToString();
        _mls = Logger.CreateLogSource($"{VileVendingMachinePlugin.ModGuid} | Company Cola {_colaId}");
    }

    public override void Start()
    {
        base.Start();
        grabbable = true;
        grabbableToEnemies = true;
        _lastPositionY = transform.position.y;
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

        RemoveRigidbodyAndDoubleScanNodes();
    }

    public override void GrabItem()
    {
        base.GrabItem();
        isPartOfVendingMachine = false;
        isPhysicsEnabled = false;
        
        RemoveRigidbodyAndDoubleScanNodes();
    }

    private void RemoveRigidbodyAndDoubleScanNodes()
    {
        if (_hasBeenPickedUp) return;
        Destroy(GetComponent<Rigidbody>());
        Destroy(outerScanNode);

        _hasBeenPickedUp = true;
    }

    public void UpdateScrapValue(int value)
    {
        // Two scan node property scripts are needed, because the rigidbody somehow makes the scan node gameobject "hidden"
        if (innerScanNode != null)
        {
            LogDebug("Inner scan node is not null");
            innerScanNode.scrapValue = value;
            innerScanNode.subText = $"Value: {value}";
        }
        
        if (outerScanNode != null)
        {
            LogDebug("Outer scan node is not null");
            outerScanNode.scrapValue = value;
            outerScanNode.subText = $"Value: {value}";
        }
    }

    private void LogDebug(string msg)
    {
        #if DEBUG
        if (!IsOwner) return;
        _mls.LogInfo(msg);
        #endif
    }
}