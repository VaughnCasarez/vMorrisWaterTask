using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    // References to other managers
    [Header("Managers")]
    public TreasureChestManager treasureChestManager;
    public DataCollector dataCollector;
    public PlayerController playerController;
    public CueManager cueManager;
    public DistalCueManager distalCueManager;

    [Header("UI Elements")]
    public GameObject timeUpPanel;
    public TextMeshProUGUI instructionText;
    [Header("Time Up Panel")]
    public float timeUpDisplaySeconds = 3f;

    [Header("End of Experiment UI")]
    public GameObject experimentCompletePanel;
    public Button quitButton;

    [Header("VR Notifications")]
    [Tooltip("Scene object with a VRNotification component. If empty, a default one is created at runtime.")]
    public VRNotification treasureFoundNotification;
    [Tooltip("Message shown instead of the notification's own message after the final trial. Leave empty to use it as-is.")]
    public string finalTrialTreasureMessage = "That was the final trial.";
    public VRNotification experimentCompleteNotification;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip treasureJingle;


    [Header("Coin Effect")]
    public GameObject coinPrefab;

    [Header("Countdown Circle")]
    public Image countdownCircle;
    public GameObject lightBeamPrefab;
    public Image screenFadeImage;
    public int numberOfCoins = 20;
    public float spawnHeight = 10f;
    public float spawnRadius = 2f;


    // Trial management
    private int currentTrial = 1;
    private bool trialInProgress = false;

    private float trialTimer = 0f;
    private TrialDefinition currentTrialDefinition;

    public float trialEndDuration = 10f;      // Duration to stay in the trial-end sequence.
    public float elevationOffset = 1f;      // How much to raise the player (in Unity units).
    public float platformSafeRadius = 10.0f;   // The safe radius on the platform where the player can roam.
    public static int CurrentTrialIndex => instance.currentTrial - 1;
    private static GameManager instance;

    void Awake()
    {
        instance = this;
    }
    void LogAllCameras()
    {
        Camera[] allCams = Camera.allCameras;
        foreach (var cam in allCams)
        {
            Debug.Log($"[CAMERA] Name: {cam.name} | Enabled: {cam.enabled} | Tag: {cam.tag} | Depth: {cam.depth}");
        }
    }

    void DisableExtraCameras()
    {
        foreach (var cam in Camera.allCameras)
        {
            if (cam != Camera.main)
            {
                cam.enabled = false;
                Debug.Log("Disabled extra camera: " + cam.name);
            }
        }
    }


    void Start()
    {

        DisableExtraCameras();
        Camera[] allCams = Camera.allCameras;
        foreach (var cam in allCams)
        {
            Debug.Log($"[CAMERA] Name: {cam.name} | Enabled: {cam.enabled} | Tag: {cam.tag} | Depth: {cam.depth}");
        }




        if (treasureChestManager == null || dataCollector == null || playerController == null || cueManager == null)
        {
            Debug.LogError("One or more manager references are missing in the GameManager.");
            return;
        }

        ResetTimeUpPanel();

        if (experimentCompletePanel != null)
            experimentCompletePanel.SetActive(false);

        // Subscribe to the chest found event
        treasureChestManager.OnChestFound += OnChestFound;

        // Start the first trial
        StartNextTrial();
    }

    /// <summary>
    /// Initiates the next trial.
    /// </summary>
    void StartNextTrial()
    {
        if (currentTrial > GameSettings.numberOfTrials)
        {
            Debug.Log("All trials completed!");

            // Show end-of-experiment UI
            if (experimentCompletePanel != null)
                experimentCompletePanel.SetActive(true);

            ShowNotification(
                ref experimentCompleteNotification,
                "Experiment Complete",
                "Thank you for participating! You may now remove the headset.",
                0f,
                new Color(0.35f, 0.85f, 0.5f),
                null);

            // Optionally freeze player
            playerController.FreezePlayer();

            return;

        }

        trialInProgress = true;
        Debug.Log($"Starting Trial {currentTrial}");
        ResetTimeUpPanel();

        // Retrieve the current trial definition from the game settings
        currentTrialDefinition = GameSettings.allTrials[currentTrial - 1];

        GameSettings.circleRadius = currentTrialDefinition.circleRadius;
        GameSettings.numberOfProximalCues = currentTrialDefinition.numberOfProximalCues;
        GameSettings.numberOfQuadrants = QuadrantUtility.ClampCount(currentTrialDefinition.numberOfQuadrants);
        GameSettings.trialType = currentTrialDefinition.trialType;
        GameSettings.timeLimit = currentTrialDefinition.timeLimit;

        if (distalCueManager != null)
        {
            distalCueManager.SetDistalCuesActive(GameSettings.enableDistalCues);
        }



        // Reset DataCollector for the new trial
        dataCollector.StartNewTrial(currentTrial);

        // Reset Player Position then inform DataCollector to log the true start
        ResetPlayerPosition();
        if (dataCollector != null)
        {
            dataCollector.InitializeTrialStartingPosition();
        }

        // Reset the chest
        treasureChestManager.ResetChest();


        trialTimer = 0f;

        // Unfreeze the player to allow movement
        playerController.UnfreezePlayer();

        cueManager.UpdateCues(currentTrialDefinition.numberOfProximalCues);

        // Optional: Update UI
        // uiManager.UpdateTrialDisplay(currentTrial);
    }

    void Update()
    {
        // 🔴 QUIT CHECK
        if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            QuitExperiment();
            return;
        }

        if (trialInProgress)
        {
            // Increment trial timer
            trialTimer += Time.deltaTime;

            // Check if time limit is reached
            // Check time limit using the current trial's timeLimit setting (if set to a positive value)
            if (currentTrialDefinition.timeLimit > 0 && trialTimer >= currentTrialDefinition.timeLimit)
            {
                OnTrialTimeout();
            }
        }
    }
    void ShowNotification(ref VRNotification notification, string defaultTitle, string defaultMessage,
        float defaultDuration, Color defaultAccent, string messageOverride)
    {
        // Fall back to a runtime-built default when none is assigned in the scene
        if (notification == null)
        {
            var go = new GameObject(defaultTitle + " Notification");
            notification = go.AddComponent<VRNotification>();
            notification.BuildUI();
            notification.title = defaultTitle;
            notification.message = defaultMessage;
            notification.displayDuration = defaultDuration;
            notification.accentColor = defaultAccent;
            notification.titleColor = defaultAccent;
        }

        if (string.IsNullOrEmpty(messageOverride))
            notification.Show();
        else
            notification.Show(notification.title, messageOverride);
    }

    void OnChestFound()
    {
        if (!trialInProgress)
            return;

        trialInProgress = false;
        Debug.Log($"Trial {currentTrial} completed!");
        if (audioSource != null && treasureJingle != null)
        {
            audioSource.PlayOneShot(treasureJingle);
        }

        // Immediately stop collection and export results to avoid counting the wait period
        if (dataCollector != null)
        {
            dataCollector.StopCollectionAndExport();
        }

        // Freeze the player

        SpawnCoinEffect();
        SpawnLightBeamEffect();

        bool lastTrial = currentTrial >= GameSettings.numberOfTrials;
        ShowNotification(
            ref treasureFoundNotification,
            "Treasure Found!",
            "Great job! Look around while you wait for the next trial.",
            4f,
            new Color(1f, 0.82f, 0.2f),
            lastTrial ? finalTrialTreasureMessage : null);

        StartCoroutine(TrialEndSequence());
    }

    void SpawnCoinEffect()
    {
        if (coinPrefab == null || playerController == null) return;

        Vector3 playerPos = playerController.transform.position;

        for (int i = 0; i < numberOfCoins; i++)
        {
            Vector2 circle = Random.insideUnitCircle * spawnRadius;
            Vector3 spawnPos = new Vector3(
                playerPos.x + circle.x,
                playerPos.y + spawnHeight,
                playerPos.z + circle.y
            );

            GameObject coin = Instantiate(coinPrefab, spawnPos, Quaternion.Euler(0, Random.Range(0, 360), 0));

            Rigidbody rb = coin.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.AddTorque(Random.insideUnitSphere * 10f, ForceMode.Impulse);
            }

            Destroy(coin, 10f); // Clean up after 5 seconds
        }
    }


    IEnumerator TrialEndSequence()
    {
        bool isLastTrial = currentTrial >= GameSettings.numberOfTrials;
        Vector3 playerPosition = playerController.transform.position;

        playerController.transform.position = new Vector3(playerPosition.x, playerPosition.y + elevationOffset, playerPosition.z);
        Vector3 fixedPosition = playerController.transform.position;

        if (instructionText != null)
        {
            instructionText.text = isLastTrial
                ? "Congratulations! You've completed all trials!"
                : "Good job! Feel free to look around before the next trial.";

            // Enable the parent panel as well, if it exists
            Transform parent = instructionText.transform.parent;
            if (parent != null)
            {
                parent.gameObject.SetActive(true);
            }

            instructionText.gameObject.SetActive(true);
        }

        // ⏱️ Activate and reset circular countdown
        float countdownDuration = 10f;
        float countdownTime = countdownDuration;

        if (countdownCircle != null)
        {
            countdownCircle.gameObject.SetActive(true);
            countdownCircle.fillAmount = 1f;
        }

        while (countdownTime > 0f)
        {
            // Lock player position
            playerController.transform.position = fixedPosition;

            // Update fill amount (normalized)
            if (countdownCircle != null)
            {
                countdownCircle.fillAmount = countdownTime / countdownDuration;
            }

            countdownTime -= Time.deltaTime;
            yield return null;
        }

        if (screenFadeImage != null)
        {
            yield return StartCoroutine(FadeScreen(fadeToBlack: true, duration: 1f));
        }

        // Hide UI
        if (instructionText != null)
        {
            instructionText.gameObject.SetActive(false);
            Transform parent = instructionText.transform.parent;
            if (parent != null)
            {
                parent.gameObject.SetActive(false);
            }
        }

        if (countdownCircle != null)
            countdownCircle.gameObject.SetActive(false);

        // Exports already performed at chest hit

        currentTrial++;
        StartCoroutine(StartNextTrialWithDelay(0.001f));
    }




    /// <summary>
    /// Called when the trial timer runs out (timeout).
    /// </summary>
    void OnTrialTimeout()
    {
        Debug.Log($"Trial {currentTrial} ended due to timeout.");
        trialInProgress = false;

        // Export data just like the trial ended
        // Immediately stop collection and export at timeout as well
        if (dataCollector != null)
        {
            dataCollector.StopCollectionAndExport();
        }

        StartCoroutine(TimeUpSequence());
    }



    void ResetPlayerPosition()
    {
        // Determine starting position based on the trial definition’s starting location option
        Vector3 newStartPosition = GetStartingPosition(currentTrialDefinition);
        if (playerController != null)
        {
            newStartPosition = playerController.ClampPositionToArena(newStartPosition);
        }
        playerController.transform.position = newStartPosition;

        // Set rotation based on starting location option
        if (currentTrialDefinition.startingLocationOption == StartingLocationOption.Auto)
        {
            // Use the chest’s position as the reference
            Vector3 chestPos = treasureChestManager.ChestPosition;
            Vector3 directionToChest = (chestPos - newStartPosition).normalized;

            // Offset the angle so the player does not face the chest directly.
            float offsetAngle = 90f; // degrees
            float sign = Random.value > 0.5f ? 1f : -1f;
            Vector3 rotatedDirection = Quaternion.Euler(0, offsetAngle * sign, 0) * new Vector3(directionToChest.x, 0, directionToChest.z);
            playerController.transform.rotation = Quaternion.LookRotation(rotatedDirection);
        }
        else
        {
            // For Randomize and Circle options, use a default orientation.
            playerController.transform.rotation = Quaternion.identity;
        }

        // Reset player's CharacterController if necessary
        CharacterController controller = playerController.GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.enabled = false;
            controller.enabled = true;
        }

        Debug.Log("Player position reset using starting location option: " + currentTrialDefinition.startingLocationOption);
    }

    // Helper method to determine the starting position based on trial settings.
    Vector3 GetStartingPosition(TrialDefinition trialDef)
    {
        switch (trialDef.startingLocationOption)
        {
            case StartingLocationOption.Randomize:
                {
                    // Choose any random point within a circle of radius equal to circleRadius.
                    Vector2 randomPoint = Random.insideUnitCircle * trialDef.circleRadius * 0.9f;
                    return new Vector3(randomPoint.x, 1, randomPoint.y); // Offset of to avoid being underground
                }
            case StartingLocationOption.Auto:
                {
                    // For the auto option, avoid the quadrant containing the chest.
                    Vector3 chestPos = treasureChestManager.ChestPosition;
                    if (chestPos == Vector3.zero)
                    {
                        // Fallback: choose a random angle if the chest position is not available.
                        Debug.LogWarning("Chest position not available. Choosing random starting position.");
                        float randomAngle = Random.Range(0, 360f);
                        float rad = randomAngle * Mathf.Deg2Rad;
                        return new Vector3(Mathf.Cos(rad), 0, Mathf.Sin(rad)) * trialDef.circleRadius * 0.9f;
                    }
                    else
                    {
                        int quadrantCount = QuadrantUtility.ClampCount(GameSettings.numberOfQuadrants);
                        int chestQuadrant = QuadrantUtility.GetQuadrant(chestPos, quadrantCount);

                        List<int> allowedQuadrants = new List<int>();
                        for (int q = 1; q <= quadrantCount; q++)
                        {
                            if (q != chestQuadrant)
                            {
                                allowedQuadrants.Add(q);
                            }
                        }

                        float sectorSize = 360f / quadrantCount;
                        float chosenAngle;

                        if (allowedQuadrants.Count == 0)
                        {
                            chosenAngle = Random.Range(0f, 360f);
                        }
                        else
                        {
                            int chosenQuadrant = allowedQuadrants[Random.Range(0, allowedQuadrants.Count)];
                            float angleMin = (chosenQuadrant - 1) * sectorSize;
                            float angleMax = chosenQuadrant * sectorSize;
                            chosenAngle = Random.Range(angleMin, angleMax);
                        }
                        float rad = chosenAngle * Mathf.Deg2Rad;
                        Vector3 spawnPosition = new Vector3(Mathf.Cos(rad), 0, Mathf.Sin(rad)) * (trialDef.circleRadius);
                        spawnPosition.y = 1f; // Offset to avoid being underground
                        spawnPosition.x = Mathf.Clamp(spawnPosition.x, -trialDef.circleRadius * 0.7f, trialDef.circleRadius * 0.7f);
                        spawnPosition.z = Mathf.Clamp(spawnPosition.z, -trialDef.circleRadius * 0.7f, trialDef.circleRadius * 0.7f);
                        return spawnPosition;
                    }
                }
            case StartingLocationOption.Circle:
                {
                    // For the circle option, use a custom starting position if provided;
                    // otherwise, default to a point on the circle (e.g., at (circleRadius, 0, 0)).
                    if (trialDef.customStartingPosition != Vector3.zero)
                        return trialDef.customStartingPosition;
                    else
                        return new Vector3(trialDef.circleRadius, 0, 0);
                }
            default:
                return Vector3.zero;
        }
    }
    IEnumerator StartNextTrialWithDelay(float delay)
    {
        // Optional: Show inter-trial interval UI
        // uiManager.ShowInterTrialInterval(currentTrial);
        if (screenFadeImage != null)
        {
            StartCoroutine(FadeScreen(fadeToBlack: false, duration: 1f));
        }

        yield return new WaitForSeconds(delay);

        StartNextTrial();
    }

    void ResetTimeUpPanel()
    {
        if (timeUpPanel == null) return;
        timeUpPanel.SetActive(false);
    }

    IEnumerator TimeUpSequence()
    {
        float displaySeconds = Mathf.Max(0f, timeUpDisplaySeconds);
        Vector3 fixedPosition = playerController != null ? playerController.transform.position : Vector3.zero;

        if (playerController != null)
        {
            playerController.UnfreezePlayer();
        }

        if (timeUpPanel != null)
        {
            timeUpPanel.SetActive(true);
        }

        if (displaySeconds > 0f)
        {
            float remainingSeconds = displaySeconds;
            while (remainingSeconds > 0f)
            {
                if (playerController != null)
                {
                    playerController.transform.position = fixedPosition;
                }
                remainingSeconds -= Time.deltaTime;
                yield return null;
            }
        }

        if (timeUpPanel != null)
        {
            timeUpPanel.SetActive(false);
        }

        currentTrial++;
        StartCoroutine(StartNextTrialWithDelay(0.001f));
    }

    void OnDestroy()
    {
        // Unsubscribe from events to prevent memory leaks
        treasureChestManager.OnChestFound -= OnChestFound;
    }

    void SpawnLightBeamEffect()
    {
        if (lightBeamPrefab == null || playerController == null) return;

        Vector3 playerPos = playerController.transform.position;
        Vector3 spawnPos = new Vector3(playerPos.x, playerPos.y, playerPos.z);

        GameObject beam = Instantiate(lightBeamPrefab, spawnPos, Quaternion.identity);
        beam.transform.SetParent(playerController.transform); // optional: follow player
        Destroy(beam, 10f); // clean up
    }

    IEnumerator FadeScreen(bool fadeToBlack, float duration)
    {
        float startAlpha = fadeToBlack ? 0f : 1f;
        float endAlpha = fadeToBlack ? 1f : 0f;

        Color color = screenFadeImage.color;
        float time = 0f;

        while (time < duration)
        {
            float t = time / duration;
            color.a = Mathf.Lerp(startAlpha, endAlpha, t);
            screenFadeImage.color = color;
            time += Time.deltaTime;
            yield return null;
        }

        // Ensure exact final alpha
        color.a = endAlpha;
        screenFadeImage.color = color;
    }

    public void QuitExperiment()
    {
        Debug.Log("Quitting experiment...");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
