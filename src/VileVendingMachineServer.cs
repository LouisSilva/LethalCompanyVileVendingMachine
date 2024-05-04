using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BepInEx.Logging;
using Unity.Netcode;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;
using Random = UnityEngine.Random;
using Vector3 = UnityEngine.Vector3;

namespace LethalCompanyVileVendingMachine;

[SuppressMessage("ReSharper", "RedundantDefaultMemberInitializer")]
public class VileVendingMachineServer : EnemyAI
{
    private static readonly Dictionary<string, List<Tuple<int, LeftOrRight, Vector3, Quaternion>>> StoredViablePlacements = new()
    {
        { "Level1Experimentation", [new Tuple<int, LeftOrRight, Vector3, Quaternion>(0, LeftOrRight.Left, new Vector3(-113.437f, 2.926f, -14.116f), Quaternion.Euler(0, 90, 0)), 
            new Tuple<int, LeftOrRight, Vector3, Quaternion>(0, LeftOrRight.Right, new Vector3(-113.437f, 2.926f, -23.18f), Quaternion.Euler(0, 90, 0)),
            new Tuple<int, LeftOrRight, Vector3, Quaternion>(1, LeftOrRight.Left, new Vector3(-133.845f, 25.4f, -52.101f), Quaternion.Euler(0, 90, 0)),
            new Tuple<int, LeftOrRight, Vector3, Quaternion>(1, LeftOrRight.Right, Vector3.zero, Quaternion.identity)]}, 
        
        { "Level2Assurance", [new Tuple<int, LeftOrRight, Vector3, Quaternion>(0, LeftOrRight.Left, new Vector3(134.802f, 6.470059f, 70.641f), Quaternion.Euler(0, -90, 0)), 
            new Tuple<int, LeftOrRight, Vector3, Quaternion>(0, LeftOrRight.Right, new Vector3(127.49f, 6.470059f, 85.131f), Quaternion.Euler(0, -180, 0)),
            new Tuple<int, LeftOrRight, Vector3, Quaternion>(1, LeftOrRight.Left, new Vector3(110.139f, 15.244f, -70.461f), Quaternion.Euler(0, -45, 0)),
            new Tuple<int, LeftOrRight, Vector3, Quaternion>(1, LeftOrRight.Right, new Vector3(114.87f, 15.244f, -65.73f), Quaternion.Euler(0, -45, 0))]},
        
        { "Level3Vow", [new Tuple<int, LeftOrRight, Vector3, Quaternion>(0, LeftOrRight.Left, new Vector3(-26.221f, -1.181f, 150.428f), Quaternion.Euler(0, -180, 0)), 
            new Tuple<int, LeftOrRight, Vector3, Quaternion>(0, LeftOrRight.Right, new Vector3(-36.91f, -1.181f, 150.428f), Quaternion.Euler(0, -180, 0)),
            new Tuple<int, LeftOrRight, Vector3, Quaternion>(1, LeftOrRight.Left, new Vector3(-69.86f, -23.48f, 122.2f), Quaternion.Euler(0, 90, 0)),
            new Tuple<int, LeftOrRight, Vector3, Quaternion>(1, LeftOrRight.Right, new Vector3(-69.86f, -23.48f, 109.61f), Quaternion.Euler(0, 90, 0))]},
        
        { "Level8Titan", [new Tuple<int, LeftOrRight, Vector3, Quaternion>(0, LeftOrRight.Left, new Vector3(-33.96f, 47.67f, 10.8f), Quaternion.Euler(0, 125, 0)), 
            new Tuple<int, LeftOrRight, Vector3, Quaternion>(0, LeftOrRight.Right, new Vector3(-37.73f, 47.67f, 5.42f), Quaternion.Euler(0, 125, 0)),
            new Tuple<int, LeftOrRight, Vector3, Quaternion>(1, LeftOrRight.Left, new Vector3(-41.011f, 47.738f, 4.101f), Quaternion.Euler(0, 90, 0)),
            new Tuple<int, LeftOrRight, Vector3, Quaternion>(1, LeftOrRight.Right, Vector3.zero, Quaternion.identity)]},
        
        { "Level10Adamance", [new Tuple<int, LeftOrRight, Vector3, Quaternion>(0, LeftOrRight.Left, new Vector3(-121.2046f, 1.8107f, 0.4547f), Quaternion.Euler(0, 90, 0))]},
    };

