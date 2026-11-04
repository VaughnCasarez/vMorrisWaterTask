using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using VIVE.OpenXR;
using VIVE.OpenXR.EyeTracker;

// Logs every frame of VIVE eye tracker data (gaze, pupil, geometric) to a CSV file that opens in Excel.
// Attach to any active GameObject. File location: Application.persistentDataPath/EyeData_<timestamp>.csv
public class EyeDataLogger : MonoBehaviour
{
    [SerializeField] float flushIntervalSeconds = 1f;

    [Header("Gaze dot / target detection")]
    [SerializeField] bool showGazeDot = true;
    [SerializeField] Color dotColor = Color.red;
    [SerializeField] float dotAngularSizeDegrees = 1.5f;
    [SerializeField] float defaultDotDistance = 3f;
    [SerializeField] float maxRayDistance = 100f;
    [SerializeField] LayerMask raycastMask = ~0;
    [Tooltip("Parent of the headset camera (e.g. XR Origin camera offset). Gaze data is relative to it. Defaults to Camera.main's parent.")]
    [SerializeField] Transform trackingSpace;

    Transform dot;
    string gazeTarget = "";
    StreamWriter writer;
    float lastFlush;
    int row;

    const string Header =
        "row,unity_time_s,frame,utc_time," +
        "L_gaze_valid,L_gaze_x,L_gaze_y,L_gaze_z,L_gaze_qx,L_gaze_qy,L_gaze_qz,L_gaze_qw," +
        "R_gaze_valid,R_gaze_x,R_gaze_y,R_gaze_z,R_gaze_qx,R_gaze_qy,R_gaze_qz,R_gaze_qw," +
        "L_pupil_diam_valid,L_pupil_diameter,L_pupil_pos_valid,L_pupil_x,L_pupil_y," +
        "R_pupil_diam_valid,R_pupil_diameter,R_pupil_pos_valid,R_pupil_x,R_pupil_y," +
        "L_geo_valid,L_openness,L_squeeze,L_wide," +
        "R_geo_valid,R_openness,R_squeeze,R_wide," +
        "gaze_target";

