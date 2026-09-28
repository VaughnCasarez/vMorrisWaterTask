using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using System.IO;
using System.Collections;

public class TrialEditorController : MonoBehaviour
{
    // Hard bounds (override any serialized inspector values)
    private const float HardMinCueOffset = 2f;
    private const float HardMaxCueOffset = 50f;
    [Header("UI References")]
    public TMP_Text trialLabel;
    public TMP_InputField radiusInput;
    public TMP_InputField cuesInput;
    public TMP_InputField quadrantsInput;
    public TMP_InputField timeLimitInput;
    public TMP_Dropdown trialTypeDropdown;
    public Button saveButton;
    public Button loadButton;
    public TMP_InputField fileNameInput;

    [Header("Cue Offset UI")]
    public Slider cueDistanceSlider;      // 0.5..10 (assign in Inspector)
    public TMP_Text cueDistanceValueText; // shows "X.X m"
    public float cueDistanceMinMeters = HardMinCueOffset; // not relied upon at runtime
    public float cueDistanceMaxMeters = HardMaxCueOffset;  // not relied upon at runtime
    public float cueMarkerUiEdgeScale = 0.55f; // fraction of UI radius for blue markers


    public TMP_Text statusText;

    public TMP_Dropdown savedFilesDropdown; // assign via Inspector


    public TMP_Dropdown startingLocationDropdown;
    public TMP_Dropdown cuePlacementDropdown;
    public TMP_Dropdown chestPlacementDropdown;
    public Toggle distalCueToggle;

    public RectTransform cueMarkerContainer; // Parent for blue cue markers

    public RectTransform startingLocationCircle; // The main clickable black circle
    public RectTransform selectedMarker;      // Green dot for start (ensure assigned)
    public RectTransform cueMarkerPrefab;     // Blue dot for cues (ensure assigned)
    public RectTransform chestMarkerPrefab;   // Gold dot for chest (ensure assigned)
    private GameObject chestMarkerInstance; // UI instance for the gold treasure marker

    public Button prevButton;
    public Button nextButton;
    public Button doneButton;
    public Button copyToAllButton;
    public Toggle[] cueToggles; // Assign 8 toggles in the Inspector
    [Header("Exit Button")]
    public Button quitButton;
    public Vector2 quitButtonOffset = new Vector2(-12f, -12f);
    public Vector2 quitButtonSize = new Vector2(36f, 36f);

    // Starting location dropdown mapping: show Auto first without changing enum values
    private readonly StartingLocationOption[] startingLocOrder = new[]
    {
        StartingLocationOption.Auto,
        StartingLocationOption.Randomize,
        StartingLocationOption.Circle
    };
    private readonly List<string> startingLocLabels = new List<string>
    {
        "Auto",
        "Randomize",
        "Manual"
    };

    public string nextSceneName = "TrialScene";
    private int currentTrialIndex = 0;

    void Start()
    {
        // Ensure all marker prefabs are actually assigned
        if (selectedMarker == null) Debug.LogError("SelectedMarker (Green Dot) is not assigned in Inspector!");
        if (cueMarkerPrefab == null) Debug.LogError("CueMarkerPrefab (Blue Dot) is not assigned in Inspector!");
        if (chestMarkerPrefab == null) Debug.LogError("ChestMarkerPrefab (Gold Dot) is not assigned in Inspector!");
        if (startingLocationCircle == null) Debug.LogError("StartingLocationCircle is not assigned!");
        if (cueMarkerContainer == null) Debug.LogError("CueMarkerContainer is not assigned!");

        if (cueDistanceSlider != null)
        {
            // Force hard bounds regardless of serialized values
            cueDistanceSlider.minValue = HardMinCueOffset;
            cueDistanceSlider.maxValue = HardMaxCueOffset;
            cueDistanceSlider.onValueChanged.AddListener(OnCueDistanceChanged);
        }


        RefreshUIFromData();
        UpdateButtonInteractables();

        prevButton.onClick.AddListener(OnClickPrev);
        nextButton.onClick.AddListener(OnClickNext);
        doneButton.onClick.AddListener(OnClickDone);
        copyToAllButton.onClick.AddListener(OnCopyToAll);
        saveButton.onClick.AddListener(SaveSettingsToFile);
        loadButton.onClick.AddListener(OnLoadFromDropdown);
        cuesInput.onEndEdit.AddListener(OnNumberOfCuesChanged);
        if (quadrantsInput != null)
        {
            quadrantsInput.onEndEdit.AddListener(OnNumberOfQuadrantsChanged);
        }


        distalCueToggle.isOn = GameSettings.enableDistalCues;

        // Rebuild starting location dropdown to desired order (Auto first)
        if (startingLocationDropdown != null)
        {
            startingLocationDropdown.ClearOptions();
            startingLocationDropdown.AddOptions(startingLocLabels);
        }

        startingLocationDropdown.onValueChanged.AddListener((_) => OnStartingLocationOptionChanged());
        cuePlacementDropdown.onValueChanged.AddListener((_) => OnCuePlacementOptionChanged());
        chestPlacementDropdown.onValueChanged.AddListener((_) => OnChestPlacementOptionChanged());

        PopulateSavedFilesDropdown();

        foreach (Toggle toggle in cueToggles)
        {
            toggle.onValueChanged.AddListener(delegate { EnforceCueLimit(); });
        }

        SetupQuitButton();
    }

