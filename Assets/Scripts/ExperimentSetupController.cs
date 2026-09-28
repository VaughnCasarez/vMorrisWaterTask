using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;

public class ExperimentSetupController : MonoBehaviour
{
    [Header("Mode Toggles")]
    public Toggle newExperimentToggle;     // “Set up New Experiment”
    public Toggle runParticipantToggle;    // “Run a Participant”

    [Header("New Experiment UI (shown only when New is selected)")]
    public TMP_InputField numTrialsInput;  // Number of Trials

    [Header("Run Participant UI (shown only when Run is selected)")]
    public TMP_InputField participantIdInput;  // Participant ID (Run only)
    public TMP_Dropdown savedFilesDropdown;    // List *.json from StreamingAssets/ExperimentSettings

    [Header("Buttons")]
    public Button nextButton;
    public Button deleteButton;
    public TMP_Text nextButtonLabel;   // <- assign the label (child TMP_Text of the button)

    [Header("Exit Button")]
    public Button quitButton;
    public Vector2 quitButtonOffset = new Vector2(-12f, -12f);
    public Vector2 quitButtonSize = new Vector2(36f, 36f);

    [Header("Scene Names")]
    public string trialEditorSceneName = "TrialEditorScene"; // Configure settings
    public string runSceneName = "TrialScene";               // Run sequence

    void Start()
    {
        // Default to New Experiment
        if (newExperimentToggle != null) newExperimentToggle.isOn = true;
        if (runParticipantToggle != null) runParticipantToggle.isOn = false;

        // Wire events
        if (newExperimentToggle != null) newExperimentToggle.onValueChanged.AddListener(_ => UpdatePanels());
        if (runParticipantToggle != null) runParticipantToggle.onValueChanged.AddListener(_ => UpdatePanels());
        if (nextButton != null) nextButton.onClick.AddListener(OnClickNext);

        PopulateSavedFilesDropdown();
        SetupDeleteButton();
        UpdatePanels();
        SetupQuitButton();
    }

    void UpdatePanels()
    {
        bool isNew = newExperimentToggle == null || newExperimentToggle.isOn;

        // New Experiment section
        if (numTrialsInput != null)
            numTrialsInput.transform.parent.gameObject.SetActive(isNew);

        // Run Participant section
        bool showRun = !isNew;
        if (participantIdInput != null)
            participantIdInput.transform.parent.gameObject.SetActive(showRun);
        if (savedFilesDropdown != null)
            savedFilesDropdown.transform.parent.gameObject.SetActive(showRun);
        if (deleteButton != null)
            deleteButton.gameObject.SetActive(showRun);

        // Update button label
        if (nextButtonLabel != null)
            nextButtonLabel.text = isNew ? "Edit Trials..." : "Run...";
    }


    void PopulateSavedFilesDropdown()
    {
        if (savedFilesDropdown == null) return;

        savedFilesDropdown.ClearOptions();
        var files = ExperimentSettingsStorage.GetFiles();
        var options = new List<string>();
        foreach (var f in files) options.Add(Path.GetFileName(f));
        if (options.Count == 0) options.Add("(no saved experiments found)");
        savedFilesDropdown.AddOptions(options);
        savedFilesDropdown.value = 0;
        savedFilesDropdown.RefreshShownValue();
    }

    void SetupDeleteButton()
    {
        if (deleteButton == null && nextButton != null)
        {
            GameObject deleteButtonObject = Instantiate(nextButton.gameObject, nextButton.transform.parent);
            deleteButtonObject.name = "DeleteSettingsButton";
            deleteButton = deleteButtonObject.GetComponent<Button>();

            RectTransform nextRect = nextButton.GetComponent<RectTransform>();
            RectTransform deleteRect = deleteButtonObject.GetComponent<RectTransform>();
            deleteRect.anchoredPosition = nextRect.anchoredPosition + Vector2.right * (nextRect.rect.width + 8f);

            TMP_Text label = deleteButtonObject.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = "Delete";
        }

        if (deleteButton != null)
        {
            deleteButton.onClick.RemoveListener(DeleteSelectedSettings);
            deleteButton.onClick.AddListener(DeleteSelectedSettings);
        }
    }

