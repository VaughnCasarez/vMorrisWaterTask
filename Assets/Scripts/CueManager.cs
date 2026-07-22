using System.Collections.Generic;
using UnityEngine;

public class CueManager : MonoBehaviour
{
    [Header("Cue References")]
    [Tooltip("Assign all eight distinct cue GameObjects here.")]
    public GameObject[] allCues; // Array to hold the eight cues
    private float radius;

    [Tooltip("Assign a light prefab to illuminate proximal cues.")]
    public GameObject cueLightPrefab;

    void Start()
    {
        // Initialize cues based on the initial number of proximal cues
        UpdateCues(GameSettings.numberOfProximalCues);
        radius = GameSettings.circleRadius + 3;
    }

    /// <summary>
    /// Updates the active cues based on the specified number, positioning them evenly.
    /// </summary>
    /// <param name="numberOfCues">Number of cues to activate and position.</param>
    public void UpdateCues(int numberOfCues)
    {
        if (allCues == null || allCues.Length == 0)
        {
            Debug.LogWarning("CueManager has no cues assigned.", this);
            return;
        }

        if (GameSettings.allTrials == null || GameSettings.allTrials.Length == 0)
        {
            Debug.LogWarning("GameSettings.allTrials is not initialized.", this);
            return;
        }

        if (GameManager.CurrentTrialIndex < 0 || GameManager.CurrentTrialIndex >= GameSettings.allTrials.Length)
        {
            Debug.LogWarning($"Current trial index {GameManager.CurrentTrialIndex} is out of range.", this);
            return;
        }

        TrialDefinition td = GameSettings.allTrials[GameManager.CurrentTrialIndex];
        bool[] selectedCues = td.cueSelections;

        if (selectedCues == null || selectedCues.Length != allCues.Length)
        {
            Debug.LogWarning("Cue selections not initialized correctly.");
            return;
        }

        List<int> finalCueIndices = new List<int>();

        // Step 1: Add selected cues first
        for (int i = 0; i < selectedCues.Length; i++)
        {
            if (selectedCues[i])
            {
                finalCueIndices.Add(i);
            }
        }

        // Step 2: Add fallback cues if fewer than required
        for (int i = 0; i < selectedCues.Length && finalCueIndices.Count < numberOfCues; i++)
        {
            if (!selectedCues[i] && !finalCueIndices.Contains(i))
            {
                finalCueIndices.Add(i);
            }
        }

        // Step 3: Activate and position only the final selected cues
        for (int i = 0; i < allCues.Length; i++)
        {
            if (allCues[i] == null)
            {
                continue;
            }

            if (finalCueIndices.Contains(i))
            {
                int cueIndexInList = finalCueIndices.IndexOf(i);
                allCues[i].SetActive(true);

                Vector3 cuePosition;

                if (td.customCuePositions != null && cueIndexInList < td.customCuePositions.Count)
                {
                    cuePosition = td.customCuePositions[cueIndexInList];
                }
                else
                {
                    float angle = 360f * cueIndexInList / Mathf.Max(1, numberOfCues);
                    float rad = angle * Mathf.Deg2Rad;
                    float desiredRadius = Mathf.Max(0f, td.circleRadius + td.cueDistanceFromEdge);
                    cuePosition = new Vector3(Mathf.Cos(rad), 0, Mathf.Sin(rad)) * desiredRadius;
                }

                allCues[i].transform.position = cuePosition;
                allCues[i].transform.rotation = Quaternion.LookRotation(-cuePosition.normalized);

                // Handle light
                if (cueLightPrefab != null && allCues[i].transform.Find("CueLight") == null)
                {
                    GameObject cueLight = Instantiate(cueLightPrefab, allCues[i].transform);
                    cueLight.name = "CueLight";
                    cueLight.transform.localPosition = new Vector3(0f, 0.1f, 2f);
                    Vector3 liftedDirection = Vector3.Lerp((Vector3.zero - cueLight.transform.localPosition).normalized, Vector3.up, 0.2f);
                    cueLight.transform.localRotation = Quaternion.LookRotation(liftedDirection);
                }
            }
            else
            {
                allCues[i].SetActive(false);
                Transform light = allCues[i].transform.Find("CueLight");
                if (light != null) Destroy(light.gameObject);
            }
        }
    }


}
