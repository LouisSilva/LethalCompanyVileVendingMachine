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

    /// <summary>
    /// Types of items that the vending machine can dispense
    /// </summary>
    private enum ColaTypes
    {
        Normal,
        Crushed,
    }
    
    private ManualLogSource _mls;
    private string _vendingMachineId;

    [Header("AI")] [Space(5f)]
    [SerializeField] private float initialKillProbability = 0.01f;
    [SerializeField] private float killProbabilityGrowthFactor = 4.64f;
    [SerializeField] private float killProbabilityReductionFactor = 0.25f;
    [SerializeField] private int expensiveItemValue = 90;
    [SerializeField] private int companyColaMinValue = 30;
    [SerializeField] private int companyColaMaxValue = 90;
    [SerializeField] private int crushedColaMinValue = 1;
    [SerializeField] private int crushedColaMaxValue = 5;
    private float _currentKillProbability = 0.01f;
    
#pragma warning disable 0649
    [Header("Colliders and Transforms")] [Space(5f)]
    [SerializeField] private BoxCollider centreCollider;
    [SerializeField] private BoxCollider relaxedCentreCollider;
    [SerializeField] private BoxCollider frontCollider;
    [SerializeField] private BoxCollider backCollider;
    [SerializeField] private BoxCollider floorCollider;
    [SerializeField] private Transform colaPlaceholder;
    [SerializeField] private Transform itemHolder;
    
    [Header("Controllers")] [Space(5f)]
    [SerializeField] private VileVendingMachineNetcodeController netcodeController;