    void Start()
    {
        string path = Path.Combine(Application.persistentDataPath,
            "EyeData_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
        writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine(Header);
        lastFlush = Time.realtimeSinceStartup;
        Debug.Log("EyeDataLogger writing to: " + path);

        if (trackingSpace == null && Camera.main != null) trackingSpace = Camera.main.transform.parent;
        if (showGazeDot) CreateDot();
    }

    void CreateDot()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "GazeDot";
        Destroy(go.GetComponent<Collider>());
        var shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            var mat = new Material(shader) { color = dotColor };
            go.GetComponent<Renderer>().material = mat;
        }
        dot = go.transform;
        dot.gameObject.SetActive(false);
    }

    // Combines valid eyes into one world-space gaze ray, updates the dot and the target name.
    void UpdateGazeTarget(XrSingleEyeGazeDataHTC[] gazes)
    {
        gazeTarget = "";
        bool hasRay = false;
        Vector3 origin = Vector3.zero, dir = Vector3.zero;
        int count = 0;

        if (gazes != null)
        {
            foreach (var eye in new[] { XrEyePositionHTC.XR_EYE_POSITION_LEFT_HTC, XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC })
            {
                if (gazes.Length <= (int)eye || !gazes[(int)eye].isValid) continue;
                var pose = gazes[(int)eye].gazePose;
                origin += pose.position.ToUnityVector();
                dir += pose.orientation.ToUnityQuaternion() * Vector3.forward;
                count++;
            }
        }

        if (count > 0)
        {
            origin /= count;
            dir.Normalize();
            if (trackingSpace != null)
            {
                origin = trackingSpace.TransformPoint(origin);
                dir = trackingSpace.TransformDirection(dir);
            }
            hasRay = true;
        }

        if (!hasRay)
        {
            gazeTarget = "NoValidGaze";
            if (dot != null) dot.gameObject.SetActive(false);
            return;
        }

        Vector3 point = origin + dir * defaultDotDistance;
        if (Physics.Raycast(origin, dir, out RaycastHit hit, maxRayDistance, raycastMask, QueryTriggerInteraction.Ignore))
        {
            gazeTarget = hit.collider.gameObject.name;
            point = hit.point;
        }
        else
        {
            gazeTarget = "None";
        }

        if (dot != null)
        {
            dot.gameObject.SetActive(true);
            dot.position = point;
            float dist = Vector3.Distance(Camera.main != null ? Camera.main.transform.position : origin, point);
            dot.localScale = Vector3.one * (2f * dist * Mathf.Tan(dotAngularSizeDegrees * 0.5f * Mathf.Deg2Rad));
        }
    }

    void Update()
    {
        if (writer == null) return;

        XrSingleEyeGazeDataHTC[] gazes = null;
        XrSingleEyePupilDataHTC[] pupils = null;
        XrSingleEyeGeometricDataHTC[] geos = null;
        bool okGaze = XR_HTC_eye_tracker.Interop.GetEyeGazeData(out gazes);
        bool okPupil = XR_HTC_eye_tracker.Interop.GetEyePupilData(out pupils);
        bool okGeo = XR_HTC_eye_tracker.Interop.GetEyeGeometricData(out geos);

        var sb = new StringBuilder(512);
        sb.Append(row++).Append(',')
          .Append(F(Time.realtimeSinceStartup)).Append(',')
          .Append(Time.frameCount).Append(',')
          .Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

        AppendGaze(sb, okGaze ? gazes : null, XrEyePositionHTC.XR_EYE_POSITION_LEFT_HTC);
        AppendGaze(sb, okGaze ? gazes : null, XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC);
        AppendPupil(sb, okPupil ? pupils : null, XrEyePositionHTC.XR_EYE_POSITION_LEFT_HTC);
        AppendPupil(sb, okPupil ? pupils : null, XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC);
        AppendGeo(sb, okGeo ? geos : null, XrEyePositionHTC.XR_EYE_POSITION_LEFT_HTC);
        AppendGeo(sb, okGeo ? geos : null, XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC);

        UpdateGazeTarget(okGaze ? gazes : null);
        sb.Append(',').Append(Csv(gazeTarget));

        writer.WriteLine(sb.ToString());

        if (Time.realtimeSinceStartup - lastFlush >= flushIntervalSeconds)
        {
            writer.Flush();
            lastFlush = Time.realtimeSinceStartup;
        }
    }

    static void AppendGaze(StringBuilder sb, XrSingleEyeGazeDataHTC[] data, XrEyePositionHTC eye)
    {
        if (data == null || data.Length <= (int)eye) { sb.Append(",0,,,,,,,"); return; }
        var g = data[(int)eye];
        if (!g.isValid) { sb.Append(",0,,,,,,,"); return; }
        Vector3 p = g.gazePose.position.ToUnityVector();
        Quaternion q = g.gazePose.orientation.ToUnityQuaternion();
        sb.Append(",1,").Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z))
          .Append(',').Append(F(q.x)).Append(',').Append(F(q.y)).Append(',').Append(F(q.z)).Append(',').Append(F(q.w));
    }

    static void AppendPupil(StringBuilder sb, XrSingleEyePupilDataHTC[] data, XrEyePositionHTC eye)
    {
        if (data == null || data.Length <= (int)eye) { sb.Append(",0,,0,,"); return; }
        var p = data[(int)eye];
        sb.Append(p.isDiameterValid ? ",1," + F(p.pupilDiameter) : ",0,");
        sb.Append(p.isPositionValid ? ",1," + F(p.pupilPosition.x) + "," + F(p.pupilPosition.y) : ",0,,");
    }

    static void AppendGeo(StringBuilder sb, XrSingleEyeGeometricDataHTC[] data, XrEyePositionHTC eye)
    {
        if (data == null || data.Length <= (int)eye) { sb.Append(",0,,,"); return; }
        var g = data[(int)eye];
        if (!g.isValid) { sb.Append(",0,,,"); return; }
        sb.Append(",1,").Append(F(g.eyeOpenness)).Append(',').Append(F(g.eyeSqueeze)).Append(',').Append(F(g.eyeWide));
    }

    static string Csv(string s)
    {
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    void OnApplicationPause(bool paused)
    {
        if (paused && writer != null) writer.Flush();
    }

    void OnDestroy()
    {
        if (writer != null) { writer.Flush(); writer.Close(); writer = null; }
    }
}