    private enum ColaTypes
    {
        Normal,
        Crushed,
    }
    
    private ManualLogSource _mls;
    private string _vendingMachineId;

    [Header("AI")]
    [Space(5f)]
    [SerializeField] private float initialKillProbability = 0.01f;
    [SerializeField] private float killProbabilityGrowthFactor = 4.64f;
    [SerializeField] private float killProbabilityReductionFactor = 0.25f;
    private float _currentKillProbability = 0.01f;

    [Header("Colliders and Transforms")]
    [Space(5f)]
#pragma warning disable 0649
    [SerializeField] private BoxCollider centreCollider;
    [SerializeField] private BoxCollider frontCollider;
    [SerializeField] private BoxCollider backCollider;
    [SerializeField] private BoxCollider floorCollider;
    [SerializeField] private Transform colaPlaceholder;
    [SerializeField] private Transform itemHolder;
    
    [Header("Controllers")] [Space(5f)]
    [SerializeField] private VileVendingMachineNetcodeController netcodeController;
#pragma warning restore 0649

    public override void OnDestroy()
    {
        netcodeController.OnDespawnHeldItem -= HandleDespawnHeldItem;
        netcodeController.OnStartAcceptItemAnimation -= HandleStartAcceptItemAnimation;
        netcodeController.OnChangeTargetPlayer -= HandleChangeTargetPlayer;
        
        VendingMachineRegistry.RemoveVendingMachine(_vendingMachineId);
    }

    public override void Start()
    {
        base.Start();
        if (!IsServer) return;

        _vendingMachineId = Guid.NewGuid().ToString();
        _mls = Logger.CreateLogSource(
            $"{VileVendingMachinePlugin.ModGuid} | Vile Vending Machine Server {_vendingMachineId}");
        
        Random.InitState(StartOfRound.Instance.randomMapSeed + _vendingMachineId.GetHashCode());

        netcodeController.SyncVendingMachineIdClientRpc(_vendingMachineId);
        netcodeController.OnDespawnHeldItem += HandleDespawnHeldItem;
        netcodeController.OnStartAcceptItemAnimation += HandleStartAcceptItemAnimation;
        netcodeController.OnChangeTargetPlayer += HandleChangeTargetPlayer;

        initialKillProbability = VileVendingMachineConfig.Instance.InitialKillProbability.Value;
        killProbabilityGrowthFactor = VileVendingMachineConfig.Instance.KillProbabilityGrowthFactor.Value;
        killProbabilityReductionFactor = VileVendingMachineConfig.Instance.KillProbabilityReductionFactor.Value;

        agent.updateRotation = false;
        agent.updatePosition = false;
        
        #if !DEBUG
        netcodeController.SetMeshEnabledClientRpc(_vendingMachineId, false);
        EnableEnemyMesh(false);
        #endif

        try
        {
            if (IsServer)
                StartCoroutine(PlaceVendingMachine(() =>
                {
                    netcodeController.ChangeAnimationParameterBoolClientRpc(_vendingMachineId,
                        VileVendingMachineClient.ArmAccept, true);
                }));
        }
        catch (Exception)
        {
            VendingMachineRegistry.IsPlacementInProgress = false;
            throw;
        }
    }

