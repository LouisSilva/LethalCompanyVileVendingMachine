﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BepInEx.Logging;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;
using Vector3 = UnityEngine.Vector3;

namespace LethalCompanyVileVendingMachine;

[SuppressMessage("ReSharper", "RedundantDefaultMemberInitializer")]
public class VileVendingMachineServer : EnemyAI
{
    private ManualLogSource _mls;
    private string _vendingMachineId;
    
    private bool _isItemOnHand = false;

    [Header("AI")]
    [Space(5f)]
    [SerializeField] private float annoyanceLevel = 0f;
    [SerializeField] private float annoyanceThreshold = 3f;
    [SerializeField] private float placementIntervalTimer = 0.1f;

    private readonly LayerMask _placementMask = ~(
        1 << 2
        | 1 << 3
        | 1 << 5
        | 1 << 7
        | 1 << 9 
        | 1 << 13
        | 1 << 14
        | 1 << 18 
        | 1 << 19 
        | 1 << 22
        | 1 << 23
        );

    [Header("Colliders and Transforms")]
    [Space(5f)]
#pragma warning disable 0649
    [SerializeField] private BoxCollider mainCollider;
    [SerializeField] private BoxCollider frontCollider;
    [SerializeField] private BoxCollider backCollider;
    [SerializeField] private BoxCollider floorCollider;
    [SerializeField] private Transform colaPlaceholder;
    [SerializeField] private Transform itemHolder;
    
    [Header("Controllers")]
    [Space(5f)]
    [SerializeField] private InteractTrigger triggerScript;
    [SerializeField] private VileVendingMachineNetcodeController netcodeController;
#pragma warning restore 0649

    private enum States
    {
        Idle,
    }

    public override void OnDestroy()
    {
        netcodeController.OnDespawnHeldItem -= HandleDespawnHeldItem;
        netcodeController.OnSpawnCola -= HandleSpawnCola;
        
        VendingMachineRegistry.RemoveVendingMachine(_vendingMachineId);
    }

