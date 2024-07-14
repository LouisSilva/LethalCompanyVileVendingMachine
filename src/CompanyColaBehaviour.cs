using System;
using System.Collections.Generic;
using System.Linq;
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
    
    [HideInInspector] public bool isPartOfVendingMachine;
    
    private bool _hasBeenPickedUp;

    public override void Start()
    {
        // Doing this prop collider stuff is needed because if not, the start method breaks the custom cola physics.
        // I also can't just not use the base.Start(), because it breaks compatibility with mods.
        propColliders = gameObject.GetComponentsInChildren<Collider>();
        List<LayerMask> propCollidersExcludeLayersBeforeStart = new(propColliders.Length);
        propCollidersExcludeLayersBeforeStart.AddRange(propColliders.Select(propCollider => propCollider.excludeLayers));

        base.Start();

        for (int index = 0; index < propColliders.Length; ++index)
        {
            if (index < propCollidersExcludeLayersBeforeStart.Count)
                propColliders[index].excludeLayers = propCollidersExcludeLayersBeforeStart[index];
        }
        
        grabbable = true;
        grabbableToEnemies = true;
        
        if (IsServer)
        {
            _colaId = Guid.NewGuid().ToString();
            SyncColaIdClientRpc(_colaId);
        }
    }

    public override void Update()
    {
        if (isHeld && isPartOfVendingMachine)
        {
            isPartOfVendingMachine = false;
            foreach (Collider propCollider in propColliders)
                propCollider.excludeLayers = -2621449;
        }
        
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
    public void SyncColaIdClientRpc(string receivedColaId)
    {
        _colaId = receivedColaId;
        _mls?.Dispose();
        _mls = Logger.CreateLogSource($"{VileVendingMachinePlugin.ModGuid} | Company Cola {_colaId}");
        
        LogDebug("Successfully synced company cola identifier.");
    }

    private void LogDebug(string msg)
    {
#if DEBUG
        _mls?.LogInfo(msg);
#endif
    }
}