    public void DeleteSelectedSettings()
    {
        if (savedFilesDropdown == null || savedFilesDropdown.options.Count == 0)
        {
            Debug.LogWarning("No saved settings selected.");
            return;
        }

        string selectedFile = savedFilesDropdown.options[savedFilesDropdown.value].text;
        if (string.IsNullOrEmpty(selectedFile) || selectedFile.StartsWith("("))
        {
            Debug.LogWarning("No saved settings selected.");
            return;
        }

        string path = ExperimentSettingsStorage.GetFilePath(selectedFile);
        if (!File.Exists(path))
        {
            Debug.LogWarning("Saved settings file not found: " + path);
            PopulateSavedFilesDropdown();
            return;
        }

        ExperimentSettingsStorage.DeleteFile(selectedFile);
        PopulateSavedFilesDropdown();
        Debug.Log("Deleted settings: " + selectedFile);
    }

    public void OnClickNext()
    {
        bool isNew = newExperimentToggle == null || newExperimentToggle.isOn;

        if (isNew)
        {
            // === New Experiment → define #trials and go configure ===
            if (!int.TryParse(numTrialsInput != null ? numTrialsInput.text : "1", out int nTrials))
                nTrials = 1;

            GameSettings.numberOfTrials = Mathf.Max(1, nTrials);
            GameSettings.allTrials = new TrialDefinition[GameSettings.numberOfTrials];
            for (int i = 0; i < GameSettings.numberOfTrials; i++)
                GameSettings.allTrials[i] = new TrialDefinition();

            // Seed some defaults for the editor preview if you want
            if (GameSettings.allTrials.Length > 0)
            {
                var first = GameSettings.allTrials[0];
                GameSettings.circleRadius = first.circleRadius;
                GameSettings.numberOfProximalCues = first.numberOfProximalCues;
                GameSettings.numberOfQuadrants = first.numberOfQuadrants;
                GameSettings.trialType = first.trialType;
                GameSettings.timeLimit = first.timeLimit;
            }

            SceneManager.LoadScene(trialEditorSceneName);
        }
        else
        {
            // === Run Participant → set participant ID, load saved experiment, jump to run ===
            if (!int.TryParse(participantIdInput != null ? participantIdInput.text : "0", out int pid))
                pid = 0;
            GameSettings.participantID = pid;

            if (savedFilesDropdown == null || savedFilesDropdown.options.Count == 0)
            {
                Debug.LogWarning("No saved file selected.");
                return;
            }

            string chosen = savedFilesDropdown.options[savedFilesDropdown.value].text;
            if (string.IsNullOrEmpty(chosen) || chosen.StartsWith("("))
            {
                Debug.LogWarning("No valid saved experiment found.");
                return;
            }

            string path = ExperimentSettingsStorage.GetFilePath(chosen);
            if (!File.Exists(path))
            {
                Debug.LogWarning("Saved experiment file not found: " + path);
                return;
            }

            var data = JsonUtility.FromJson<ExperimentSettingsData>(File.ReadAllText(path));

            // Load trials/settings from file
            GameSettings.allTrials = data.allTrials ?? new TrialDefinition[0];
            GameSettings.numberOfTrials = GameSettings.allTrials.Length > 0 ? GameSettings.allTrials.Length : 1;
            GameSettings.enableDistalCues = data.enableDistalCues;

            // Seed global defaults from first trial (for runtime components that consult GameSettings)
            if (GameSettings.allTrials.Length > 0)
            {
                var first = GameSettings.allTrials[0];
                GameSettings.circleRadius = first.circleRadius;
                GameSettings.numberOfProximalCues = first.numberOfProximalCues;
                GameSettings.numberOfQuadrants = first.numberOfQuadrants;
                GameSettings.trialType = first.trialType;
                GameSettings.timeLimit = first.timeLimit;
            }

            SceneManager.LoadScene(runSceneName);
        }
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