    public void OnClickPrev() { SaveUIIntoData(); currentTrialIndex--; RefreshUIFromData(); UpdateButtonInteractables(); }
    public void OnClickNext() { SaveUIIntoData(); currentTrialIndex++; RefreshUIFromData(); UpdateButtonInteractables(); }
    public void OnClickDone()
    {
        SaveUIIntoData();
        if (GameSettings.allTrials != null && GameSettings.allTrials.Length > 0)
        {
            TrialDefinition firstTrial = GameSettings.allTrials[0];
            GameSettings.circleRadius = firstTrial.circleRadius;
            GameSettings.numberOfProximalCues = firstTrial.numberOfProximalCues;
            GameSettings.numberOfQuadrants = firstTrial.numberOfQuadrants;
            GameSettings.trialType = firstTrial.trialType;
            GameSettings.timeLimit = firstTrial.timeLimit;
        }
        GameSettings.enableDistalCues = distalCueToggle.isOn;
        SceneManager.LoadScene(nextSceneName);
    }
    void OnCopyToAll()
    {
        SaveUIIntoData();
        if (GameSettings.allTrials == null || GameSettings.allTrials.Length == 0 || currentTrialIndex >= GameSettings.allTrials.Length) return;
        TrialDefinition currentSettings = GameSettings.allTrials[currentTrialIndex];
        for (int i = 0; i < GameSettings.allTrials.Length; i++)
        {
            if (i == currentTrialIndex) continue;
            GameSettings.allTrials[i] = new TrialDefinition // Create a new instance or deep copy
            {
                circleRadius = currentSettings.circleRadius,
                numberOfProximalCues = currentSettings.numberOfProximalCues,
                numberOfQuadrants = currentSettings.numberOfQuadrants,
                trialType = currentSettings.trialType,
                timeLimit = currentSettings.timeLimit,
                startingLocationOption = currentSettings.startingLocationOption,
                customStartingPosition = currentSettings.customStartingPosition,
                cuePlacementOption = currentSettings.cuePlacementOption,
                customCuePositions = new List<Vector3>(currentSettings.customCuePositions),
                chestPlacementOption = currentSettings.chestPlacementOption,
                customTreasureChestPosition = currentSettings.customTreasureChestPosition,
                cueSelections = (bool[])currentSettings.cueSelections.Clone()
            };
        }
        RefreshUIFromData();
    }