    public override void Start()
    {
        base.Start();
        if (!IsServer) return;

        _vendingMachineId = Guid.NewGuid().ToString();
        _mls = Logger.CreateLogSource(
            $"{VileVendingMachinePlugin.ModGuid} | Volatile Vending Machine Server {_vendingMachineId}");
        
        UnityEngine.Random.InitState(StartOfRound.Instance.randomMapSeed + _vendingMachineId.GetHashCode());

        netcodeController.UpdateVendingMachineIdClientRpc(_vendingMachineId);
        netcodeController.OnDespawnHeldItem += HandleDespawnHeldItem;
        netcodeController.OnSpawnCola += HandleSpawnCola;

        triggerScript = GetComponentInChildren<InteractTrigger>();
        triggerScript.onInteract.AddListener(InteractVendingMachine);
        triggerScript.tag = nameof(InteractTrigger);
        triggerScript.interactCooldown = false;
        triggerScript.cooldownTime = 0;

        agent.updateRotation = false;
        agent.updatePosition = false;
        
        // netcodeController.SetMeshEnabledClientRpc(_vendingMachineId, false);
        // EnableEnemyMesh(false);

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

    public override void Update()
    {
        base.Update();
        if (!IsServer) return;
    }

    public override void DoAIInterval()
    {
        base.DoAIInterval();
        if (!IsServer) return;
    }

    private IEnumerator PlaceVendingMachine(Action callback = null)
    {
        while (VendingMachineRegistry.IsPlacementInProgress) yield return new WaitForSeconds(1);
        
        VendingMachineRegistry.IsPlacementInProgress = true;
        bool isFireExitAllowedOutside = IsFireExitAllowedOutside();
        bool isFireExitAllowedInside = IsFireExitAllowedInside();
        
        EntranceTeleport[] doors = GetDoorTeleports();
        
        // Shuffle the doors
        if (!VolatileVendingMachineConfig.Instance.AlwaysSpawnOutsideMainEntrance.Value)
            doors = doors.OrderBy(x => UnityEngine.Random.Range(int.MinValue, int.MaxValue)).ToArray();
        
        // Loops over all the doors on the outside, and inside the dungeon. The i values correspond to the EntranceOrExit enum
        for (int i = 0; i < 2; i++)
        {
            // Checks if a vending machine can spawn outside
            if (!VolatileVendingMachineConfig.Instance.CanSpawnOutsideMaster.Value && i == (int)EntranceOrExit.Entrance)
                continue;

            // Checks if a vending machine can spawn inside the dungeon
            if (!VolatileVendingMachineConfig.Instance.CanSpawnInsideMaster.Value && i == (int)EntranceOrExit.Exit)
                continue;
            
            foreach (EntranceTeleport door in doors)
            {
                // Checks if a vending machine can spawn at the main door, inside or outside
                if (!VolatileVendingMachineConfig.Instance.CanSpawnAtMainDoorMaster.Value && door.entranceId == 0)
                    continue;

                // Checks if a vending machine can spawn at a fire exit, inside or outside
                if (!VolatileVendingMachineConfig.Instance.CanSpawnAtFireExitMaster.Value && door.entranceId != 0)
                    continue;

                // Checks if a vending machine is blacklisted from spawning outside a fire exit on a certain map (to save time)
                if (door.entranceId != 0 && (int)EntranceOrExit.Entrance == i && !isFireExitAllowedOutside)
                    continue;
                
                // Checks if a vending machine is blacklisted from spawning inside a particular dungeon flow e.g. facility because there is no space 99% of the time
                if (door.entranceId != 0 && (int)EntranceOrExit.Exit == i && !isFireExitAllowedInside)
                    continue;

                if (VendingMachineRegistry.IsDoorOccupied(door, i))
                {
                    continue;
                }
                
                Vector3 doorPosition = default;
                Tuple<Transform, int>[] doorTransforms = GetDoorTransforms(door.entranceId);
                foreach (Tuple<Transform, int> doorTransformTuple in doorTransforms)
                {
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
                transform.position = doorPosition + vectorA.normalized * -0.5f;
                transform.rotation = Quaternion.LookRotation(vectorA);

                int counter = 0;
                LogDebug($"backCol: {IsColliderColliding(backCollider, StartOfRound.Instance.collidersAndRoomMask)}, frontCol: {IsColliderColliding(frontCollider, StartOfRound.Instance.collidersAndRoomMask)}");
                while (!IsColliderColliding(backCollider, StartOfRound.Instance.collidersAndRoomMask) || IsColliderColliding(frontCollider, StartOfRound.Instance.collidersAndRoomMask))
                {
                    LogDebug($"counter: {counter}, backCol: {IsColliderColliding(backCollider, StartOfRound.Instance.collidersAndRoomMask)}, frontCol: {IsColliderColliding(frontCollider, StartOfRound.Instance.collidersAndRoomMask)}");
                    if (counter >= 500)
                    {
                        LogDebug("Vending machine could not be placed");
                        VendingMachineRegistry.IsPlacementInProgress = false;
                        KillEnemyClientRpc(true);
                        yield break;
                    }
                    
                    transform.position += vectorA.normalized * 0.01f;
                    counter++;
                    yield return new WaitForSeconds(placementIntervalTimer);
                }

                // Move vending machine to the floor
                Vector3 floorRayStart = transform.position + Vector3.up * 0.5f;
                if (Physics.Raycast(floorRayStart, -Vector3.up, out RaycastHit hit, 8f,
                        StartOfRound.Instance.collidersAndRoomMaskAndDefault))
                {
                    transform.position = new Vector3(transform.position.x,
                        hit.point.y + floorCollider.transform.localPosition.y, transform.position.z);
                }

                // Move vending machien to the left or right
                float leftDistance = GetDistanceToObjectInDirection(-transform.right, 30f);
                float rightDistance = GetDistanceToObjectInDirection(transform.right, 30f);

                float distanceToDoor = leftDistance > rightDistance ? leftDistance : rightDistance;
                if (distanceToDoor == 0) continue;

                int leftOrRight = leftDistance > rightDistance ? (int)LeftOrRight.Left : (int)LeftOrRight.Right;
                distanceToDoor = Mathf.Clamp(distanceToDoor, 0.5f, 3f);
                transform.position += vectorB * ((leftOrRight == (int)LeftOrRight.Left ? -1 : 1) * distanceToDoor);

                // Check if its back is still touching the wall, if not then move back a bit
                counter = 0;
                while (!IsColliderColliding(backCollider, StartOfRound.Instance.collidersAndRoomMask))
                {
                    LogDebug($"2WOOW counter: {counter}, backCol: {IsColliderColliding(backCollider, StartOfRound.Instance.collidersAndRoomMask)}, frontCol: {IsColliderColliding(frontCollider, StartOfRound.Instance.collidersAndRoomMask)}");
                    if (counter >= 500)
                    {
                        LogDebug("Vending machine could not be placed");
                        VendingMachineRegistry.IsPlacementInProgress = false;
                        KillEnemyClientRpc(true);
                        yield break;
                    }
                    
                    transform.position += vectorA.normalized * -0.01f;
                    counter++;
                    yield return new WaitForSeconds(placementIntervalTimer);
                }
                
                // Check if its front is still not touching anything, if so then move forward a bit
                counter = 0;
                while (IsColliderColliding(frontCollider, StartOfRound.Instance.collidersAndRoomMask))
                {
                    LogDebug($"3WOZO counter: {counter}, backCol: {IsColliderColliding(backCollider, StartOfRound.Instance.collidersAndRoomMask)}, frontCol: {IsColliderColliding(frontCollider, StartOfRound.Instance.collidersAndRoomMask)}");
                    if (counter >= 500)
                    {
                        LogDebug("Vending machine could not be placed");
                        VendingMachineRegistry.IsPlacementInProgress = false;
                        KillEnemyClientRpc(true);
                        yield break;
                    }
                    
                    transform.position += vectorA.normalized * 0.01f;
                    counter++;
                    yield return new WaitForSeconds(placementIntervalTimer);
                }
                
                Destroy(frontCollider.gameObject);
                Destroy(backCollider.gameObject);
                Destroy(floorCollider.gameObject);

                // Rigidbody rigidbody = gameObject.AddComponent<Rigidbody>();
                // rigidbody.isKinematic = true;

                VendingMachineRegistry.AddVendingMachine(_vendingMachineId, door, leftOrRight, i);
                VendingMachineRegistry.IsPlacementInProgress = false;
                netcodeController.PlayMaterializeVfxClientRpc(_vendingMachineId, transform.position, transform.rotation);
                if (callback == null) yield break;
                yield return new WaitForSeconds(5f);
                EnableEnemyMesh(true);
                callback.Invoke();
                yield break;
            }
        }

        LogDebug("Vending machine could not be placed");
        VendingMachineRegistry.IsPlacementInProgress = false;
        KillEnemyClientRpc(true);
    }

    private float GetDistanceToObjectInDirection(Vector3 direction, float maxDistance = 50f)
    {
        float distance = 0f;
        if (Physics.Raycast(transform.position, direction, out RaycastHit hit3, maxDistance, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
        {
            distance = hit3.distance;
        }
        else
        {
            distance = 999999;
            LogDebug("No object found");
        }

        return distance;
    }
    
    private EntranceTeleport[] GetDoorTeleports()
    {
        return FindObjectsOfType<EntranceTeleport>().Where(t => t != null && t.exitPoint != null && t.isEntranceToBuilding).ToArray();
    }

    private Tuple<Transform, int>[] GetDoorTransforms(int entranceId)
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

    private void HandleSpawnCola(string recievedVendingMachineId)
    {
        if (!IsServer) return;
        if (recievedVendingMachineId != _vendingMachineId) return;
        
        /*
        GameObject colaObject = Instantiate(
            testColaPrefab,
            colaPlaceholder.position,
            colaPlaceholder.rotation,
            colaPlaceholder);
        
        colaObject.GetComponent<NetworkObject>().Spawn();
        colaObject.GetComponent<Transform>().SetParent(colaPlaceholder);
        netcodeController.UpdateColaNetworkObjectReferenceClientRpc(_vendingMachineId, colaObject.GetComponent<NetworkObject>());
        */
        
        GameObject colaObject = Instantiate(
            VileVendingMachinePlugin.CompanyColaItem.spawnPrefab,
            colaPlaceholder.position,
            colaPlaceholder.rotation,
            colaPlaceholder);

        CompanyColaBehaviour colaBehaviour = colaObject.GetComponent<CompanyColaBehaviour>();
        if (colaBehaviour == null) _mls.LogError("colaBehaviour is null");
        colaBehaviour.isPartOfVendingMachine = true;
        colaBehaviour.isPhysicsEnabled = true;
        int colaScrapValue = UnityEngine.Random.Range(
            VolatileVendingMachineConfig.Instance.ColaMinValue.Value, 
            VolatileVendingMachineConfig.Instance.ColaMaxValue.Value + 1);

        // ScanNodeProperties scanNode = colaObject.GetComponent<ScanNodeProperties>();
        // if (scanNode == null)
        // {
        //     scanNode = colaObject.AddComponent<ScanNodeProperties>();
        //     scanNode.scrapValue = colaScrapValue;
        //     scanNode.headerText = "Company Cola";
        //     scanNode.subText = $"Value: {colaScrapValue}";
        //     scanNode.maxRange = 13;
        //     scanNode.minRange = 1;
        //     scanNode.requiresLineOfSight = true;
        //     scanNode.creatureScanID = -1;
        //     scanNode.nodeType = 2;
        // }
        
        colaObject.GetComponent<GrabbableObject>().fallTime = 1f;
        colaObject.GetComponent<GrabbableObject>().SetScrapValue(colaScrapValue);
        colaBehaviour.UpdateScrapValue(colaScrapValue);
        RoundManager.Instance.totalScrapValueInLevel += colaScrapValue;
        
        colaObject.GetComponent<NetworkObject>().Spawn();
        colaObject.GetComponent<Transform>().SetParent(colaPlaceholder);
        netcodeController.UpdateColaNetworkObjectReferenceClientRpc(_vendingMachineId, colaObject.GetComponent<NetworkObject>());
    }

    private void HandleDespawnHeldItem(string recievedVendingMachineId)
    {
        if (!IsServer) return;
        if (recievedVendingMachineId != _vendingMachineId) return;
        
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

        _isItemOnHand = false;
    }
    
    private void InteractVendingMachine(PlayerControllerB playerInteractor)
    {
        if (!playerInteractor.isHoldingObject) return;
        PlaceItemInHand(ref playerInteractor);
    }
    
    private void PlaceItemInHand(ref PlayerControllerB playerInteractor)
    {
        if (_isItemOnHand || GameNetworkManager.Instance == null) return;

        _isItemOnHand = false;
        netcodeController.ChangeTargetPlayerClientRpc(_vendingMachineId, (int)playerInteractor.actualClientId);
        netcodeController.PlayerDiscardHeldObjectClientRpc(_vendingMachineId, (int)playerInteractor.actualClientId);
        
        StartCoroutine(AcceptItem());
    }

    private IEnumerator AcceptItem()
    {
        yield return new WaitForSeconds(0.1f);
        netcodeController.DoAnimationClientRpc(_vendingMachineId, VileVendingMachineClient.ArmRetract);
        
        yield return new WaitForSeconds(2.5f);
        if (annoyanceLevel >= annoyanceThreshold) // kill player
        {
            annoyanceLevel = 0;
            netcodeController.DoAnimationClientRpc(_vendingMachineId, VileVendingMachineClient.ArmGrab);
            
            // kill player with animation event
            yield return new WaitForSeconds(2.5f);
            netcodeController.DoAnimationClientRpc(_vendingMachineId, VileVendingMachineClient.Grind);
            yield return new WaitForSeconds(4);
        }
        else // dont kill player
        {
            annoyanceLevel++;
        }
        
        SpawnCola();
        yield return new WaitForSeconds(0.5f);
        netcodeController.DoAnimationClientRpc(_vendingMachineId, VileVendingMachineClient.Dispense);
        yield return new WaitForSeconds(2);
        
        netcodeController.DoAnimationClientRpc(_vendingMachineId, VileVendingMachineClient.ArmAccept);
        _isItemOnHand = false;
    }
    
    private static bool IsColliderAgainstWall(Collider collider)
    {
        return Physics.Raycast(collider.bounds.center, -collider.transform.forward, out RaycastHit hit, collider.bounds.size.z);
    }
    
    private bool IsColliderColliding(Collider collider)
    {
        Collider[] collidingObjects = Physics.OverlapBox(collider.bounds.center, collider.bounds.extents, collider.transform.rotation);
        foreach (Collider obj in collidingObjects) {
            LogDebug("Colliding with: " + obj.name);
        }
        return collidingObjects.Length > 0;
        //return Physics.CheckBox(collider.bounds.center, collider.bounds.extents, collider.transform.rotation);
    }
    
    private bool IsColliderColliding(Collider collider, LayerMask layerMask)
    {
        Collider[] collidingObjects = Physics.OverlapBox(collider.bounds.center, collider.bounds.extents, collider.transform.rotation, layerMask);
        foreach (Collider obj in collidingObjects) {
            LogDebug("Colliding with: " + obj.name);
            DrawDebugCircleAtPosition(obj.bounds.center);
        }
        return collidingObjects.Length > 0;
        //return Physics.CheckBox(collider.bounds.center, collider.bounds.extents, collider.transform.rotation);
    }

    private void SpawnCola()
    {
        HandleSpawnCola(_vendingMachineId);
    }

    private static bool IsFireExitAllowedOutside()
    {
        string[] noFireExitDoors =
        [
            "Level1Experimentation",
            "Level8Titan",
        ];

        return noFireExitDoors.All(mapName => StartOfRound.Instance.currentLevel.sceneName != mapName);
    }

    private static bool IsFireExitAllowedInside()
    {
        string[] noFireExitDoors =
        [
            "Level1Flow",
        ];

        return noFireExitDoors.All(dungeonName =>
            RoundManager.Instance.dungeonGenerator.Generator.DungeonFlow.name != dungeonName);
    }
    
    private void DrawDebugHorizontalLineAtTransform(Transform transform, Color colour = new())
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

    private void DrawDebugLineAtTransformForward(Transform transform, Color colour = new())
    {
        GameObject lineObj = new("ForwardLine");
        lineObj.transform.position = transform.position;
        
        LineRenderer lineRenderer = lineObj.AddComponent<LineRenderer>();
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.widthMultiplier = 0.1f;
        lineRenderer.positionCount = 4;
        lineRenderer.useWorldSpace = true;
        lineRenderer.startColor = colour;
        lineRenderer.endColor = colour;
        
        Vector3 lineEnd = transform.position + transform.forward * 8;
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
    
    private void DrawDebugCircleAtPosition(Vector3 position)
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

    public string GetId()
    {
        return _vendingMachineId;
    }
    
    private void LogDebug(string msg)
    {
        #if DEBUG
        if (!IsServer) return;
        _mls?.LogInfo(msg);
        #endif
    }
}