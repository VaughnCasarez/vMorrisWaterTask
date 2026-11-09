using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public class TreasureChestManager : MonoBehaviour
{
    // Chest parameters
    [Header("Chest Settings")]
    public GameObject treasureChestPrefab;         // Reference to the chest prefab
    public float chestDetectionDistance = 2.0f;   // Distance at which the chest is "found"

    // Player reference
    [Header("Player Settings")]
    public PlayerController playerController;      // Reference to the PlayerController script

    // Private variables
    private Vector3 chestPosition;                 // Position where the chest will appear
    private bool chestFound = false;               // Flag to check if the chest has been found
    private bool positionInitialized = false;      // True once Start() has picked the chest position

    public event Action OnChestFound;

    public GameObject chestMarkerPrefab; // Assign via Inspector
    private GameObject chestMarkerInstance;
    private GameObject treasureChestInstance;

    [Header("Arena")]
    public Vector3 arenaCenter = Vector3.zero;     // Center of the grass circle
    public float edgeMargin = 3f;                  // Keep the chest this far inside the circle edge

    float MaxChestRadius
    {
        get { return Mathf.Max(0f, GameSettings.circleRadius - edgeMargin); }
    }

    Vector3 ClampToArena(Vector3 pos)
    {
        Vector3 offset = new Vector3(pos.x - arenaCenter.x, 0, pos.z - arenaCenter.z);
        offset = Vector3.ClampMagnitude(offset, MaxChestRadius);
        return new Vector3(arenaCenter.x + offset.x, 0, arenaCenter.z + offset.z);
    }

    public Vector3 ChestPosition
    {
        get { return chestPosition; }
    }


    void Start()
    {
        // Centered on the arena (not the player, who may already have been moved to a random start)
        Vector2 randomDirection = UnityEngine.Random.insideUnitCircle * MaxChestRadius;
        chestPosition = new Vector3(randomDirection.x, 0, randomDirection.y) + arenaCenter;

        // make sure the chest is not too close to the player, without leaving the circle
        if (Vector3.Distance(chestPosition, playerController.transform.position) < 2f)
        {
            chestPosition = ClampToArena(chestPosition + new Vector3(2, 0, 0));
        }

        positionInitialized = true;
        SpawnChestPosition();
    }

    void Update()
    {
        if (!chestFound)
        {
            CheckChestProximity();
        }
    }

    void SpawnChestPosition()
    {


        Debug.Log($"Chest spawned at: {chestPosition}");

        // Handle marker visibility based on trial type
        if (GameSettings.trialType == GameSettings.TrialType.Visible)
        {
            if (chestMarkerPrefab != null)
            {
                chestMarkerInstance = Instantiate(chestMarkerPrefab, chestPosition, Quaternion.identity);
            }
        }
        else if (GameSettings.trialType == GameSettings.TrialType.Invisible)
        {
            if (chestMarkerPrefab != null)
            {
                chestMarkerInstance = Instantiate(chestMarkerPrefab, chestPosition, Quaternion.identity);
                chestMarkerInstance.SetActive(false); // Marker is invisible
            }
        }
    }
    void CheckChestProximity()
    {
        float distance = Vector3.Distance(playerController.transform.position, chestPosition);
        if (distance <= chestDetectionDistance && GameSettings.trialType != GameSettings.TrialType.Absent)
        {
            FindChest();
        }
    }

    void FindChest()
    {
        chestFound = true;

        // Notify subscribers that the chest has been found
        OnChestFound?.Invoke();

        // Spawn the chest if the trial type is not Absent
        if (GameSettings.trialType != GameSettings.TrialType.Absent)
        {
            // Spawn chest in front of the player
            Vector3 chestViewPosition = playerController.transform.position + playerController.transform.forward * 5 + Vector3.up * 0.01f;
            // treasureChestInstance = Instantiate(treasureChestPrefab, chestViewPosition, Quaternion.Euler(0, 0, 0));
            // treasureChestInstance.transform.localScale *= 5f;
        }

        Debug.Log($"Chest found at: {chestPosition}");

        // Freeze the player
        // playerController.FreezePlayer();
 
        // Optional: Provide feedback to the player
        Debug.Log("Chest found!");
    }

    public void ResetChest()
    {
        chestFound = false;

        // GameManager.Start() can call this before our Start() has picked a position;
        // spawning now would place a stray marker at (0,0,0) and Start() would then spawn a second one.
        if (!positionInitialized)
        {
            return;
        }

        // Trial radius may have changed since the chest was placed
        chestPosition = ClampToArena(chestPosition);

        // Destroy existing chest and marker instances to prevent duplicates
        if (treasureChestInstance != null)
        {
            Destroy(treasureChestInstance);
        }

        if (chestMarkerInstance != null)
        {
            Destroy(chestMarkerInstance);
        }

        // Only spawn chest position if the trial type is not Absent
        if (GameSettings.trialType != GameSettings.TrialType.Absent)
        {
            SpawnChestPosition();
        }

        // Optional: Provide feedback
        Debug.Log("Chest reset for the next trial.");
    }
}

