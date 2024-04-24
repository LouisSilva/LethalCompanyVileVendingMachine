using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalCompanyVileVendingMachine;

public enum LeftOrRight
{
    Left = 1,
    Right = 2,
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
        public int TeleportId;
        public Transform Transform;
        public LeftOrRight LeftOrRight;
        public EntranceOrExit EntranceOrExit;
    }

    private static readonly Dictionary<string, VendingMachinePlacement> VendingMachines = new();
    public static bool IsPlacementInProgress;

    public static void AddVendingMachine(string id, int teleportId, LeftOrRight leftOrRight, EntranceOrExit entranceOrExit, Transform transform)
    {
        if (IsVendingMachineInDict(id)) return;

        VendingMachinePlacement newVendingMachinePlacement = new()
        {
            TeleportId = teleportId,
            LeftOrRight = leftOrRight,
            EntranceOrExit = entranceOrExit,
            Transform = transform,
        };

        VendingMachines[id] = newVendingMachinePlacement;
    }

    public static void RemoveVendingMachine(string id)
    {
        if (IsVendingMachineInDict(id)) VendingMachines.Remove(id);
    }

    public static bool IsDoorAndSideOccupied(int teleportId, LeftOrRight leftOrRight, EntranceOrExit entranceOrExit)
    {
        return VendingMachines.Any(vendingMachineKeyValuePair => vendingMachineKeyValuePair.Value.TeleportId == teleportId
                                                                  && vendingMachineKeyValuePair.Value.EntranceOrExit == entranceOrExit 
                                                                  && vendingMachineKeyValuePair.Value.LeftOrRight == leftOrRight);
    }

    public static bool IsDoorOccupied(int teleportId, EntranceOrExit entranceOrExit)
    {
        return VendingMachines.Any(vendingMachineKeyValuePair => vendingMachineKeyValuePair.Value.TeleportId == teleportId
                                                                  && vendingMachineKeyValuePair.Value.EntranceOrExit == entranceOrExit);
    }

    public static bool IsVendingMachineInDict(string id)
    {
        return VendingMachines.ContainsKey(id);
    }

    public static Dictionary<string, VendingMachinePlacement> GetVendingMachineRegistry()
    {
        return VendingMachines;
    }

    public static string GetVendingMachineRegistryPrint()
    {
        return VendingMachines.Aggregate("", (current, keyValuePair) => current + $"Vending Machine Id: {keyValuePair.Key}, TeleportId: {keyValuePair.Value.TeleportId} EntranceOrExit: {keyValuePair.Value.EntranceOrExit}, LeftOrRight: {keyValuePair.Value.LeftOrRight}\n");
    }
}