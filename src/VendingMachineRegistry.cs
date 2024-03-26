using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalCompanyVileVendingMachine;

public enum LeftOrRight
{
    Left = 0,
    Right = 1
}

public enum EntranceOrExit
{
    Entrance = 0,
    Exit = 1
}

public static class VendingMachineRegistry
{
    public struct VendingMachinePlacement
    {
        public VileVendingMachineServer ServerScript;
        public EntranceTeleport Teleport;
        public Vector3 Position;
        public int LeftOrRight;
        public int EntranceOrExit;
    }

    private static readonly Dictionary<string, VendingMachinePlacement> _vendingMachines = new();
    public static bool IsPlacementInProgress;

    public static void AddVendingMachine(string id, EntranceTeleport teleport, int leftOrRight, int entranceOrExit)
    {
        if (teleport == null) throw new EntranceTeleportIsNull();
        if (IsVendingMachineInDict(id)) return;

        VendingMachinePlacement newVendingMachinePlacement = new()
        {
            Teleport = teleport,
            LeftOrRight = leftOrRight,
            EntranceOrExit = entranceOrExit,
        };

        _vendingMachines[id] = newVendingMachinePlacement;
    }

    public static void RemoveVendingMachine(string id)
    {
        if (IsVendingMachineInDict(id)) _vendingMachines.Remove(id);
    }

    public static bool IsDoorAndSideOccupied(EntranceTeleport teleport, int leftOrRight, int entranceOrExit)
    {
        if (teleport == null) throw new EntranceTeleportIsNull();
        return _vendingMachines.Any(vendingMachineKeyValuePair => vendingMachineKeyValuePair.Value.Teleport == teleport 
                                                                  && vendingMachineKeyValuePair.Value.EntranceOrExit == entranceOrExit 
                                                                  && vendingMachineKeyValuePair.Value.LeftOrRight == leftOrRight);
    }

    public static bool IsDoorOccupied(EntranceTeleport teleport, int entranceOrExit)
    {
        if (teleport == null) throw new EntranceTeleportIsNull();
        return _vendingMachines.Any(vendingMachineKeyValuePair => vendingMachineKeyValuePair.Value.Teleport.entranceId == teleport.entranceId
                                                                  && vendingMachineKeyValuePair.Value.EntranceOrExit == entranceOrExit);
    }

    public static bool IsVendingMachineInDict(string id)
    {
        return _vendingMachines.ContainsKey(id);
    }

    public static Dictionary<string, VendingMachinePlacement> GetVendingMachineRegistry()
    {
        return _vendingMachines;
    }

    public static string GetVendingMachineRegistryPrint()
    {
        return _vendingMachines.Aggregate("", (current, keyValuePair) => current + $"Vending Machine Id: {keyValuePair.Key}, EntranceOrExit: {keyValuePair.Value.EntranceOrExit}, LeftOrRight: {keyValuePair.Value.LeftOrRight}\n");
    }
}

public class EntranceTeleportIsNull : Exception
{
    public EntranceTeleportIsNull()
    {
        
    }

    public EntranceTeleportIsNull(string msg) : base(msg)
    {
        
    }

    public EntranceTeleportIsNull(string msg, Exception inner) : base(msg, inner)
    {
        
    }
}