#pragma warning restore 0649
    
    private bool _networkEventsSubscribed;
    
    /// <summary>
    /// Subscribes to the needed network events when the vending machine is enabled.
    /// </summary>
    private void OnEnable()
    {
        SubscribeToNetworkEvents();
    }

    /// <summary>
    /// Unsubscribes from the needed network events when the vending machine is disabled.
    /// </summary>
    private void OnDisable()
    {
        UnsubscribeFromNetworkEvents();
    }
    
    /// <summary>
    /// Removes itself from the vending machine registry when the gameobject is destroyed.
    /// </summary>
    public override void OnDestroy()
    {
        VendingMachineRegistry.RemoveVendingMachine(_vendingMachineId);
        base.OnDestroy();
    }

    public override void Start()
    {
        base.Start();
        if (!IsServer) return;

        // Create vending machine id and logger
        _vendingMachineId = Guid.NewGuid().ToString();
        _mls = Logger.CreateLogSource(
            $"{VileVendingMachinePlugin.ModGuid} | Vile Vending Machine Server {_vendingMachineId}");
        
        // Initialize the random function and config values
        SubscribeToNetworkEvents();
        Random.InitState(StartOfRound.Instance.randomMapSeed + _vendingMachineId.GetHashCode());
        InitializeConfigValues();
        
        netcodeController.SyncVendingMachineIdClientRpc(_vendingMachineId);
        
        // Make sure the vending machine agent doesn't move, ever.
        // This enemy shouldn't have a NavMeshAgent, but zeeker's enemyAI code needs enemies to have an agent 
        agent.updateRotation = false;
        agent.updatePosition = false;
        
        // Make the vending machine go invisible when its spawning
        //#if !DEBUG
        netcodeController.MeshEnabled.Value = false;
        EnableEnemyMesh(false);
        //#endif

        try
        {
            if (IsServer)
                StartCoroutine(PlaceVendingMachine(() =>
                {
                    netcodeController.SetAnimationTriggerClientRpc(_vendingMachineId, VileVendingMachineClient.ArmAccept);
                }));
        }
        catch (Exception)
        {
            VendingMachineRegistry.IsPlacementInProgress = false;
            throw;
        }

        netcodeController.TargetPlayerClientId.Value = 69420;
    }

    /// <summary>
    /// The coroutine that spawns the vending machine in a sufficient spot
    /// </summary>
    /// <param name="callback">The callback function which in this case is used to play animations after the spawn has finished</param>
    /// <returns></returns>
    private IEnumerator PlaceVendingMachine(Action callback = null)
    {
        // Wait until another vending machine has finished spawning to avoid the vending machines colliding with each-other and messing up the spawning algorithm
        int checksDone = 0;
        while (VendingMachineRegistry.IsPlacementInProgress)
        {
            LogDebug($"is placement in progress?: {VendingMachineRegistry.IsPlacementInProgress}, vending machines in registry: {VendingMachineRegistry.GetVendingMachineRegistryPrint()}");
            if (checksDone >= 30)
            {
                PlacementFail();
                yield break;
            }
            
            checksDone++;
            yield return new WaitForSeconds(3);
        }
        
        VendingMachineRegistry.IsPlacementInProgress = true;
        EntranceTeleport[] doors = [];
        
        // When the vending machine is spawned right at the beginning of the round, the entrance teleports haven't loaded in yet, so we need to wait a bit
        checksDone = 0;
        while (doors.Length == 0)
        {
            if (checksDone >= 30)
            {
                PlacementFail();
                yield break;
            }
            
            doors = GetDoorTeleports();
            LogDebug("There are no entrance teleports available yet.");
            checksDone++;
            
            yield return new WaitForSeconds(2);
        }
        
        // Shuffle the doors
        if (!VileVendingMachineConfig.Instance.AlwaysSpawnOutsideMainEntrance.Value) Shuffle(doors);
        
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

                if (VendingMachineRegistry.IsDoorOccupied(door.entranceId, (EntranceOrExit)i))
                {
                    continue;
                }

                // See if there is a cached placement for this map
                //#if !DEBUG
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
                //#endif
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
                
                // VectorA is a vector representing a line that goes through the centre of the door
                Vector3 vectorA = teleportPosition - doorPosition;
                
                // VectorB is the perpendicular vector of VectorA
                Vector3 vectorB = Vector3.Cross(vectorA, Vector3.up).normalized;
                
                // Move the vending machine a little bit behind the door, and rotate the vending machine so it's facing opposite the door
                vectorA.y = 0;
                transform.position = doorPosition + vectorA.normalized * -0.1f;
                transform.rotation = Quaternion.LookRotation(vectorA);

                // Move vending machine forward until its back is still touching the wall, but its main body is not touching the wall
                bool stageOneSuccess = false;
                for (int k=0; k < 300; k++)
                {
                    #if DEBUG
                    yield return new WaitForSeconds(0.14f);
                    #else
                    yield return new WaitForFixedUpdate();
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

                if (!stageOneSuccess)
                {
                    continue;
                }
                
                // Randomly pick either left or right
                int startingDirection = Random.Range(0, 2) == 0 ? 1 : -1;
                Vector3 startingPosition = transform.position;

                // Iterate over both directions, to see if the vending machine can be placed (to the left or right)
                int currentDirection = 1;
                bool stageTwoSuccess = false;
                for (int j=0; j < 2; j++) 
                {
                    LogDebug($"Iteration of left or right placement: {j+1} out of 2");
                    
                    // First make sure to reset the vending machine's position to in front of the door, just in case this is the second iteration
                    transform.position = startingPosition;
                    
                    // Second, move the vending machine out of the way of the door
                    const float initialDistanceFromDoor = 2.5f;
                    currentDirection = j == 0 ? startingDirection : startingDirection * -1;
                    transform.position += vectorB * (currentDirection * initialDistanceFromDoor);
                    LogDebug($"Moved vending machine {initialDistanceFromDoor} units from door for left/right placement");
                    yield return new WaitForFixedUpdate();
                    
                    // Now check if the vending machine is still in a valid position
                    // If it is, then try to move the vending machine a random distance away from the door, if there is space
                    // If it's not, then try the other side of the door (if both sides have been tried then abort the vending machine placement)
                    if (IsColliderColliding(backCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault) &&
                        !IsColliderColliding(frontCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault) &&
                        !IsColliderColliding(centreCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                    {
                        LogDebug("After initial movement, the placement is still valid");
                        stageTwoSuccess = true;
                            
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
                            yield return new WaitForSeconds(0.2f);
                            #else
                            yield return new WaitForFixedUpdate();
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
                        
                        break;
                    }
                    
                    // Use the relaxed centre collider
                    // This will cause the vending machine to clip into walls, but it will be good enough
                    if (VileVendingMachineConfig.Instance.UseRelaxedColliderIfNeeded.Value)
                    {
                        if (IsColliderColliding(backCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault) &&
                            !IsColliderColliding(frontCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault) &&
                            !IsColliderColliding(relaxedCentreCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                        {
                            LogDebug("Using the relaxed centre collider worked, and a valid placement has been found");
                            stageTwoSuccess = true;
                            break;
                        }
                        
                        LogDebug("Using the relaxed centre collider did not help");
                    }
                    
                    // Current iteration failed
                    #if DEBUG
                    LogDebug($"Current iteration: {j+1} failed");
                    LogDebug($"backCol: {IsColliderColliding(backCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault)}, mainCol: {IsColliderColliding(centreCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault)}, relaxedMainCol: {IsColliderColliding(relaxedCentreCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault)}, frontCol: {IsColliderColliding(frontCollider, StartOfRound.Instance.collidersAndRoomMaskAndDefault)}");
                    #endif
                    
                    #if DEBUG
                    yield return new WaitForSeconds(1f);
                    #else
                    yield return new WaitForFixedUpdate();
                    #endif
                }

                if (!stageTwoSuccess)
                {
                    // If both directions have been tried, abort placement and try another door
                    LogDebug("Both the left and right side has been tried with no success, aborting placement at this current door.");
                    PlacementFail();
                    continue;
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
        }

        PlacementFail();
    }

    /// <summary>
    /// The coroutine responsible for playing the animations when the vending machine spawns
    /// </summary>
    /// <param name="teleportId">The door id which the vending machine spawned at</param>
    /// <param name="leftOrRight">Whether the vending machine spawned to the left or right of the door</param>
    /// <param name="entranceOrExit">Whether the door was an entrance to the dungeon, or exit</param>
    /// <param name="callback">The callback function which is used to play animations</param>
    /// <returns></returns>
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
    
    /// <summary>
    /// Gets all the doors on the moon
    /// </summary>
    /// <returns>An array of all the door objects on the moon</returns>
    private static EntranceTeleport[] GetDoorTeleports()
    {
        return FindObjectsOfType<EntranceTeleport>().Where(t => t != null && t.exitPoint != null && t.isEntranceToBuilding).ToArray();
    }

    /// <summary>
    /// Returns a list of tuples which contain a transform of the exit door and entrance door for a door id
    /// </summary>
    /// <param name="entranceId">The id of the door to get the transforms of</param>
    /// <returns>The list of transforms</returns>
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

    /// <summary>
    /// Spawns a given cola from the vending machine
    /// </summary>
    /// <param name="colaType">The type of cola to spawn</param>
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
                    companyColaMinValue, 
                    companyColaMaxValue + 1);
                break;
            }

            case ColaTypes.Crushed:
            {
                colaSpawnPrefab = VileVendingMachinePlugin.CrushedCompanyColaItem.spawnPrefab;
                colaScrapValue = Random.Range(
                    crushedColaMinValue, 
                    crushedColaMaxValue + 1);
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

    /// <summary>
    /// Despawns the current item the vending machine is holding in its hand
    /// </summary>
    /// <param name="receivedVendingMachineId">The vending machine id received by the network event</param>
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

        netcodeController.IsItemOnHand.Value = false;
    }

    /// <summary>
    /// The coroutine for handling the logic and animations of when a player places an item in the vending machine's hand
    /// </summary>
    /// <returns></returns>
    private IEnumerator AcceptItem()
    {
        yield return new WaitForSeconds(0.1f);
        LogDebug("nkees");
        netcodeController.SetAnimationTriggerClientRpc(_vendingMachineId, VileVendingMachineClient.ArmRetract);

        // Get the scan node properties
        int heldItemValue = 20;
        string heldItemName = "";
        ScanNodeProperties heldItemProperties = itemHolder.GetComponentInChildren<ScanNodeProperties>();
        if (heldItemProperties != null)
        {
            heldItemValue = heldItemProperties.scrapValue;
            heldItemName = heldItemProperties.headerText;
        }
        LogDebug($"Item retrieved: {heldItemName ?? "null"}");
        
        yield return new WaitForSeconds(3f);
        
        // Set the kill probability
        if (heldItemName == "Crushed Company Cola") _currentKillProbability = 1;
        else if (heldItemValue > expensiveItemValue) _currentKillProbability = Mathf.Max(initialKillProbability, _currentKillProbability * killProbabilityReductionFactor);
        else _currentKillProbability = Mathf.Min(_currentKillProbability * killProbabilityGrowthFactor, 1f);

        ColaTypes colaTypeToSpawn;
        if (Random.value < _currentKillProbability) // kill player
        {
            _currentKillProbability = initialKillProbability;
            netcodeController.SetAnimationTriggerClientRpc(_vendingMachineId, VileVendingMachineClient.ArmGrab);

            // kill player with animation event
            yield return new WaitForSeconds(2.5f);
            netcodeController.SetAnimationTriggerClientRpc(_vendingMachineId, VileVendingMachineClient.Grind);
            yield return new WaitForSeconds(4);
            colaTypeToSpawn = ColaTypes.Crushed;
        }
        else colaTypeToSpawn = ColaTypes.Normal;
        
        SpawnCola(colaTypeToSpawn);
        
        yield return new WaitForSeconds(0.5f);
        netcodeController.SetAnimationTriggerClientRpc(_vendingMachineId, VileVendingMachineClient.Dispense);
        yield return new WaitForSeconds(2);
        
        netcodeController.SetAnimationTriggerClientRpc(_vendingMachineId, VileVendingMachineClient.ArmAccept);
        netcodeController.IsItemOnHand.Value = false;
    }
    
    /// <summary>
    /// Checks if a collider is colliding with another collider
    /// </summary>
    /// <param name="collider">The collider to check</param>
    /// <param name="layerMask">The layer mask of the colliders</param>
    /// <returns>Whether the collider is colliding or not</returns>
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

    /// <summary>
    /// Handles the events which should occur when the placement of the vending machine has failed
    /// </summary>
    private void PlacementFail()
    {
        LogDebug("Vending machine could not be placed");
        VendingMachineRegistry.IsPlacementInProgress = false;
        KillEnemyServerRpc(true);
        Destroy(this);
    }

    private void HandleStartAcceptItemAnimation(string receivedVendingMachineId)
    {
        LogDebug("boi1");
        if (!IsServer) return;
        LogDebug("boi2");
        if (_vendingMachineId != receivedVendingMachineId) return;
        LogDebug("boi3");
        StartCoroutine(AcceptItem());
    }

    private void HandleTargetPlayerChanged(ulong oldValue, ulong newValue)
    {
        targetPlayer = newValue == 69420 ? null : StartOfRound.Instance.allPlayerScripts[newValue];
        LogDebug(targetPlayer != null
            ? $"Changed target player to {targetPlayer.playerUsername}"
            : "Changed target player to null");
    }
    
    /// <summary>
    /// Initializes all the config values.
    /// </summary>
    private void InitializeConfigValues()
    {
        initialKillProbability = Mathf.Clamp(VileVendingMachineConfig.Instance.InitialKillProbability.Value, 0f, 1f);
        killProbabilityGrowthFactor = Mathf.Clamp(VileVendingMachineConfig.Instance.KillProbabilityGrowthFactor.Value, 0f, float.MaxValue);
        killProbabilityReductionFactor = Mathf.Clamp(VileVendingMachineConfig.Instance.KillProbabilityReductionFactor.Value, 0f, 1f);
        companyColaMinValue = Mathf.Clamp(VileVendingMachineConfig.Instance.ColaMinValue.Value, 0, int.MaxValue);
        companyColaMaxValue = Mathf.Clamp(VileVendingMachineConfig.Instance.ColaMaxValue.Value, 0, int.MaxValue);
        crushedColaMinValue = Mathf.Clamp(VileVendingMachineConfig.Instance.CrushedColaMinValue.Value, 0, int.MaxValue);
        crushedColaMaxValue = Mathf.Clamp(VileVendingMachineConfig.Instance.CrushedColaMaxValue.Value, 0, int.MaxValue);
        expensiveItemValue = Mathf.Clamp(VileVendingMachineConfig.Instance.ExpensiveItemValue.Value, -1, int.MaxValue);
        
        if (expensiveItemValue == -1) expensiveItemValue = companyColaMaxValue;
    }

    private void SubscribeToNetworkEvents()
    {
        if (!IsServer || _networkEventsSubscribed) return;
        
        netcodeController.OnDespawnHeldItem += HandleDespawnHeldItem;
        netcodeController.OnStartAcceptItemAnimation += HandleStartAcceptItemAnimation;

        netcodeController.TargetPlayerClientId.OnValueChanged += HandleTargetPlayerChanged;

        _networkEventsSubscribed = true;
    }

    private void UnsubscribeFromNetworkEvents()
    {
        if (!IsServer || !_networkEventsSubscribed) return;
        
        netcodeController.OnDespawnHeldItem -= HandleDespawnHeldItem;
        netcodeController.OnStartAcceptItemAnimation -= HandleStartAcceptItemAnimation;

        netcodeController.TargetPlayerClientId.OnValueChanged -= HandleTargetPlayerChanged;
        
        _networkEventsSubscribed = false;
    }
    
    /// <summary>
    /// Shuffles an array of objects
    /// </summary>
    /// <param name="array">The array to shuffle</param>
    /// <typeparam name="T">The object type in the array</typeparam>
    private static void Shuffle<T>(IList<T> array)
    {
        int n = array.Count;
        for (int i = 0; i < n; i++)
        {
            // Pick a new index higher than the current position
            int j = i + Random.Range(0, n - i);
            // Swap elements at indices i and j
            (array[i], array[j]) = (array[j], array[i]);
        }
    }
    
    private void LogDebug(string msg)
    {
        #if DEBUG
        if (!IsServer) return;
        _mls?.LogInfo(msg);
        #endif
    }
}