    private IEnumerator PlaceVendingMachine(Action callback = null)
    {
        int checksDone = 0;
        while (VendingMachineRegistry.IsPlacementInProgress && checksDone < 20)
        {
            checksDone++;
            LogDebug($"is placement in progress?: {VendingMachineRegistry.IsPlacementInProgress}, vending machines in registry: {VendingMachineRegistry.GetVendingMachineRegistryPrint()}");
            yield return new WaitForSeconds(1);
        }
        
        VendingMachineRegistry.IsPlacementInProgress = true;
        bool isFireExitAllowedOutside = IsFireExitAllowedOutside();
        bool isFireExitAllowedInside = IsFireExitAllowedInside();
        
        EntranceTeleport[] doors = GetDoorTeleports();
        
        // Shuffle the doors
        if (!VileVendingMachineConfig.Instance.AlwaysSpawnOutsideMainEntrance.Value)
            doors = doors.OrderBy(x => Random.Range(int.MinValue, int.MaxValue)).ToArray();
        
        // Loops over all the doors on the outside, and inside the dungeon. The i values correspond to the EntranceOrExit enum
        for (int i = 0; i < 2; i++)
        {
            // Checks if a vending machine can spawn outside
            if (!VileVendingMachineConfig.Instance.CanSpawnOutsideMaster.Value && i == (int)EntranceOrExit.Entrance)
                continue;

            // Checks if a vending machine can spawn inside the dungeon
            if (!VileVendingMachineConfig.Instance.CanSpawnInsideMaster.Value && i == (int)EntranceOrExit.Exit)
                continue;
            
            foreach (EntranceTeleport door in doors)
            {
                // Checks if a vending machine can spawn at the main door, inside or outside
                if (!VileVendingMachineConfig.Instance.CanSpawnAtMainDoorMaster.Value && door.entranceId == 0)
                    continue;

                // Checks if a vending machine can spawn at a fire exit, inside or outside
                if (!VileVendingMachineConfig.Instance.CanSpawnAtFireExitMaster.Value && door.entranceId != 0)
                    continue;

                // Checks if a vending machine is blacklisted from spawning outside a fire exit on a certain map (to save time)
                if (door.entranceId != 0 && (int)EntranceOrExit.Entrance == i && !isFireExitAllowedOutside)
                    continue;
                
                // Checks if a vending machine is blacklisted from spawning inside a particular dungeon flow e.g. facility because there is no space 99% of the time
                if (door.entranceId != 0 && (int)EntranceOrExit.Exit == i && !isFireExitAllowedInside)
                    continue;

                if (VendingMachineRegistry.IsDoorOccupied(door.entranceId, (EntranceOrExit)i))
                {
                    continue;
                }

                // See if there is a cached placement for this map
                #if !DEBUG
                if (StoredViablePlacements.TryGetValue(StartOfRound.Instance.currentLevel.sceneName, out List<Tuple<int, LeftOrRight, Vector3, Quaternion>> placement))
                {
                    foreach (Tuple<int, LeftOrRight, Vector3, Quaternion> placementTuple in placement.Where(
                                 placementTuple => 
                                     placementTuple.Item1 == door.entranceId && 
                                     !VendingMachineRegistry.IsDoorAndSideOccupied(placementTuple.Item1, placementTuple.Item2, EntranceOrExit.Entrance) &&
                                     placementTuple.Item3 != Vector3.zero &&
                                     placementTuple.Item4 != Quaternion.identity))
                    {
                        LogDebug("Found cached vending machine placement");
                        transform.position = placementTuple.Item3;
                        transform.rotation = placementTuple.Item4;
                        StartCoroutine(PlacementSuccess(placementTuple.Item1, placementTuple.Item2, (EntranceOrExit)i, callback));
                        yield break;
                    }
                }
                #endif
                // If the using the cache stuff didn't work, then try spawning it normally
                
                // Gets the transform of the door. It takes into account whether the current door is an entrance or exit
                Vector3 doorPosition = default;
                IEnumerable<Tuple<Transform, int>> doorTransforms = GetDoorTransforms(door.entranceId);
                foreach (Tuple<Transform, int> doorTransformTuple in doorTransforms)
                {
                    yield return null;
                    if (doorTransformTuple.Item2 != i) continue;
                    doorPosition = doorTransformTuple.Item1.position;
                    break;
                }

                Vector3 teleportPosition = i == (int)EntranceOrExit.Entrance
                    ? door.entrancePoint.position
                    : door.exitPoint.position;
                
                Vector3 vectorA = teleportPosition - doorPosition;
                Vector3 vectorB = Vector3.Cross(vectorA, Vector3.up).normalized;

                vectorA.y = 0;
                transform.position = doorPosition + vectorA.normalized * -0.1f;
                transform.rotation = Quaternion.LookRotation(vectorA);

                // Move vending machine forward until its back is still touching the wall, but its main body is not touching the wall
                bool stageOneSuccess = false;
                for (int k=0; k < 300; k++)
                {
                    #if DEBUG
                    yield return new WaitForSeconds(0.2f);
                    #else
                    yield return null;
                    #endif
                    
                    #if DEBUG
                    LogDebug($"counter: {k}, backCol: {IsColliderColliding(backCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault)}, mainCol: {IsColliderColliding(centreCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault)}, frontCol: {IsColliderColliding(frontCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault)}");
                    #endif

                    transform.position += vectorA.normalized * 0.02f;
                    if (!IsColliderColliding(backCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault) ||
                        IsColliderColliding(frontCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault) ||
                        IsColliderColliding(centreCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                        continue;
                    
                    stageOneSuccess = true;
                    break;
                }
                
                if (!stageOneSuccess) PlacementFail();
                
                // Randomly pick either left or right
                int startingDirection = Random.Range(0, 2) == 0 ? 1 : -1;
                Vector3 startingPosition = transform.position;

                // Iterate over both directions, to see if the vending machine can be placed (to the left or right)
                for (int j=0; j < 2; j++) 
                {
                    LogDebug($"Iteration of left or right placement: {j+1} out of 2");
                    
                    // First make sure to reset the vending machine's position to in front of the door, just in case this is the second iteration
                    transform.position = startingPosition;
                    
                    // Second, move the vending machine out of the way of the door
                    const float initialDistanceFromDoor = 2.5f;
                    int currentDirection = j == 0 ? startingDirection : startingDirection * -1;
                    transform.position += vectorB * (currentDirection * initialDistanceFromDoor);
                    LogDebug($"Moved vending machine {initialDistanceFromDoor} units from door for left/right placement");
                    yield return null;
                    
                    // Now check if the vending machine is still in a valid position
                    // If it is, then try to move the vending machine a random distance away from the door, if there is space
                    // If it's not, then try the other side of the door (if both sides have been tried then abort the vending machine placement)
                    if (IsColliderColliding(backCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault) &&
                        !IsColliderColliding(frontCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault) &&
                        !IsColliderColliding(centreCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                    {
                        LogDebug("After initial movement, the placement is still valid");
                        
                        #if DEBUG
                        LogDebug($"backCol: {IsColliderColliding(backCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault)}, mainCol: { IsColliderColliding(centreCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault)}, frontCol: {IsColliderColliding(frontCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault)}");
                        #endif
                        
                        // Pick a random distance from the door to try to reach
                        float randomDistanceFromDoor = Random.Range(0.01f, 6f);
                        float currentDistanceFromDoor = 0;
                        Vector3 previousPosition = transform.position;
                        LogDebug($"Random distance picked: {randomDistanceFromDoor} units");
                        
                        // Start moving towards the target distance
                        while (currentDistanceFromDoor < randomDistanceFromDoor)
                        {
                            // Don't hog the thread
                            #if DEBUG
                            yield return new WaitForSeconds(0.3f);
                            #else
                            yield return null;
                            #endif
                            
                            // If the new position makes the vending machine spawn invalid, move back to the last valid position and stop moving
                            if (!IsColliderColliding(backCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault) ||
                                IsColliderColliding(frontCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault) ||
                                IsColliderColliding(centreCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                            {
                                LogDebug($"At distance from door {currentDistanceFromDoor} units, the placement has been made no longer valid. Moving to old position");
                                transform.position = previousPosition;
                                break;
                            }

                            LogDebug($"At distance from door {currentDistanceFromDoor} units, the placement is still valid. Moving again.");
                            
                            // Move the vending machine further towards the target distance from the door
                            previousPosition = transform.position;
                            const float distanceIncrease = 0.05f;
                            transform.position += vectorB * (currentDirection * distanceIncrease);
                            currentDistanceFromDoor += distanceIncrease;
                        }
                        
                        // Move vending machine to the floor
                        Vector3 floorRayStart = transform.position + Vector3.up * 0.5f;
                        if (Physics.Raycast(floorRayStart, -Vector3.up, out RaycastHit hit, 8f,
                                StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                        {
                            transform.position = new Vector3(transform.position.x,
                                hit.point.y + floorCollider.transform.localPosition.y, transform.position.z);
                        }
                        
                        // Placement was a success, finalize the details
                        LeftOrRight finalDirectionFromDoor = currentDirection == 1 ? LeftOrRight.Left : LeftOrRight.Right;
                        LogDebug($"Placement was a success, with position: {transform.position} and being in the {finalDirectionFromDoor} direction from the door.");
                        StartCoroutine(PlacementSuccess(door.entranceId, finalDirectionFromDoor, (EntranceOrExit)i, callback));
                        yield break;
                    }
                    
                    // Current iteration failed
                    #if DEBUG
                    LogDebug($"Current iteration: {j+1} failed");
                    LogDebug($"backCol: {IsColliderColliding(backCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault)}, mainCol: { IsColliderColliding(centreCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault)}, frontCol: {IsColliderColliding(frontCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault)}");
                    #endif
                    
                    #if DEBUG
                    yield return new WaitForSeconds(1f);
                    #else
                    yield return null;
                    #endif
                    
                    if (j != 1) continue;
                    
                    // If both directions have been tried, abort placement
                    LogDebug("Both the left and right side has been tried with no success, aborting placement.");
                    PlacementFail();
                    yield break;
                }
            }
        }

        PlacementFail();
    }

    private IEnumerator PlacementSuccess(int teleportId, LeftOrRight leftOrRight, EntranceOrExit entranceOrExit, Action callback = null)
    {
        LogDebug($"Vending machine was placed successfully at teleportId: {teleportId}");

        VendingMachineRegistry.AddVendingMachine(_vendingMachineId, teleportId, leftOrRight, entranceOrExit, transform);
        VendingMachineRegistry.IsPlacementInProgress = false;
        netcodeController.PlayMaterializeVfxClientRpc(_vendingMachineId, transform.position, transform.rotation);
        yield return new WaitForSeconds(5f);
        
        // Needed try block because of the imperium mod bug
        try
        {
            EnableEnemyMesh(true);
        }
        catch (NullReferenceException)
        {
           LogDebug("The EnableEnemyMesh function failed");
        }
        
        agent.enabled = false;
                
        callback?.Invoke();
    }
    
    private static EntranceTeleport[] GetDoorTeleports()
    {
        return FindObjectsOfType<EntranceTeleport>().Where(t => t != null && t.exitPoint != null && t.isEntranceToBuilding).ToArray();
    }

    private static IEnumerable<Tuple<Transform, int>> GetDoorTransforms(int entranceId)
    {
        EntranceTeleport[] allTeleports = FindObjectsOfType<EntranceTeleport>().ToArray();
        List<EntranceTeleport> matchedTeleports = allTeleports.Where(t => t.entranceId == entranceId).ToList();
        
        EntranceTeleport entrance = matchedTeleports.FirstOrDefault(t => t.isEntranceToBuilding);
        EntranceTeleport exit = matchedTeleports.FirstOrDefault(t => t.isEntranceToBuilding == false);

        Transform entranceTransform = entrance == null ? default : entrance.transform;
        Transform exitTransform = exit == null ? default : exit.transform;
        
        Tuple<Transform, int> entranceTuple = new(entranceTransform, (int)EntranceOrExit.Entrance);
        Tuple<Transform, int> exitTuple = new(exitTransform, (int)EntranceOrExit.Exit);

        return [entranceTuple, exitTuple];
    }

    private void SpawnCola(ColaTypes colaType = ColaTypes.Normal)
    {
        if (!IsServer) return;

        GameObject colaSpawnPrefab;
        int colaScrapValue;
        switch (colaType)
        {
            case ColaTypes.Normal:
            {
                colaSpawnPrefab = VileVendingMachinePlugin.CompanyColaItem.spawnPrefab;
                colaScrapValue = Random.Range(
                    VileVendingMachineConfig.Instance.ColaMinValue.Value, 
                    VileVendingMachineConfig.Instance.ColaMaxValue.Value + 1);
                break;
            }

            case ColaTypes.Crushed:
            {
                colaSpawnPrefab = VileVendingMachinePlugin.CrushedCompanyColaItem.spawnPrefab;
                colaScrapValue = Random.Range(
                    VileVendingMachineConfig.Instance.CrushedColaMinValue.Value, 
                    VileVendingMachineConfig.Instance.CrushedColaMaxValue.Value + 1);
                break;
            }

            default:
            {
                _mls.LogError("The given cola type is not valid. This should not happen.");
                return; 
            }
        }

        GameObject colaObject = Instantiate(
            colaSpawnPrefab,
            colaPlaceholder.position,
            colaPlaceholder.rotation,
            colaPlaceholder);
        
        CompanyColaBehaviour colaBehaviour = colaObject.GetComponent<CompanyColaBehaviour>();
        if (colaBehaviour == null)
        {
            _mls.LogError("colaBehaviour is null. This should not happen.");
            return;
        }
        
        colaBehaviour.isPartOfVendingMachine = true;
        colaBehaviour.grabbableToEnemies = false;
        colaBehaviour.fallTime = 1f;
        
        
        colaBehaviour.SetScrapValue(colaScrapValue);
        colaBehaviour.UpdateScrapValue(colaScrapValue);
        RoundManager.Instance.totalScrapValueInLevel += colaScrapValue;

        NetworkObject colaNetworkObject = colaObject.GetComponent<NetworkObject>();
        colaNetworkObject.Spawn();
        
        netcodeController.UpdateColaNetworkObjectReferenceClientRpc(_vendingMachineId, colaNetworkObject, colaScrapValue);
    }

    private void HandleDespawnHeldItem(string receivedVendingMachineId)
    {
        if (!IsServer) return;
        if (receivedVendingMachineId != _vendingMachineId) return;
        
        NetworkObject[] grabbableObjects = itemHolder.GetComponentsInChildren<NetworkObject>();
        if (grabbableObjects.Length <= 0)
        {
            _mls.LogWarning("ItemOnHand is null, unable to despawn object");
            return;
        }

        foreach (NetworkObject grabbableObject in grabbableObjects)
        {
            grabbableObject.Despawn();
        }

        netcodeController.SetIsItemOnHandClientRpc(_vendingMachineId, false);
    }

    private IEnumerator AcceptItem()
    {
        if (!IsServer) yield break;
        yield return new WaitForSeconds(0.1f);
        netcodeController.DoAnimationClientRpc(_vendingMachineId, VileVendingMachineClient.ArmRetract);

        // Get the scan node properties
        int heldItemValue = 20;
        string heldItemName = "";
        ScanNodeProperties heldItemProperties = itemHolder.GetComponentInChildren<ScanNodeProperties>();
        if (heldItemProperties != null)
        {
            heldItemValue = heldItemProperties.scrapValue;
            heldItemName = heldItemProperties.headerText;
        }
        LogDebug(heldItemName);
        
        yield return new WaitForSeconds(3f);
        
        // Set the kill probability
        if (heldItemName == "Crushed Company Cola") _currentKillProbability = 1;
        else if (heldItemValue > 90) _currentKillProbability = Mathf.Max(initialKillProbability, _currentKillProbability * killProbabilityReductionFactor);
        else _currentKillProbability = Mathf.Min(_currentKillProbability * killProbabilityGrowthFactor, 1f);

        ColaTypes colaTypeToSpawn;
        if (Random.value < _currentKillProbability) // kill player
        {
            _currentKillProbability = initialKillProbability;
            netcodeController.DoAnimationClientRpc(_vendingMachineId, VileVendingMachineClient.ArmGrab);

            // kill player with animation event
            yield return new WaitForSeconds(2.5f);
            netcodeController.DoAnimationClientRpc(_vendingMachineId, VileVendingMachineClient.Grind);
            yield return new WaitForSeconds(4);
            colaTypeToSpawn = ColaTypes.Crushed;
        }
        else colaTypeToSpawn = ColaTypes.Normal;
        
        SpawnCola(colaTypeToSpawn);
        
        yield return new WaitForSeconds(0.5f);
        netcodeController.DoAnimationClientRpc(_vendingMachineId, VileVendingMachineClient.Dispense);
        yield return new WaitForSeconds(2);
        
        netcodeController.DoAnimationClientRpc(_vendingMachineId, VileVendingMachineClient.ArmAccept);
        netcodeController.SetIsItemOnHandClientRpc(_vendingMachineId, false);
    }
    
    private static bool IsColliderAgainstWall(Collider collider)
    {
        return Physics.Raycast(collider.bounds.center, -collider.transform.forward, out RaycastHit hit, collider.bounds.size.z);
    }
    
    private bool IsColliderColliding(Collider collider, LayerMask layerMask)
    {
        #if DEBUG
        Collider[] collidingObjects = Physics.OverlapBox(collider.bounds.center, collider.bounds.extents, collider.transform.rotation, layerMask, QueryTriggerInteraction.Ignore);
        foreach (Collider obj in collidingObjects) {
            LogDebug("Colliding with: " + obj.name);
        }
        return collidingObjects.Length > 0;
    
        #else
        return Physics.CheckBox(collider.bounds.center, collider.bounds.extents, collider.transform.rotation, layerMask, QueryTriggerInteraction.Ignore);
        
        #endif
    }

    private bool IsFireExitAllowedOutside()
    {
        string[] allowedFireExitDoors =
        [
            ""
        ];

        LogDebug($"Fire exit allowed outside: {allowedFireExitDoors.All(mapName => StartOfRound.Instance.currentLevel.sceneName == mapName)}");
        return allowedFireExitDoors.All(mapName => StartOfRound.Instance.currentLevel.sceneName == mapName);
    }

    private bool IsFireExitAllowedInside()
    {
        string[] allowedFireExitDoors =
        [
            ""
        ];

        LogDebug($"Fire exit allowed inside: {allowedFireExitDoors.All(dungeonName =>
            RoundManager.Instance.dungeonGenerator.Generator.DungeonFlow.name == dungeonName)}");
        return allowedFireExitDoors.All(dungeonName =>
            RoundManager.Instance.dungeonGenerator.Generator.DungeonFlow.name == dungeonName);
    }
    
    private void DrawDebugHorizontalLineAtTransform(Transform transform, Color colour = default)
    {
        GameObject lineObj = new("ForwardLine");
        lineObj.transform.position = transform.position;
        
        LineRenderer lineRenderer = lineObj.AddComponent<LineRenderer>();
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.widthMultiplier = 0.1f;
        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;
        lineRenderer.startColor = colour;
        lineRenderer.endColor = colour;
        
        lineRenderer.SetPosition(0, transform.position - transform.right * 5);
        lineRenderer.SetPosition(1, transform.position + transform.right * 5);
    }
    
    private void DrawDebugLineAtTransformWithDirection(Transform transform, Vector3 direction, Color colour = default)
    {
        GameObject lineObj = new("ForwardLine");
        lineObj.transform.position = transform.position;
        
        LogDebug("Drawling line");
        LineRenderer lineRenderer = lineObj.AddComponent<LineRenderer>();
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.widthMultiplier = 0.1f;
        lineRenderer.positionCount = 4;
        lineRenderer.useWorldSpace = true;
        lineRenderer.startColor = colour;
        lineRenderer.endColor = colour;
        
        Vector3 lineEnd = transform.position + direction * 8;
        Vector3 arrowDirection = (lineEnd - transform.position).normalized;
        const float arrowHeadLength = 1f; // Adjust as needed
        const float arrowHeadAngle = 25.0f; // Adjust as needed

        Vector3 left = Quaternion.LookRotation(arrowDirection) * Quaternion.Euler(0, 180 + arrowHeadAngle, 0) * new Vector3(0, 0, 1);
        Vector3 right = Quaternion.LookRotation(arrowDirection) * Quaternion.Euler(0, 180 - arrowHeadAngle, 0) * new Vector3(0, 0, 1);
        
        lineRenderer.SetPosition(0, transform.position);
        lineRenderer.SetPosition(1, lineEnd);
        lineRenderer.SetPosition(2, lineEnd + left * arrowHeadLength);
        lineRenderer.SetPosition(3, lineEnd + right * arrowHeadLength);
    }
    
    private void DrawDebugCircleAtPosition(Vector3 position, Color color = default)
    {
        float angle = 20f;
        const float circleRadius = 2f;
        GameObject circleObj = new("Circle");
        circleObj.transform.position = position;

        LineRenderer lineRenderer = circleObj.AddComponent<LineRenderer>();
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.widthMultiplier = 0.1f;
        lineRenderer.positionCount = 51;
        lineRenderer.useWorldSpace = true;
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        
        
        for (int i = 0; i <= 50; i++)
        {
            float x = position.x + Mathf.Sin(Mathf.Deg2Rad * angle) * circleRadius;
            float z = position.z + Mathf.Cos(Mathf.Deg2Rad * angle) * circleRadius;
            float y = position.y;
            
            lineRenderer.SetPosition(i, new Vector3(x, y, z));
            angle += 360f / 50;
        }
    }
    
    private EntranceTeleport GetRandomDoor()
    {
        EntranceTeleport[] doors = FindObjectsOfType<EntranceTeleport>().Where(t => t != null && t.exitPoint != null).ToArray();
        // foreach (EntranceTeleport door in doors)
        // {
        //     DrawDebugHorizontalLineAtTransform(door.transform, Color.green);
        //     DrawDebugHorizontalLineAtTransform(door.entrancePoint, Color.red);
        //     DrawDebugLineAtTransformForward(door.transform, Color.blue);
        // }
        
        return doors[UnityEngine.Random.Range(0, doors.Length - 1)];
    }

    private void PlacementFail()
    {
        LogDebug("Vending machine could not be placed");
        VendingMachineRegistry.IsPlacementInProgress = false;
        KillEnemyClientRpc(true);
    }

    private void HandleStartAcceptItemAnimation(string receivedVendingMachineId)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;
        StartCoroutine(AcceptItem());
    }

    private void HandleChangeTargetPlayer(string receivedVendingMachineId, ulong playerClientId)
    {
        if (_vendingMachineId != receivedVendingMachineId) return;
        targetPlayer = playerClientId == 69420 ? null : StartOfRound.Instance.allPlayerScripts[playerClientId];
    }
    
    private void LogDebug(string msg)
    {
        #if DEBUG
        if (!IsServer) return;
        _mls?.LogInfo(msg);
        #endif
    }
}