    void RefreshUIFromData()
    {
        if (GameSettings.allTrials == null || GameSettings.allTrials.Length == 0 || currentTrialIndex >= GameSettings.allTrials.Length)
        {
            // Consider disabling UI or showing an error
            Debug.LogWarning("RefreshUIFromData: Trial data invalid or index out of bounds.");
            return;
        }
        TrialDefinition td = GameSettings.allTrials[currentTrialIndex];

        if (cueDistanceSlider != null)
        {
            // Clamp and reflect hard bounds
            float clamped = Mathf.Clamp(td.cueDistanceFromEdge, HardMinCueOffset, HardMaxCueOffset);
            if (Mathf.Abs(clamped - td.cueDistanceFromEdge) > 0.0001f) td.cueDistanceFromEdge = clamped;
            cueDistanceSlider.minValue = HardMinCueOffset;
            cueDistanceSlider.maxValue = HardMaxCueOffset;
            cueDistanceSlider.SetValueWithoutNotify(td.cueDistanceFromEdge);
        }
        UpdateCueDistanceLabel(td.cueDistanceFromEdge);

        trialLabel.text = $"Trial {currentTrialIndex + 1} of {GameSettings.allTrials.Length}";
        radiusInput.text = td.circleRadius.ToString();
        cuesInput.text = td.numberOfProximalCues.ToString();
        if (quadrantsInput != null)
        {
            quadrantsInput.text = td.numberOfQuadrants.ToString();
        }
        timeLimitInput.text = td.timeLimit.ToString();
        trialTypeDropdown.value = (int)td.trialType;
        // Map enum value to dropdown index according to our custom order
        startingLocationDropdown.value = StartingLocToDropdownIndex(td.startingLocationOption);
        cuePlacementDropdown.value = (int)td.cuePlacementOption;
        chestPlacementDropdown.value = (int)td.chestPlacementOption;
        distalCueToggle.isOn = GameSettings.enableDistalCues;

        for (int i = 0; i < cueToggles.Length; i++)
        {
            if (i < td.cueSelections.Length)
                cueToggles[i].isOn = td.cueSelections[i];
        }


        UpdateUIVisibilityAndMarkers(td);
        EnforceCueLimit();
    }

    void UpdateUIVisibilityAndMarkers(TrialDefinition td)
    {
        bool showMainCircle = td.startingLocationOption == StartingLocationOption.Circle ||
                              td.cuePlacementOption == CuePlacementOption.CircleManual ||
                              td.chestPlacementOption == ChestPlacementOption.CircleManual;
        if (startingLocationCircle != null) startingLocationCircle.gameObject.SetActive(showMainCircle);

        // Starting Location Marker (Green)
        if (selectedMarker != null)
        {
            if (td.startingLocationOption == StartingLocationOption.Circle && td.customStartingPosition.magnitude > 0.001f)

            {
                PositionMarkerOnUI(selectedMarker, td.customStartingPosition, td.circleRadius);
                selectedMarker.gameObject.SetActive(true);
            }
            else
            {
                selectedMarker.gameObject.SetActive(false);
            }
        }

        // Cue Markers (Blue)
        RegenerateCueMarkers(td);

        // Treasure Marker (Gold)
        RegenerateTreasureChestMarker(td);
    }

    void SaveUIIntoData()
    {
        if (GameSettings.allTrials == null || GameSettings.allTrials.Length == 0 || currentTrialIndex >= GameSettings.allTrials.Length) return;
        TrialDefinition td = GameSettings.allTrials[currentTrialIndex];

        if (cueDistanceSlider != null)
            td.cueDistanceFromEdge = Mathf.Clamp(cueDistanceSlider.value, HardMinCueOffset, HardMaxCueOffset);


        if (float.TryParse(radiusInput.text, out float rad)) td.circleRadius = rad;
        if (int.TryParse(cuesInput.text, out int nCues)) td.numberOfProximalCues = nCues;
        if (int.TryParse(timeLimitInput.text, out int time)) td.timeLimit = time;
        if (quadrantsInput != null && int.TryParse(quadrantsInput.text, out int quadrantCount))
        {
            td.numberOfQuadrants = QuadrantUtility.ClampCount(quadrantCount);
            if (quadrantCount != td.numberOfQuadrants)
            {
                quadrantsInput.SetTextWithoutNotify(td.numberOfQuadrants.ToString());
            }
        }
        td.trialType = (GameSettings.TrialType)trialTypeDropdown.value;
        // Map dropdown index back to enum value
        td.startingLocationOption = DropdownIndexToStartingLoc(startingLocationDropdown.value);
        td.cuePlacementOption = (CuePlacementOption)cuePlacementDropdown.value;
        td.chestPlacementOption = (ChestPlacementOption)chestPlacementDropdown.value;
        GameSettings.enableDistalCues = distalCueToggle.isOn;

        for (int i = 0; i < cueToggles.Length && i < td.cueSelections.Length; i++)
        {
            td.cueSelections[i] = cueToggles[i].isOn;
        }
    }

