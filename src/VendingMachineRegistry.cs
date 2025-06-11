using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using System;
using UnityEngine;

namespace LethalCompanyVileVendingMachine;

internal enum LeftOrRight
{
    Left = 1,
    Right = 2,
}

internal enum EntranceOrExit
{
    Entrance = 0,
    Exit = 1
}

public class VendingMachineRegistry
{
    private static WeakReference<VendingMachineRegistry> _instanceWeakReference;
    private static readonly object Lock = new();
    
    private static readonly ManualLogSource Mls = 
        new($"{VileVendingMachinePlugin.ModGuid} | Vile Vending Machine Registry");

    public struct VendingMachinePlacement
    {
        public VileVendingMachineServer ServerScript;
        public int TeleportId;
        public Transform Transform;
        internal LeftOrRight LeftOrRight;
        internal EntranceOrExit EntranceOrExit;
    }

    private readonly Dictionary<string, VendingMachinePlacement> _vendingMachines = new();
    public bool IsPlacementInProgress;
    
    private VendingMachineRegistry() { }
    
    public static VendingMachineRegistry Instance
    {
        get
        {
            lock (Lock)
            {
                // Try to get the instance from the weak reference, if it hasn't been garbage collected
                if (_instanceWeakReference != null && _instanceWeakReference.TryGetTarget(out VendingMachineRegistry instance))
                {
                    return instance;
                }

                // If the instance is null or has been garbage collected, create a new one
                instance = new VendingMachineRegistry();
                _instanceWeakReference = new WeakReference<VendingMachineRegistry>(instance);

                return instance;
            }
        }
    }


    internal void AddVendingMachine(string id, int teleportId, LeftOrRight leftOrRight,
        EntranceOrExit entranceOrExit, Transform transform)
    {
        if (IsVendingMachineInDict(id)) return;
        
        VendingMachinePlacement newVendingMachinePlacement = new()
        {
            TeleportId = teleportId,
            LeftOrRight = leftOrRight,
            EntranceOrExit = entranceOrExit,
            Transform = transform,
        };
        
        _vendingMachines[id] = newVendingMachinePlacement;
    }

    internal void RemoveVendingMachine(string id)
    {
        if (IsVendingMachineInDict(id)) _vendingMachines.Remove(id);
    }

    internal bool IsDoorAndSideOccupied(int teleportId, LeftOrRight leftOrRight, EntranceOrExit entranceOrExit)
    {
        return _vendingMachines.Any(vendingMachineKeyValuePair =>
            vendingMachineKeyValuePair.Value.TeleportId == teleportId
            && vendingMachineKeyValuePair.Value.EntranceOrExit == entranceOrExit
            && vendingMachineKeyValuePair.Value.LeftOrRight == leftOrRight);
    }

    internal bool IsDoorOccupied(int teleportId, EntranceOrExit entranceOrExit)
    {
        return _vendingMachines.Any(vendingMachineKeyValuePair =>
            vendingMachineKeyValuePair.Value.TeleportId == teleportId
            && vendingMachineKeyValuePair.Value.EntranceOrExit == entranceOrExit);
    }

    private bool IsVendingMachineInDict(string id)
    {
        if (id != null) return _vendingMachines.ContainsKey(id);
        Mls.LogWarning("Given string id was null, this should not happen.");
        return false;
    }

    public Dictionary<string, VendingMachinePlacement> GetVendingMachineRegistry()
    {
        return _vendingMachines;
    }

    public string GetVendingMachineRegistryPrint()
    {
        return _vendingMachines.Aggregate("",
            (current, keyValuePair) =>
                current +
                $"Vending Machine Id: {keyValuePair.Key}, TeleportId: {keyValuePair.Value.TeleportId} EntranceOrExit: {keyValuePair.Value.EntranceOrExit}, LeftOrRight: {keyValuePair.Value.LeftOrRight}\n");
    }
}