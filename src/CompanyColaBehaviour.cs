using System;
using BepInEx.Logging;
using Unity.Netcode;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace LethalCompanyVileVendingMachine;

public class CompanyColaBehaviour : PhysicsProp
{
    private ManualLogSource _mls;
    private string _colaId;

    #pragma warning disable 0649
    [SerializeField] private ScanNodeProperties innerScanNode;
    [SerializeField] private ScanNodeProperties outerScanNode;
    #pragma warning restore 0649
    
    public bool isPartOfVendingMachine;
    
    private bool _hasBeenPickedUp;

    public override void Start()
    {
        base.Start();
        grabbable = true;
        grabbableToEnemies = true;
        
        if (!IsServer)
        {
            _colaId = Guid.NewGuid().ToString();
            _mls = Logger.CreateLogSource($"{VileVendingMachinePlugin.ModGuid} | Company Cola {_colaId}");
            SyncColaIdClientRpc(_colaId);
        }
    }

    public override void Update()
    {
        LogDebug($"Is part of vending machine?: {isPartOfVendingMachine}");
        LogDebug($"Is my parent null?: {transform.parent == null}");
        LogDebug($"My position: {transform.position}");
        
        if (isHeld && isPartOfVendingMachine) isPartOfVendingMachine = false;
        if (isPartOfVendingMachine) return;
        base.Update();
    }

    public override void LateUpdate()
    {
        transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
        
        if (isPartOfVendingMachine)
        {
            if (transform.parent != null)
            {
                transform.position = transform.parent.position;
                transform.rotation = transform.parent.rotation;
            }
            return;
        }
        base.LateUpdate();
    }

    public override void EquipItem()
    {
        base.EquipItem();
        isPartOfVendingMachine = false;

        RemoveRigidbodyAndDoubleScanNodes();
    }

    public override void GrabItem()
    {
        base.GrabItem();
        isPartOfVendingMachine = false;
        
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
        SetScrapValue(value);
        
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

    [ClientRpc]
    public void SyncColaIdClientRpc(string colaId)
    {
        if (IsServer) return;
        _colaId = colaId;
        _mls = Logger.CreateLogSource($"{VileVendingMachinePlugin.ModGuid} | Company Cola {_colaId}");
    }

    private void LogDebug(string msg)
    {
        #if DEBUG
        try
        {
            _mls.LogInfo(msg);
        }
        catch (Exception e)
        {
        }
            
        #endif
    }
}