    void OnStartingLocationOptionChanged() { if (GameSettings.allTrials == null || GameSettings.allTrials.Length <= currentTrialIndex) return; GameSettings.allTrials[currentTrialIndex].startingLocationOption = DropdownIndexToStartingLoc(startingLocationDropdown.value); UpdateUIVisibilityAndMarkers(GameSettings.allTrials[currentTrialIndex]); }

    int StartingLocToDropdownIndex(StartingLocationOption opt)
    {
        for (int i = 0; i < startingLocOrder.Length; i++)
        {
            if (startingLocOrder[i] == opt) return i;
        }
        return 0;
    }

    StartingLocationOption DropdownIndexToStartingLoc(int idx)
    {
        if (idx >= 0 && idx < startingLocOrder.Length) return startingLocOrder[idx];
        return startingLocOrder[0];
    }
    void OnCuePlacementOptionChanged() { if (GameSettings.allTrials == null || GameSettings.allTrials.Length <= currentTrialIndex) return; GameSettings.allTrials[currentTrialIndex].cuePlacementOption = (CuePlacementOption)cuePlacementDropdown.value; UpdateUIVisibilityAndMarkers(GameSettings.allTrials[currentTrialIndex]); }
    void OnChestPlacementOptionChanged() { if (GameSettings.allTrials == null || GameSettings.allTrials.Length <= currentTrialIndex) return; GameSettings.allTrials[currentTrialIndex].chestPlacementOption = (ChestPlacementOption)chestPlacementDropdown.value; UpdateUIVisibilityAndMarkers(GameSettings.allTrials[currentTrialIndex]); }

    void UpdateButtonInteractables() { prevButton.interactable = currentTrialIndex > 0; nextButton.interactable = currentTrialIndex < (GameSettings.allTrials.Length - 1); doneButton.interactable = GameSettings.allTrials != null && GameSettings.allTrials.Length > 0; }

