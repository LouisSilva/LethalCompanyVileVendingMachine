using System;
using System.Collections;
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

    [SerializeField] private InteractTrigger triggerScript;
    private GrabbableObject _itemOnHand;

    [Header("AI")]
    [Space(5f)]
    [SerializeField] private float annoyanceLevel = 0f;
    [SerializeField] private float annoyanceThreshold = 3f;
    
    [Header("Colliders and Transforms")]
    [Space(5f)]
#pragma warning disable 0649
    [SerializeField] private BoxCollider itemHolder;
    [SerializeField] private BoxCollider frontCollider;
    [SerializeField] private BoxCollider backCollider;
    [SerializeField] private BoxCollider floorCollider;
    [SerializeField] private Transform colaPlaceholder;
    [SerializeField] private GameObject testColaPrefab;
    
    [Header("Controllers")]
    [Space(5f)]
    [SerializeField] private VileVendingMachineNetcodeController netcodeController;
#pragma warning restore 0649

    private enum States
    {
        Idle,
    }

    public override void OnDestroy()
    {
        netcodeController.OnDespawnHeldItem -= HandleDespawnHeldItem;
        netcodeController.OnUpdateServerHeldItemCopy -= HandleUpdateServerHeldItemCopy;
        netcodeController.OnSpawnCola -= HandleSpawnCola;
    }   

    public override void Start()
    {
        base.Start();
        if (!IsServer) return;

        _vendingMachineId = Guid.NewGuid().ToString();
        _mls = Logger.CreateLogSource(
            $"{VileVendingMachinePlugin.ModGuid} | Volatile Vending Machine Server {_vendingMachineId}");
        
        netcodeController.UpdateVendingMachineIdClientRpc(_vendingMachineId);
        netcodeController.OnDespawnHeldItem += HandleDespawnHeldItem;
        netcodeController.OnUpdateServerHeldItemCopy += HandleUpdateServerHeldItemCopy;
        netcodeController.OnSpawnCola += HandleSpawnCola;

        foreach (BoxCollider collider in GetComponentsInChildren<BoxCollider>())
        {
            if (collider.name != "ItemHolder") continue;
            
            itemHolder = collider;
            break;
        }

        triggerScript = GetComponentInChildren<InteractTrigger>();
        triggerScript.onInteract.AddListener(InteractVendingMachine);
        triggerScript.tag = nameof(InteractTrigger);
        triggerScript.interactCooldown = false;
        triggerScript.cooldownTime = 0;
        
        agent.updateRotation = false;
        agent.updatePosition = false;
        if (IsServer) StartCoroutine(PlaceVendingMachine(() => 
            { netcodeController.ChangeAnimationParameterBoolClientRpc(_vendingMachineId, VileVendingMachineClient.ArmAccept, true);}));
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

    private void InteractVendingMachine(PlayerControllerB playerInteractor)
    {
        if (!playerInteractor.isHoldingObject) return;
        PlaceItemInHand(ref playerInteractor);
    }

    private IEnumerator PlaceVendingMachine(Action callback = null)
    {
        EntranceTeleport[] doors = GetDoors();
        foreach (EntranceTeleport door in doors)
        {
            Vector3 vectorA = door.entrancePoint.position - door.transform.position;
            Vector3 vectorB = Vector3.Cross(vectorA, Vector3.up).normalized;

            vectorA.y = 0;
            transform.position = door.transform.position + vectorA.normalized * -0.5f;
            transform.rotation = Quaternion.LookRotation(vectorA);
            
            while (!IsColliderAgainstWall(backCollider) && IsColliderColliding(frontCollider))
            {
                transform.position += vectorA.normalized * 0.1f;
                yield return new WaitForSeconds(0.01f);
            }

            Vector3 floorRayStart = transform.position + Vector3.up * 0.5f;
            if (Physics.Raycast(floorRayStart, -Vector3.up, out RaycastHit hit, 8f, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
            {
                transform.position = new Vector3(transform.position.x,
                    hit.point.y + floorCollider.transform.localPosition.y, transform.position.z);
            }
            
            // Cast ray to left
            float leftDistance;
            if (Physics.Raycast(transform.position, -transform.right, out RaycastHit hit2, 50f, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
            {
                leftDistance = hit2.distance;
                if (hit2.collider.CompareTag("VolatileVendingMachine"))
                    leftDistance = 0;
            }
            else
            {
                leftDistance = 999999;
                LogDebug("No object found to the left");
            }
            
            // Cast ray to right
            float rightDistance;
            if (Physics.Raycast(transform.position, transform.right, out RaycastHit hit3, 50f, StartOfRound.Instance.collidersAndRoomMaskAndDefault))
            {
                rightDistance = hit3.distance;
                if (hit3.collider.CompareTag("VolatileVendingMachine"))
                    rightDistance = 0;
            }
            else
            {
                rightDistance = 999999;
                LogDebug("No object found to the right");
            }

            float distanceToDoor = leftDistance > rightDistance ? leftDistance : rightDistance;
            if (distanceToDoor <= 0) continue;
            
            distanceToDoor = Mathf.Clamp(distanceToDoor, 1f, 3f);
            transform.position += vectorB * distanceToDoor;
            
            Destroy(frontCollider);
            Destroy(backCollider);
            Destroy(floorCollider);
            
            if (callback == null) yield break;
            yield return new WaitForSeconds(0.5f);
            callback.Invoke();
            yield break;
        }
        
        LogDebug("Vending machine could not be placed");
    }

    private EntranceTeleport[] GetDoors()
    {
        return FindObjectsOfType<EntranceTeleport>().Where(t => t != null && t.exitPoint != null).ToArray();
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

        int colaScrapValue = UnityEngine.Random.Range(60, 91);
        colaObject.GetComponent<GrabbableObject>().fallTime = 0f;
        colaObject.GetComponent<GrabbableObject>().SetScrapValue(colaScrapValue);
        RoundManager.Instance.totalScrapValueInLevel += colaScrapValue;
        
        colaObject.GetComponent<NetworkObject>().Spawn();
        colaObject.GetComponent<Transform>().SetParent(colaPlaceholder);
        netcodeController.UpdateColaNetworkObjectReferenceClientRpc(_vendingMachineId, colaObject.GetComponent<NetworkObject>());
    }

    private void HandleDespawnHeldItem(string recievedVendingMachineId)
    {
        if (!IsServer) return;
        if (recievedVendingMachineId != _vendingMachineId) return;
        if (_itemOnHand == null)
        {
            _mls.LogWarning("ItemOnHand is null, unable to despawn object");
            return;
        }
        
        _itemOnHand.NetworkObject.Despawn();
    }
    
    private void PlaceItemInHand(ref PlayerControllerB playerInteractor)
    {
        if (_itemOnHand != null || GameNetworkManager.Instance == null) return;
        
        _itemOnHand = playerInteractor.currentlyHeldObjectServer;
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
        
        // Reset values
        _itemOnHand = null;
    }
    
    private static bool IsColliderAgainstWall(Collider collider)
    {
        return Physics.Raycast(collider.bounds.center, -collider.transform.forward, out RaycastHit hit, collider.bounds.size.z);
    }
    
    private static bool IsColliderColliding(Collider collider)
    {
        return Physics.CheckBox(collider.bounds.center, collider.bounds.extents, collider.transform.rotation);
    }

    private void HandleUpdateServerHeldItemCopy(string recievedVendingMachineId,
        NetworkObjectReference itemObjectReference)
    {
        if (!IsServer) return;
        if (recievedVendingMachineId != _vendingMachineId) return;
        if (!itemObjectReference.TryGet(out NetworkObject itemNetworkObject)) return;

        _itemOnHand = itemNetworkObject.GetComponent<GrabbableObject>();
    }

    private void SpawnCola()
    {
        HandleSpawnCola(_vendingMachineId);
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
        const float circleRadius = 4f;
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
    
    private void LogDebug(string msg)
    {
        #if DEBUG
        if (!IsServer) return;
        _mls?.LogInfo(msg);
        #endif
    }
}