    // Called by MainCircleClickHandler
    public void OnMainCircleClicked(PointerEventData eventData)
    {
        if (GameSettings.allTrials == null || GameSettings.allTrials.Length == 0 || currentTrialIndex >= GameSettings.allTrials.Length) return;
        TrialDefinition td = GameSettings.allTrials[currentTrialIndex];

        Vector2 localPoint; // Point within startingLocationCircle's RectTransform
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(startingLocationCircle, eventData.position, eventData.pressEventCamera, out localPoint))
        {
            float uiActualRadius = startingLocationCircle.rect.width * 0.5f; // Actual radius of the UI circle image
            Vector2 normalizedDirection = localPoint.normalized; // Direction from center to click

            if (td.startingLocationOption == StartingLocationOption.Circle)
            {
                // Starting location can be anywhere *within* the circle, clamped to edge
                Vector2 clampedLocalPoint = localPoint;
                if (localPoint.magnitude > uiActualRadius)
                {
                    clampedLocalPoint = normalizedDirection * uiActualRadius;
                }
                // Experimental position scales the clamped UI position
                td.customStartingPosition = new Vector3(clampedLocalPoint.x / uiActualRadius * td.circleRadius, 0, clampedLocalPoint.y / uiActualRadius * td.circleRadius);
                if (selectedMarker != null)
                {
                    selectedMarker.anchoredPosition = clampedLocalPoint; // Position green dot
                    selectedMarker.gameObject.SetActive(true);
                }
            }
            else if (td.cuePlacementOption == CuePlacementOption.CircleManual)
            {
                PlaceCueOnCircleEdge(normalizedDirection, uiActualRadius, td);
            }
            else if (td.chestPlacementOption == ChestPlacementOption.CircleManual)
            {
                PlaceTreasureChestOnCircle(localPoint, uiActualRadius, td);
            }
        }
    }

    // Helper to position a UI marker (RectTransform) based on world experimental position and radius
    void PositionMarkerOnUI(RectTransform marker, Vector3 experimentalPosition, float experimentalRadius)
    {
        if (marker == null || startingLocationCircle == null) return;
        float uiActualRadius = startingLocationCircle.rect.width * 0.5f;
        if (experimentalRadius == 0) experimentalRadius = 0.001f; // Avoid div by zero

        // Scale factor: how much smaller/larger the UI representation is compared to experimental
        float scaleFactorExpToUi = uiActualRadius / experimentalRadius;

        // Position in UI coordinate system (anchoredPosition for the marker)
        // Assumes marker's pivot is center and it's a child of something aligned with startingLocationCircle
        marker.anchoredPosition = new Vector2(experimentalPosition.x * scaleFactorExpToUi, experimentalPosition.z * scaleFactorExpToUi);
        marker.gameObject.SetActive(true);
    }

    void PlaceCueOnCircleEdge(Vector2 normalizedDirection, float uiActualRadius, TrialDefinition td)
    {
        if (td.customCuePositions.Count >= td.numberOfProximalCues) return;

        // Compute desired experimental radius: outside the boundary by cueDistanceFromEdge
        float desiredRadius = Mathf.Max(0f, td.circleRadius + td.cueDistanceFromEdge);

        // World/experimental position for this cue along the chosen direction
        Vector3 worldPos = new Vector3(
            normalizedDirection.x * desiredRadius,
            0f,
            normalizedDirection.y * desiredRadius
        );
        td.customCuePositions.Add(worldPos);

        // Place UI marker on the edge of the UI circle regardless of offset
        if (cueMarkerPrefab != null && cueMarkerContainer != null)
        {
            RectTransform newMarker = Instantiate(cueMarkerPrefab, cueMarkerContainer);
            float edgeScale = Mathf.Clamp01(cueMarkerUiEdgeScale);
            newMarker.anchoredPosition = normalizedDirection * uiActualRadius * edgeScale;
            newMarker.gameObject.SetActive(true);
        }
    }

    void RegenerateCueMarkers(TrialDefinition td)
    {
        if (cueMarkerContainer == null) return;
        for (int i = cueMarkerContainer.childCount - 1; i >= 0; i--) { Destroy(cueMarkerContainer.GetChild(i).gameObject); }

        cueMarkerContainer.gameObject.SetActive(td.cuePlacementOption == CuePlacementOption.CircleManual);

        if (td.cuePlacementOption == CuePlacementOption.CircleManual && cueMarkerPrefab != null)
        {
            float desiredRadius = Mathf.Max(0f, td.circleRadius + td.cueDistanceFromEdge);

            for (int i = 0; i < td.customCuePositions.Count; i++)
            {
                Vector3 p = td.customCuePositions[i];
                Vector2 dirXZ = new Vector2(p.x, p.z);
                if (dirXZ.sqrMagnitude < 1e-6f) continue;
                Vector2 dir = dirXZ.normalized;

                // Recompute position at the current desired radius
                Vector3 adjustedWorld = new Vector3(dir.x * desiredRadius, 0f, dir.y * desiredRadius);

                // Update stored position so runtime consumers get correct positions
                td.customCuePositions[i] = adjustedWorld;

                RectTransform newMarker = Instantiate(cueMarkerPrefab, cueMarkerContainer);
                // Keep UI marker on scaled circle edge using only direction
                float uiActualRadius = startingLocationCircle != null ? startingLocationCircle.rect.width * 0.5f : 0f;
                float edgeScale = Mathf.Clamp01(cueMarkerUiEdgeScale);
                newMarker.anchoredPosition = dir * uiActualRadius * edgeScale;
                newMarker.gameObject.SetActive(true);
            }
        }
    }

    public void ClearCueMarkers()
    {
        if (GameSettings.allTrials == null || GameSettings.allTrials.Length <= currentTrialIndex) return;
        TrialDefinition td = GameSettings.allTrials[currentTrialIndex];
        td.customCuePositions.Clear();
        RegenerateCueMarkers(td);
    }

    void PlaceTreasureChestOnCircle(Vector2 localPoint, float uiActualRadius, TrialDefinition td)
    {
        Vector2 clampedLocalPoint = localPoint;
        if (localPoint.magnitude > uiActualRadius)
        {
            clampedLocalPoint = localPoint.normalized * uiActualRadius;
        }

        td.customTreasureChestPosition = new Vector3(
            clampedLocalPoint.x / uiActualRadius * td.circleRadius,
            0,
            clampedLocalPoint.y / uiActualRadius * td.circleRadius
        );

        RegenerateTreasureChestMarker(td);
    }

    void RegenerateTreasureChestMarker(TrialDefinition td)
    {
        if (chestMarkerInstance != null) { Destroy(chestMarkerInstance); chestMarkerInstance = null; }

        if (td.chestPlacementOption == ChestPlacementOption.CircleManual && td.customTreasureChestPosition != Vector3.zero && chestMarkerPrefab != null)
        {
            // Ensure chestMarkerPrefab's parent is appropriate for anchoredPosition.
            // Often, this is the same parent as startingLocationCircle or startingLocationCircle itself.
            // For this example, let's assume chestMarkerPrefab should be a child of startingLocationCircle for positioning.
            // If selectedMarker is child of startingLocationCircle, this is consistent.
            Transform parentForMarker = startingLocationCircle.transform; // Or another designated container.
            chestMarkerInstance = Instantiate(chestMarkerPrefab, parentForMarker).gameObject;

            PositionMarkerOnUI(chestMarkerInstance.GetComponent<RectTransform>(), td.customTreasureChestPosition, td.circleRadius);
            chestMarkerInstance.SetActive(true);
        }
    }

    void SaveSettingsToFile()
    {
        SaveUIIntoData();

        ExperimentSettingsData data = new ExperimentSettingsData
        {
            participantID = GameSettings.participantID,
            numberOfTrials = GameSettings.numberOfTrials,
            enableDistalCues = GameSettings.enableDistalCues,
            allTrials = GameSettings.allTrials
        };

        string customName = fileNameInput != null ? fileNameInput.text.Trim() : "";
        string baseName = string.IsNullOrEmpty(customName)
            ? $"exp_"
            : customName;

        ExperimentSettingsStorage.EnsureDirectoryExists();
        string path = ExperimentSettingsStorage.GetFilePath(baseName + ".json");

        File.WriteAllText(path, JsonUtility.ToJson(data, true));
        Debug.Log("Settings saved to: " + path);

        ShowStatusText($"Saved settings as: {Path.GetFileName(path)}");
        PopulateSavedFilesDropdown();
    }

    void ShowStatusText(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
            CancelInvoke(nameof(ClearStatusText));
            Invoke(nameof(ClearStatusText), 3f); // Clear after 3 seconds
        }
    }

    void ClearStatusText()
    {
        if (statusText != null)
        {
            statusText.text = "";
        }
    }
    void PopulateSavedFilesDropdown()
    {
        if (savedFilesDropdown == null) return;

        string[] files = ExperimentSettingsStorage.GetFiles();
        savedFilesDropdown.ClearOptions();
        List<string> options = new List<string>();

        foreach (string file in files)
        {
            options.Add(Path.GetFileName(file));
        }

        savedFilesDropdown.AddOptions(options);
    }

    public void OnLoadFromDropdown()
    {
        if (savedFilesDropdown == null || savedFilesDropdown.options.Count == 0) return;

        string selectedFile = savedFilesDropdown.options[savedFilesDropdown.value].text;
        string path = ExperimentSettingsStorage.GetFilePath(selectedFile);

        if (!File.Exists(path)) return;

        ExperimentSettingsData data = JsonUtility.FromJson<ExperimentSettingsData>(File.ReadAllText(path));
        GameSettings.participantID = data.participantID;
        GameSettings.allTrials = data.allTrials ?? new TrialDefinition[0];
        GameSettings.numberOfTrials = GameSettings.allTrials.Length;
        GameSettings.enableDistalCues = data.enableDistalCues;
        distalCueToggle.isOn = GameSettings.enableDistalCues;
        currentTrialIndex = 0;

        RefreshUIFromData();
        UpdateButtonInteractables();

        ShowStatusText($"Loaded settings from: {selectedFile}");
    }

    void OnNumberOfCuesChanged(string newValue)
    {
        if (!int.TryParse(newValue, out int newNum)) return;

        if (GameSettings.allTrials == null || currentTrialIndex >= GameSettings.allTrials.Length)
            return;

        var td = GameSettings.allTrials[currentTrialIndex];
        td.numberOfProximalCues = newNum;

        // Clear custom cue positions if using manual cue placement
        if (td.cuePlacementOption == CuePlacementOption.CircleManual)
        {

            td.customCuePositions.Clear();
            RegenerateCueMarkers(td);
        }
    }

    void OnNumberOfQuadrantsChanged(string newValue)
    {
        if (!int.TryParse(newValue, out int newNum))
        {
            return;
        }

        int clamped = QuadrantUtility.ClampCount(newNum);

        if (GameSettings.allTrials == null || currentTrialIndex >= GameSettings.allTrials.Length)
            return;

        var td = GameSettings.allTrials[currentTrialIndex];
        td.numberOfQuadrants = clamped;

        if (quadrantsInput != null && clamped != newNum)
        {
            quadrantsInput.SetTextWithoutNotify(clamped.ToString());
        }
    }

    void EnforceCueLimit()
    {
        if (GameSettings.allTrials == null || currentTrialIndex >= GameSettings.allTrials.Length)
            return;

        TrialDefinition td = GameSettings.allTrials[currentTrialIndex];
        int limit = td.numberOfProximalCues;

        int selected = 0;
        foreach (Toggle toggle in cueToggles)
        {
            if (toggle.isOn) selected++;
        }

        bool allowMore = selected < limit;
        foreach (Toggle toggle in cueToggles)
        {
            // Disable only if not already selected
            toggle.interactable = allowMore || toggle.isOn;
        }
    }

    void OnCueDistanceChanged(float newVal)
    {
        if (GameSettings.allTrials == null || currentTrialIndex >= GameSettings.allTrials.Length) return;
        var td = GameSettings.allTrials[currentTrialIndex];
        float clampedVal = Mathf.Clamp(newVal, HardMinCueOffset, HardMaxCueOffset);
        if (Mathf.Abs(clampedVal - newVal) > 0.0001f && cueDistanceSlider != null)
        {
            cueDistanceSlider.SetValueWithoutNotify(clampedVal); // snap handle to valid range
        }
        td.cueDistanceFromEdge = clampedVal;
        UpdateCueDistanceLabel(clampedVal);

        // Rescale existing manual cue positions to the new radius while preserving angles
        if (td.cuePlacementOption == CuePlacementOption.CircleManual && td.customCuePositions != null)
        {
            float desiredRadius = Mathf.Max(0f, td.circleRadius + td.cueDistanceFromEdge);
            for (int i = 0; i < td.customCuePositions.Count; i++)
            {
                Vector3 p = td.customCuePositions[i];
                Vector2 dirXZ = new Vector2(p.x, p.z);
                if (dirXZ.sqrMagnitude < 1e-6f) continue;
                Vector2 dir = dirXZ.normalized;
                td.customCuePositions[i] = new Vector3(dir.x * desiredRadius, 0f, dir.y * desiredRadius);
            }
            RegenerateCueMarkers(td);
        }
    }

    void UpdateCueDistanceLabel(float meters)
    {
        if (cueDistanceValueText != null)
            cueDistanceValueText.text = $"Cue offset: {meters:0.0} m";
    }

    void SetupQuitButton()
    {
        if (quitButton == null)
        {
            Canvas canvas = FindRootCanvas();
            if (canvas == null)
            {
                Debug.LogWarning("No Canvas found for quit button.");
                return;
            }
            quitButton = CreateQuitButton(canvas.transform);
        }

        quitButton.onClick.RemoveListener(QuitExperiment);
        quitButton.onClick.AddListener(QuitExperiment);
    }

    Canvas FindRootCanvas()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>();
        foreach (Canvas canvas in canvases)
        {
            if (canvas.isRootCanvas)
            {
                return canvas;
            }
        }
        return canvases.Length > 0 ? canvases[0] : null;
    }

    Button CreateQuitButton(Transform parent)
    {
        GameObject buttonObject = new GameObject("QuitButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.layer = parent.gameObject.layer;
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = quitButtonSize;
        rect.anchoredPosition = quitButtonOffset;

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.6f);

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelObject.layer = parent.gameObject.layer;
        labelObject.transform.SetParent(buttonObject.transform, false);

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        Text label = labelObject.GetComponent<Text>();
        label.text = "X";
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.fontSize = 24;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.raycastTarget = false;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        return button;
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
