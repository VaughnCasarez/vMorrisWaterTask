using UnityEngine;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UXF;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;
using XRInputDevice = UnityEngine.XR.InputDevice;

/// <summary>
/// Collects eye tracking data from multiple providers (Vive, Meta OVR, OpenXR, Input System),
/// writes samples into UXF rows, and optionally mirrors samples to a standalone CSV file.
/// </summary>
public class EyeTracking : Tracker
{
    #region Constants

    private const string MetaEyeTrackingPermission = "com.oculus.permission.EYE_TRACKING";

    public override string MeasurementDescriptor => "eye_tracking";

    [Header("Standalone Local Logging")]
    [SerializeField] private bool writeStandaloneCsv = true;
    [SerializeField] private bool writeStandaloneCsvWhenNotRecording = true;
    [SerializeField, Min(0f)] private float standaloneSampleIntervalSeconds = 0f;
    [SerializeField] private string standaloneFolderName = "EyeTrackingLogs";

    [Header("Debug Gaze Cursor")]
    [SerializeField] private bool showDebugGazeCursor = true;
    [SerializeField] private bool showDebugStatusLabel = true;
    [SerializeField, Min(0.01f)] private float debugCursorDistance = 2f;
    [SerializeField, Min(0.001f)] private float debugCursorScale = 0.03f;
    [SerializeField] private Color debugCursorColor = Color.green;
    [SerializeField] private Color debugCursorUnavailableColor = Color.red;
    [SerializeField] private bool showHeadPoseFallbackDotWhenEyeUnavailable = true;

    [Header("UXF Registration")]
    [SerializeField] private bool autoRegisterWithSession = true;

    public override IEnumerable<string> CustomHeader =>
        new string[] {
            "left_gaze_origin_x",
            "left_gaze_origin_y",
            "left_gaze_origin_z",
            "left_gaze_direction_x",
            "left_gaze_direction_y",
            "left_gaze_direction_z",
            "right_gaze_origin_x",
            "right_gaze_origin_y",
            "right_gaze_origin_z",
            "right_gaze_direction_x",
            "right_gaze_direction_y",
            "right_gaze_direction_z",
            "combined_gaze_origin_x",
            "combined_gaze_origin_y",
            "combined_gaze_origin_z",
            "combined_gaze_direction_x",
            "combined_gaze_direction_y",
            "combined_gaze_direction_z",
            "eye_openness_left",
            "eye_openness_right",
            "pupil_diameter_left",
            "pupil_diameter_right",
            "focus_point_x",
            "focus_point_y",
            "focus_point_z",
            "focus_distance",
            "focus_object",
            "tracking_provider",
        };

    private const string EyeGazeBinding = "<EyeGaze>/pose";
    private const string OpenXrEyeTrackingPoseBinding = "<XRInputV1::EyeTrackingOpenXR>/pose";
    private const string OpenXrEyeTrackingIsTrackedBinding = "<XRInputV1::EyeTrackingOpenXR>/pose/isTracked";
    private const string OpenXrEyeTrackingPositionBinding = "<XRInputV1::EyeTrackingOpenXR>/pose/position";
    private const string OpenXrEyeTrackingRotationBinding = "<XRInputV1::EyeTrackingOpenXR>/pose/rotation";
    private readonly List<XRInputDevice> eyeTrackingDevices = new List<XRInputDevice>();
    private const int LeftEyeIndex = 0;
    private const int RightEyeIndex = 1;

    #endregion

    #region Runtime State

    private StreamWriter standaloneWriter;
    private float nextStandaloneSampleTime;
    private string standaloneLogPath;
    private object viveInteropInstance;
    private bool viveInteropLookupAttempted;
    private InputAction eyeGazePoseAction;
    private InputAction openXrEyeTrackingPoseAction;
    private InputAction openXrEyeTrackingIsTrackedAction;
    private InputAction openXrEyeTrackingPositionAction;
    private InputAction openXrEyeTrackingRotationAction;
    private bool attemptedEyeGazePoseActionSetup;
    private bool attemptedMetaEyeTrackingStart;
    private bool requestedMetaEyeTrackingPermission;
    private float nextProviderRefreshTime;
    private EyeSample latestDebugSample;
    private GameObject debugCursorObject;
    private Renderer debugCursorRenderer;
    private Material debugCursorMaterial;
    private bool hasRegisteredWithSession;
    private string latestDiagnosticStatus = string.Empty;
    private string lastLoggedTrackingProvider;
    private bool debugScreenDotVisible;
    private Vector2 debugScreenDotPosition;

    #endregion

    /// <summary>
    /// Snapshot of one eye-tracking sample with validity flags for each signal.
    /// </summary>
    private struct EyeSample
    {
        public Vector3 leftOrigin;
        public Vector3 leftDirection;
        public Vector3 rightOrigin;
        public Vector3 rightDirection;
        public Vector3 combinedOrigin;
        public Vector3 combinedDirection;
        public Vector3 focusPoint;
        public Vector3 worldGazeOrigin;
        public Vector3 worldGazeDirection;
        public float leftOpenness;
        public float rightOpenness;
        public float leftPupilDiameter;
        public float rightPupilDiameter;
        public float focusDistance;
        public string focusObject;
        public string trackingProvider;
        public bool leftOriginValid;
        public bool leftDirectionValid;
        public bool rightOriginValid;
        public bool rightDirectionValid;
        public bool combinedOriginValid;
        public bool combinedDirectionValid;
        public bool focusPointValid;
        public bool worldGazeValid;

        public static EyeSample CreateDefault()
        {
            return new EyeSample
            {
                leftOrigin = NaNVector,
                leftDirection = NaNVector,
                rightOrigin = NaNVector,
                rightDirection = NaNVector,
                combinedOrigin = NaNVector,
                combinedDirection = NaNVector,
                focusPoint = NaNVector,
                worldGazeOrigin = NaNVector,
                worldGazeDirection = NaNVector,
                leftOpenness = float.NaN,
                rightOpenness = float.NaN,
                leftPupilDiameter = float.NaN,
                rightPupilDiameter = float.NaN,
                focusDistance = float.NaN,
                focusObject = "None",
                trackingProvider = "Unavailable"
            };
        }
    }

    private static Vector3 NaNVector => new Vector3(float.NaN, float.NaN, float.NaN);

    #region Unity Lifecycle

    private void Awake()
    {
        TryRegisterWithSession();
    }

    private void Start()
    {
        TryRegisterWithSession();
        EnsureMetaEyeTrackingPermission();
    }

    private void Update()
    {
        TryRegisterWithSession();

        bool shouldWriteStandaloneSample = writeStandaloneCsvWhenNotRecording && !Recording && ShouldWriteStandaloneSample();
        bool shouldUpdateDebugCursor = showDebugGazeCursor;

        if (!shouldWriteStandaloneSample && !shouldUpdateDebugCursor)
        {
            return;
        }

        EyeSample sample = CaptureEyeSample();
        PrepareSampleForOutput(ref sample);
        RefreshEyeTrackingProvidersIfNeeded(sample);
        latestDebugSample = sample;
        latestDiagnosticStatus = BuildDiagnosticStatus(sample);
        LogTrackingProviderTransition(sample);
        UpdateDebugScreenDot(sample);

        if (shouldUpdateDebugCursor)
        {
            UpdateDebugCursor(sample);
        }

        if (shouldWriteStandaloneSample)
        {
            AppendStandaloneSample(sample);
        }
    }

    private void OnDisable()
    {
        SetDebugCursorActive(false);
        CloseStandaloneWriter();
    }

    private void OnDestroy()
    {
        if (eyeGazePoseAction != null)
        {
            eyeGazePoseAction.Disable();
            eyeGazePoseAction.Dispose();
            eyeGazePoseAction = null;
        }

        DisposeInputAction(ref openXrEyeTrackingPoseAction);
        DisposeInputAction(ref openXrEyeTrackingIsTrackedAction);
        DisposeInputAction(ref openXrEyeTrackingPositionAction);
        DisposeInputAction(ref openXrEyeTrackingRotationAction);

        DestroyDebugCursor();
        CloseStandaloneWriter();
    }

    #endregion

    #region UXF Integration

    private void OnGUI()
    {
        if (!showDebugStatusLabel)
        {
            DrawDebugScreenDot();
            return;
        }

        GUI.contentColor = latestDebugSample.worldGazeValid ? debugCursorColor : debugCursorUnavailableColor;
        GUI.Label(new Rect(16f, 16f, 1400f, 140f),
            $"Eye Tracking: {latestDebugSample.trackingProvider} | Combined Valid: {latestDebugSample.combinedDirectionValid} | Focus: {latestDebugSample.focusObject}\n{latestDiagnosticStatus}");
        GUI.contentColor = Color.white;
        DrawDebugScreenDot();
    }

    protected override UXFDataRow GetCurrentValues()
    {
        EyeSample sample = CaptureEyeSample();
        PrepareSampleForOutput(ref sample);
        RefreshEyeTrackingProvidersIfNeeded(sample);
        latestDebugSample = sample;
        latestDiagnosticStatus = BuildDiagnosticStatus(sample);
        LogTrackingProviderTransition(sample);
        UpdateDebugScreenDot(sample);

        if (showDebugGazeCursor)
        {
            UpdateDebugCursor(sample);
        }

        UXFDataRow row = CreateDataRow(sample);

        if (writeStandaloneCsv && ShouldWriteStandaloneSample())
        {
            AppendStandaloneSample(sample);
        }

        return row;
    }

    /// <summary>
    /// Adds this tracker to the active UXF session if not already registered.
    /// </summary>
    public void RegisterSelf()
    {
        if (Session.instance != null && !Session.instance.trackedObjects.Contains(this))
        {
            Session.instance.trackedObjects.Add(this);
            hasRegisteredWithSession = true;
        }
    }

    private void TryRegisterWithSession()
    {
        if (!autoRegisterWithSession || hasRegisteredWithSession)
        {
            return;
        }

        if (Session.instance == null)
        {
            return;
        }

        RegisterSelf();
    }

    #endregion

    #region Sampling Pipeline

    private UXFDataRow CreateDataRow(EyeSample sample)
    {
        UXFDataRow row = new UXFDataRow();
        row.Add(("left_gaze_origin_x", sample.leftOrigin.x));
        row.Add(("left_gaze_origin_y", sample.leftOrigin.y));
        row.Add(("left_gaze_origin_z", sample.leftOrigin.z));
        row.Add(("left_gaze_direction_x", sample.leftDirection.x));
        row.Add(("left_gaze_direction_y", sample.leftDirection.y));
        row.Add(("left_gaze_direction_z", sample.leftDirection.z));
        row.Add(("right_gaze_origin_x", sample.rightOrigin.x));
        row.Add(("right_gaze_origin_y", sample.rightOrigin.y));
        row.Add(("right_gaze_origin_z", sample.rightOrigin.z));
        row.Add(("right_gaze_direction_x", sample.rightDirection.x));
        row.Add(("right_gaze_direction_y", sample.rightDirection.y));
        row.Add(("right_gaze_direction_z", sample.rightDirection.z));
        row.Add(("combined_gaze_origin_x", sample.combinedOrigin.x));
        row.Add(("combined_gaze_origin_y", sample.combinedOrigin.y));
        row.Add(("combined_gaze_origin_z", sample.combinedOrigin.z));
        row.Add(("combined_gaze_direction_x", sample.combinedDirection.x));
        row.Add(("combined_gaze_direction_y", sample.combinedDirection.y));
        row.Add(("combined_gaze_direction_z", sample.combinedDirection.z));
        row.Add(("eye_openness_left", sample.leftOpenness));
        row.Add(("eye_openness_right", sample.rightOpenness));
        row.Add(("pupil_diameter_left", sample.leftPupilDiameter));
        row.Add(("pupil_diameter_right", sample.rightPupilDiameter));
        row.Add(("focus_point_x", sample.focusPoint.x));
        row.Add(("focus_point_y", sample.focusPoint.y));
        row.Add(("focus_point_z", sample.focusPoint.z));
        row.Add(("focus_distance", sample.focusDistance));
        row.Add(("focus_object", sample.focusObject));
        row.Add(("tracking_provider", sample.trackingProvider));
        return row;
    }

    private EyeSample CaptureEyeSample()
    {
        EnsureMetaEyeTrackingPermission();

        EyeSample sample = EyeSample.CreateDefault();

        if (!TryCaptureViveSample(ref sample))
        {
            if (!TryCaptureMetaOvrSample(ref sample))
            {
                TryCaptureOpenXrSample(ref sample);
            }
        }

        PopulateCombinedData(ref sample);
        PopulateFocusData(ref sample);
        return sample;
    }

    #endregion

    #region Provider Capture

    private bool TryCaptureViveSample(ref EyeSample sample)
    {
        try
        {
            if (!TryInvokeViveInteropArray("GetEyeGazeData", out Array outGazes) || outGazes.Length < 2)
            {
                return false;
            }

            if (TryGetArrayElement(outGazes, LeftEyeIndex, out object leftGaze) && TryGetBoolMember(leftGaze, "isValid", out bool isLeftValid) && isLeftValid)
            {
                PopulateEyePose(leftGaze, ref sample.leftOrigin, ref sample.leftOriginValid, ref sample.leftDirection, ref sample.leftDirectionValid);
            }

            if (TryGetArrayElement(outGazes, RightEyeIndex, out object rightGaze) && TryGetBoolMember(rightGaze, "isValid", out bool isRightValid) && isRightValid)
            {
                PopulateEyePose(rightGaze, ref sample.rightOrigin, ref sample.rightOriginValid, ref sample.rightDirection, ref sample.rightDirectionValid);
            }

            if (!sample.leftOriginValid && !sample.rightOriginValid && !sample.leftDirectionValid && !sample.rightDirectionValid)
            {
                return false;
            }

            if (TryInvokeViveInteropArray("GetEyePupilData", out Array outPupils) && outPupils.Length >= 2)
            {
                if (TryGetArrayElement(outPupils, RightEyeIndex, out object rightPupil)
                    && TryGetBoolMember(rightPupil, "isDiameterValid", out bool isRightDiameterValid)
                    && isRightDiameterValid
                    && TryGetFloatMember(rightPupil, "pupilDiameter", out float rightPupilDiameter))
                {
                    sample.rightPupilDiameter = rightPupilDiameter;
                }

                if (TryGetArrayElement(outPupils, LeftEyeIndex, out object leftPupil)
                    && TryGetBoolMember(leftPupil, "isDiameterValid", out bool isLeftDiameterValid)
                    && isLeftDiameterValid
                    && TryGetFloatMember(leftPupil, "pupilDiameter", out float leftPupilDiameter))
                {
                    sample.leftPupilDiameter = leftPupilDiameter;
                }
            }

            if (TryInvokeViveInteropArray("GetEyeGeometricData", out Array outGeometrics) && outGeometrics.Length >= 2)
            {
                if (TryGetArrayElement(outGeometrics, RightEyeIndex, out object rightGeometric)
                    && TryGetBoolMember(rightGeometric, "isValid", out bool isRightGeometricValid)
                    && isRightGeometricValid
                    && TryGetFloatMember(rightGeometric, "eyeOpenness", out float rightOpenness))
                {
                    sample.rightOpenness = rightOpenness;
                }

                if (TryGetArrayElement(outGeometrics, LeftEyeIndex, out object leftGeometric)
                    && TryGetBoolMember(leftGeometric, "isValid", out bool isLeftGeometricValid)
                    && isLeftGeometricValid
                    && TryGetFloatMember(leftGeometric, "eyeOpenness", out float leftOpenness))
                {
                    sample.leftOpenness = leftOpenness;
                }
            }

            sample.trackingProvider = "ViveOpenXR";
            return true;
        }
        catch
        {
            return false;
        }

        #endregion

        #region Reflection Helpers
    }

    private bool TryInvokeViveInteropArray(string methodName, out Array resultArray)
    {
        resultArray = null;

        object interop = GetViveInteropInstance();
        if (interop == null)
        {
            return false;
        }

        MethodInfo method = interop.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
        {
            return false;
        }

        object[] arguments = { null };
        method.Invoke(interop, arguments);
        resultArray = arguments[0] as Array;
        return resultArray != null;
    }

    // VIVE.OpenXR.XR_HTC_eye_tracker exposes a static "Interop" *property* returning a live instance;
    // GetEyeGazeData/GetEyePupilData/GetEyeGeometricData are instance methods on that object, not static nested-type members.
    private object GetViveInteropInstance()
    {
        if (viveInteropInstance != null || viveInteropLookupAttempted)
        {
            return viveInteropInstance;
        }

        viveInteropLookupAttempted = true;

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type interopHolderType = assembly.GetType("VIVE.OpenXR.XR_HTC_eye_tracker", false)
                ?? assembly.GetType("VIVE.OpenXR.EyeTracker.XR_HTC_eye_tracker", false);

            if (interopHolderType == null)
            {
                continue;
            }

            PropertyInfo interopProperty = interopHolderType.GetProperty("Interop", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (interopProperty == null)
            {
                continue;
            }

            viveInteropInstance = interopProperty.GetValue(null);
            if (viveInteropInstance != null)
            {
                return viveInteropInstance;
            }
        }

        return null;
    }

    private static bool TryGetArrayElement(Array array, int index, out object value)
    {
        value = null;
        if (array == null || index < 0 || index >= array.Length)
        {
            return false;
        }

        value = array.GetValue(index);
        return value != null;
    }

    private static void PopulateEyePose(object gazeData, ref Vector3 origin, ref bool originValid, ref Vector3 direction, ref bool directionValid)
    {
        if (!TryGetMemberValue(gazeData, "gazePose", out object gazePose) || gazePose == null)
        {
            return;
        }

        if (TryGetMemberValue(gazePose, "position", out object positionValue) && TryConvertToVector3(positionValue, out Vector3 position))
        {
            origin = position;
            originValid = true;
        }

        if (TryGetMemberValue(gazePose, "orientation", out object orientationValue) && TryConvertToQuaternion(orientationValue, out Quaternion rotation))
        {
            direction = rotation * Vector3.forward;
            directionValid = true;
        }
    }

    private static bool TryConvertToVector3(object value, out Vector3 result)
    {
        result = default;
        if (value == null)
        {
            return false;
        }

        if (value is Vector3 vector3)
        {
            result = vector3;
            return true;
        }

        if (TryInvokeConversionMethod<Vector3>(value, "ToUnityVector", out Vector3 convertedVector))
        {
            result = convertedVector;
            return true;
        }

        if (TryGetFloatMember(value, "x", out float x)
            && TryGetFloatMember(value, "y", out float y)
            && TryGetFloatMember(value, "z", out float z))
        {
            result = new Vector3(x, y, z);
            return true;
        }

        return false;
    }

    private static bool TryConvertToQuaternion(object value, out Quaternion result)
    {
        result = default;
        if (value == null)
        {
            return false;
        }

        if (value is Quaternion quaternion)
        {
            result = quaternion;
            return true;
        }

        if (TryInvokeConversionMethod<Quaternion>(value, "ToUnityQuaternion", out Quaternion convertedQuaternion))
        {
            result = convertedQuaternion;
            return true;
        }

        if (TryGetFloatMember(value, "x", out float x)
            && TryGetFloatMember(value, "y", out float y)
            && TryGetFloatMember(value, "z", out float z)
            && TryGetFloatMember(value, "w", out float w))
        {
            result = new Quaternion(x, y, z, w);
            return true;
        }

        return false;
    }

    private static bool TryInvokeConversionMethod<T>(object source, string methodName, out T result)
    {
        result = default;
        Type sourceType = source.GetType();

        MethodInfo instanceMethod = sourceType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (instanceMethod != null && instanceMethod.Invoke(source, null) is T convertedByInstance)
        {
            result = convertedByInstance;
            return true;
        }

        // VIVE.OpenXR's ToUnityVector/ToUnityQuaternion are extension methods (static, "this XrVector3f"/"this XrQuaternionf"),
        // not instance members, and they apply the required OpenXR (right-handed) -> Unity (left-handed) axis flip.
        MethodInfo extensionMethod = FindViveOpenXrHelperMethod(methodName, sourceType, typeof(T));
        if (extensionMethod != null && extensionMethod.Invoke(null, new[] { source }) is T convertedByExtension)
        {
            result = convertedByExtension;
            return true;
        }

        return false;
    }

    private static Type viveOpenXrHelperType;
    private static bool viveOpenXrHelperLookupAttempted;

    private static MethodInfo FindViveOpenXrHelperMethod(string methodName, Type parameterType, Type returnType)
    {
        if (!viveOpenXrHelperLookupAttempted)
        {
            viveOpenXrHelperLookupAttempted = true;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                viveOpenXrHelperType = assembly.GetType("VIVE.OpenXR.OpenXRHelper", false);
                if (viveOpenXrHelperType != null)
                {
                    break;
                }
            }
        }

        if (viveOpenXrHelperType == null)
        {
            return null;
        }

        MethodInfo method = viveOpenXrHelperType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, new[] { parameterType }, null);
        return method != null && method.ReturnType == returnType ? method : null;
    }

    private static bool TryGetBoolMember(object source, string memberName, out bool value)
    {
        value = default;
        if (!TryGetMemberValue(source, memberName, out object rawValue) || rawValue == null)
        {
            return false;
        }

        if (rawValue is bool boolValue)
        {
            value = boolValue;
            return true;
        }

        // OpenXR interop wrappers (e.g. VIVE's XrBool32) aren't a real System.Boolean - they only expose an implicit conversion operator.
        Type rawValueType = rawValue.GetType();
        foreach (MethodInfo candidate in rawValueType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (candidate.Name != "op_Implicit" || candidate.ReturnType != typeof(bool))
            {
                continue;
            }

            ParameterInfo[] parameters = candidate.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == rawValueType)
            {
                value = (bool)candidate.Invoke(null, new[] { rawValue });
                return true;
            }
        }

        return false;
    }

    private static bool TryGetFloatMember(object source, string memberName, out float value)
    {
        value = default;
        if (!TryGetMemberValue(source, memberName, out object rawValue) || rawValue == null)
        {
            return false;
        }

        switch (rawValue)
        {
            case float floatValue:
                value = floatValue;
                return true;
            case double doubleValue:
                value = (float)doubleValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetMemberValue(object source, string memberName, out object value)
    {
        value = null;
        if (source == null)
        {
            return false;
        }

        Type type = source.GetType();
        FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            value = field.GetValue(source);
            return true;
        }

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property != null)
        {
            value = property.GetValue(source);
            return true;
        }

        return false;
    }

    #endregion

    #region OpenXR And Input System Fallbacks

    private bool TryCaptureOpenXrSample(ref EyeSample sample)
    {
        if (TryCaptureOpenXrEyesData(ref sample))
        {
            return true;
        }

        if (TryCaptureOpenXrDevicePose(ref sample))
        {
            return true;
        }

        return TryCaptureInputSystemEyeGaze(ref sample);
    }

    private bool TryCaptureMetaOvrSample(ref EyeSample sample)
    {
        try
        {
            Type ovrPluginType = GetOvrPluginType();
            if (ovrPluginType == null)
            {
                return false;
            }

            PropertyInfo eyeTrackingSupportedProp = ovrPluginType.GetProperty("eyeTrackingSupported", BindingFlags.Public | BindingFlags.Static);
            if (eyeTrackingSupportedProp == null || !(eyeTrackingSupportedProp.GetValue(null) is bool eyeTrackingSupported) || !eyeTrackingSupported)
            {
                return false;
            }

            if (!IsMetaEyeTrackingPermissionGranted())
            {
                return false;
            }

            if (!attemptedMetaEyeTrackingStart)
            {
                attemptedMetaEyeTrackingStart = true;
                MethodInfo startEyeTrackingMethod = ovrPluginType.GetMethod("StartEyeTracking", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                startEyeTrackingMethod?.Invoke(null, null);
            }

            Type stepType = ovrPluginType.GetNestedType("Step", BindingFlags.Public);
            Type eyeGazesStateType = ovrPluginType.GetNestedType("EyeGazesState", BindingFlags.Public);
            if (stepType == null || eyeGazesStateType == null)
            {
                return false;
            }

            MethodInfo getEyeGazesStateMethod = ovrPluginType.GetMethod("GetEyeGazesState", BindingFlags.Public | BindingFlags.Static);
            if (getEyeGazesStateMethod == null)
            {
                return false;
            }

            object eyeGazesState = Activator.CreateInstance(eyeGazesStateType);
            object renderStep = Enum.Parse(stepType, "Render");
            object[] args = { renderStep, -1, eyeGazesState };
            object callResult = getEyeGazesStateMethod.Invoke(null, args);
            if (!(callResult is bool gotState) || !gotState)
            {
                return false;
            }

            object updatedState = args[2];
            if (!TryGetMemberValue(updatedState, "EyeGazes", out object eyeGazesRaw) || !(eyeGazesRaw is Array eyeGazes) || eyeGazes.Length < 2)
            {
                return false;
            }

            bool capturedAnyData = false;

            if (TryGetArrayElement(eyeGazes, LeftEyeIndex, out object leftEyeGaze)
                && TryGetMemberValue(leftEyeGaze, "IsValid", out object leftValidRaw)
                && leftValidRaw is bool leftValid
                && leftValid
                && TryGetMemberValue(leftEyeGaze, "Pose", out object leftPose))
            {
                if (TryGetMemberValue(leftPose, "Position", out object leftPositionValue) && TryConvertToVector3(leftPositionValue, out Vector3 leftOrigin))
                {
                    sample.leftOrigin = leftOrigin;
                    sample.leftOriginValid = true;
                    capturedAnyData = true;
                }

                if (TryGetMemberValue(leftPose, "Orientation", out object leftOrientationValue) && TryConvertToQuaternion(leftOrientationValue, out Quaternion leftRotation))
                {
                    sample.leftDirection = leftRotation * Vector3.forward;
                    sample.leftDirectionValid = true;
                    capturedAnyData = true;
                }
            }

            if (TryGetArrayElement(eyeGazes, RightEyeIndex, out object rightEyeGaze)
                && TryGetMemberValue(rightEyeGaze, "IsValid", out object rightValidRaw)
                && rightValidRaw is bool rightValid
                && rightValid
                && TryGetMemberValue(rightEyeGaze, "Pose", out object rightPose))
            {
                if (TryGetMemberValue(rightPose, "Position", out object rightPositionValue) && TryConvertToVector3(rightPositionValue, out Vector3 rightOrigin))
                {
                    sample.rightOrigin = rightOrigin;
                    sample.rightOriginValid = true;
                    capturedAnyData = true;
                }

                if (TryGetMemberValue(rightPose, "Orientation", out object rightOrientationValue) && TryConvertToQuaternion(rightOrientationValue, out Quaternion rightRotation))
                {
                    sample.rightDirection = rightRotation * Vector3.forward;
                    sample.rightDirectionValid = true;
                    capturedAnyData = true;
                }
            }

            if (capturedAnyData)
            {
                sample.trackingProvider = "MetaOVRPlugin";
            }

            return capturedAnyData;
        }
        catch
        {
            return false;
        }
    }

    private static Type GetOvrPluginType()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type ovrPluginType = assembly.GetType("OVRPlugin", false);
            if (ovrPluginType != null)
            {
                return ovrPluginType;
            }
        }

        return null;
    }

    private void EnsureMetaEyeTrackingPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (requestedMetaEyeTrackingPermission)
        {
            return;
        }

        requestedMetaEyeTrackingPermission = true;
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(MetaEyeTrackingPermission))
        {
            UnityEngine.Android.Permission.RequestUserPermission(MetaEyeTrackingPermission);
        }
#endif
    }

    private bool IsMetaEyeTrackingPermissionGranted()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return UnityEngine.Android.Permission.HasUserAuthorizedPermission(MetaEyeTrackingPermission);
#else
        return true;
#endif
    }

    private bool TryCaptureOpenXrEyesData(ref EyeSample sample)
    {
        if (!TryGetEyeTrackingDevice(out XRInputDevice eyeTrackingDevice))
        {
            return false;
        }

        if (!eyeTrackingDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.eyesData, out UnityEngine.XR.Eyes eyes))
        {
            return false;
        }

        bool capturedAnyData = false;

        if (eyes.TryGetLeftEyePosition(out Vector3 leftOrigin))
        {
            sample.leftOrigin = leftOrigin;
            sample.leftOriginValid = true;
            capturedAnyData = true;
        }

        if (eyes.TryGetLeftEyeRotation(out Quaternion leftRotation))
        {
            sample.leftDirection = leftRotation * Vector3.forward;
            sample.leftDirectionValid = true;
            capturedAnyData = true;
        }

        if (eyes.TryGetRightEyePosition(out Vector3 rightOrigin))
        {
            sample.rightOrigin = rightOrigin;
            sample.rightOriginValid = true;
            capturedAnyData = true;
        }

        if (eyes.TryGetRightEyeRotation(out Quaternion rightRotation))
        {
            sample.rightDirection = rightRotation * Vector3.forward;
            sample.rightDirectionValid = true;
            capturedAnyData = true;
        }

        if (capturedAnyData)
        {
            sample.trackingProvider = "XR.EyesData:" + eyeTrackingDevice.name;
        }

        return capturedAnyData;
    }

    private bool TryCaptureOpenXrDevicePose(ref EyeSample sample)
    {
        if (!TryGetEyeTrackingDevice(out XRInputDevice eyeTrackingDevice))
        {
            return false;
        }

        if (eyeTrackingDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool isTracked) && !isTracked)
        {
            return false;
        }

        bool hasPosition = eyeTrackingDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 position);
        bool hasRotation = eyeTrackingDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion rotation);
        if (!hasPosition && !hasRotation)
        {
            return false;
        }

        Vector3 gazeDirection = hasRotation ? rotation * Vector3.forward : Vector3.forward;
        PopulateCombinedPoseIntoSample(ref sample, position, hasPosition, gazeDirection, hasRotation);
        sample.trackingProvider = "XR.DevicePose:" + eyeTrackingDevice.name;
        return true;
    }

    private bool TryCaptureInputSystemEyeGaze(ref EyeSample sample)
    {
        if (TryCaptureDirectOpenXrEyeTrackingPose(ref sample))
        {
            return true;
        }

        EnsureEyeGazePoseAction();
        if (eyeGazePoseAction == null || !eyeGazePoseAction.enabled || eyeGazePoseAction.activeControl == null)
        {
            return false;
        }

#if USE_INPUT_SYSTEM_POSE_CONTROL
        if (eyeGazePoseAction.activeControl.valueType != typeof(PoseState))
        {
            return false;
        }

        PoseState pose = eyeGazePoseAction.ReadValue<PoseState>();
        if (!pose.isTracked)
        {
            return false;
        }

        bool hasPosition = pose.trackingState.HasFlag(InputTrackingState.Position);
        bool hasRotation = pose.trackingState.HasFlag(InputTrackingState.Rotation);
        PopulateCombinedPoseIntoSample(ref sample, pose.position, hasPosition, pose.rotation * Vector3.forward, hasRotation);
#else
        if (eyeGazePoseAction.activeControl.valueType != typeof(UnityEngine.XR.OpenXR.Input.Pose))
        {
            return false;
        }

        UnityEngine.XR.OpenXR.Input.Pose pose = eyeGazePoseAction.ReadValue<UnityEngine.XR.OpenXR.Input.Pose>();
        if (!pose.isTracked)
        {
            return false;
        }

        bool hasPosition = pose.trackingState.HasFlag(InputTrackingState.Position);
        bool hasRotation = pose.trackingState.HasFlag(InputTrackingState.Rotation);
        PopulateCombinedPoseIntoSample(ref sample, pose.position, hasPosition, pose.rotation * Vector3.forward, hasRotation);
#endif

        sample.trackingProvider = "InputSystem:EyeGaze";
        return true;
    }

    private bool TryCaptureDirectOpenXrEyeTrackingPose(ref EyeSample sample)
    {
        EnsureEyeGazePoseAction();
        if (openXrEyeTrackingIsTrackedAction == null || openXrEyeTrackingPositionAction == null || openXrEyeTrackingRotationAction == null)
        {
            return false;
        }

        if (!openXrEyeTrackingIsTrackedAction.enabled || openXrEyeTrackingIsTrackedAction.controls.Count == 0)
        {
            return false;
        }

        bool isTracked = openXrEyeTrackingIsTrackedAction.ReadValue<float>() > 0.5f;
        if (!isTracked)
        {
            return false;
        }

        Vector3 position = openXrEyeTrackingPositionAction.ReadValue<Vector3>();
        Quaternion rotation = openXrEyeTrackingRotationAction.ReadValue<Quaternion>();

        bool hasPositionControl = openXrEyeTrackingPositionAction.controls.Count > 0;
        bool hasRotationControl = openXrEyeTrackingRotationAction.controls.Count > 0;
        bool hasPosition = hasPositionControl && !HasNaN(position);
        bool hasRotation = hasRotationControl && !HasNaN(rotation) && rotation != default;

        if (!hasPosition && !hasRotation)
        {
            return false;
        }

        PopulateCombinedPoseIntoSample(ref sample, position, hasPosition, rotation * Vector3.forward, hasRotation);
        sample.trackingProvider = "InputSystem:EyeTrackingOpenXR";
        return sample.combinedDirectionValid || sample.combinedOriginValid;
    }

    private static bool HasNaN(Vector3 value)
    {
        return float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z);
    }

    private static bool HasNaN(Quaternion value)
    {
        return float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) || float.IsNaN(value.w);
    }

    #endregion

    #region Input Action Setup

    private void EnsureEyeGazePoseAction()
    {
        if (attemptedEyeGazePoseActionSetup)
        {
            return;
        }

        attemptedEyeGazePoseActionSetup = true;

        try
        {
            eyeGazePoseAction = new InputAction("EyeGazePose", InputActionType.PassThrough, EyeGazeBinding);
            eyeGazePoseAction.Enable();

            openXrEyeTrackingPoseAction = new InputAction("OpenXrEyeTrackingPose", InputActionType.PassThrough, OpenXrEyeTrackingPoseBinding);
            openXrEyeTrackingPoseAction.Enable();

            openXrEyeTrackingIsTrackedAction = new InputAction("OpenXrEyeTrackingIsTracked", InputActionType.Value, OpenXrEyeTrackingIsTrackedBinding);
            openXrEyeTrackingIsTrackedAction.Enable();

            openXrEyeTrackingPositionAction = new InputAction("OpenXrEyeTrackingPosition", InputActionType.PassThrough, OpenXrEyeTrackingPositionBinding);
            openXrEyeTrackingPositionAction.Enable();

            openXrEyeTrackingRotationAction = new InputAction("OpenXrEyeTrackingRotation", InputActionType.PassThrough, OpenXrEyeTrackingRotationBinding);
            openXrEyeTrackingRotationAction.Enable();
        }
        catch
        {
            eyeGazePoseAction = null;
            DisposeInputAction(ref openXrEyeTrackingPoseAction);
            DisposeInputAction(ref openXrEyeTrackingIsTrackedAction);
            DisposeInputAction(ref openXrEyeTrackingPositionAction);
            DisposeInputAction(ref openXrEyeTrackingRotationAction);
        }
    }

    private void DisposeInputAction(ref InputAction action)
    {
        if (action == null)
        {
            return;
        }

        action.Disable();
        action.Dispose();
        action = null;
    }

    #endregion

    #region Provider Refresh And Sample Normalization

    private void RefreshEyeTrackingProvidersIfNeeded(EyeSample sample)
    {
        if (sample.trackingProvider != "Unavailable")
        {
            return;
        }

        if (Time.unscaledTime < nextProviderRefreshTime)
        {
            return;
        }

        nextProviderRefreshTime = Time.unscaledTime + 1f;
        attemptedMetaEyeTrackingStart = false;
        attemptedEyeGazePoseActionSetup = false;

        DisposeInputAction(ref eyeGazePoseAction);
        DisposeInputAction(ref openXrEyeTrackingPoseAction);
        DisposeInputAction(ref openXrEyeTrackingIsTrackedAction);
        DisposeInputAction(ref openXrEyeTrackingPositionAction);
        DisposeInputAction(ref openXrEyeTrackingRotationAction);

        EnsureMetaEyeTrackingPermission();
        EnsureEyeGazePoseAction();
    }

    private void PopulateCombinedPoseIntoSample(ref EyeSample sample, Vector3 position, bool hasPosition, Vector3 direction, bool hasDirection)
    {
        if (hasPosition)
        {
            sample.leftOrigin = position;
            sample.rightOrigin = position;
            sample.combinedOrigin = position;
            sample.leftOriginValid = true;
            sample.rightOriginValid = true;
            sample.combinedOriginValid = true;
        }

        if (hasDirection)
        {
            Vector3 normalizedDirection = direction.normalized;
            sample.leftDirection = normalizedDirection;
            sample.rightDirection = normalizedDirection;
            sample.combinedDirection = normalizedDirection;
            sample.leftDirectionValid = true;
            sample.rightDirectionValid = true;
            sample.combinedDirectionValid = true;
        }
    }

    private void PrepareSampleForOutput(ref EyeSample sample)
    {
        if (sample.combinedOriginValid)
        {
            if (!sample.leftOriginValid)
            {
                sample.leftOrigin = sample.combinedOrigin;
                sample.leftOriginValid = true;
            }

            if (!sample.rightOriginValid)
            {
                sample.rightOrigin = sample.combinedOrigin;
                sample.rightOriginValid = true;
            }
        }

        if (sample.combinedDirectionValid)
        {
            if (!sample.leftDirectionValid)
            {
                sample.leftDirection = sample.combinedDirection;
                sample.leftDirectionValid = true;
            }

            if (!sample.rightDirectionValid)
            {
                sample.rightDirection = sample.combinedDirection;
                sample.rightDirectionValid = true;
            }
        }
    }

    #endregion

    #region Debug Visualization

    private void UpdateDebugScreenDot(EyeSample sample)
    {
        debugScreenDotVisible = false;

        if (!showDebugGazeCursor || Camera.main == null)
        {
            return;
        }

        Vector3 worldPoint;
        if (sample.focusPointValid)
        {
            worldPoint = sample.focusPoint;
        }
        else if (sample.worldGazeValid)
        {
            worldPoint = sample.worldGazeOrigin + (sample.worldGazeDirection * debugCursorDistance);
        }
        else if (showHeadPoseFallbackDotWhenEyeUnavailable)
        {
            worldPoint = Camera.main.transform.position + (Camera.main.transform.forward * debugCursorDistance);
        }
        else
        {
            return;
        }

        Vector3 screenPoint = Camera.main.WorldToScreenPoint(worldPoint);
        if (screenPoint.z <= 0f)
        {
            return;
        }

        debugScreenDotVisible = true;
        debugScreenDotPosition = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
    }

    private void DrawDebugScreenDot()
    {
        if (!debugScreenDotVisible)
        {
            return;
        }

        float pixelSize = Mathf.Max(6f, debugCursorScale * 600f);
        Rect dotRect = new Rect(
            debugScreenDotPosition.x - (pixelSize * 0.5f),
            debugScreenDotPosition.y - (pixelSize * 0.5f),
            pixelSize,
            pixelSize);

        Color oldColor = GUI.color;
        GUI.color = latestDebugSample.worldGazeValid ? debugCursorColor : debugCursorUnavailableColor;
        GUI.DrawTexture(dotRect, Texture2D.whiteTexture);
        GUI.color = oldColor;
    }

    private string BuildDiagnosticStatus(EyeSample sample)
    {
        StringBuilder status = new StringBuilder(256);
        bool eyeGazeExtensionEnabled = OpenXRRuntime.IsExtensionEnabled("XR_EXT_eye_gaze_interaction");
        status.Append("XR_EXT_eye_gaze_interaction: ").Append(eyeGazeExtensionEnabled ? "enabled" : "disabled");

        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.EyeTracking, eyeTrackingDevices);
        status.Append(" | XR eye devices: ").Append(eyeTrackingDevices.Count);
        if (eyeTrackingDevices.Count > 0)
        {
            status.Append(" (");
            for (int index = 0; index < eyeTrackingDevices.Count; index++)
            {
                if (index > 0)
                {
                    status.Append(", ");
                }

                status.Append(eyeTrackingDevices[index].name);
            }
            status.Append(')');
        }

        int inputSystemEyeGazeDeviceCount = 0;
        foreach (UnityEngine.InputSystem.InputDevice device in InputSystem.devices)
        {
            if (device.layout == "EyeGaze")
            {
                inputSystemEyeGazeDeviceCount++;
            }
        }

        status.Append(" | InputSystem EyeGaze devices: ").Append(inputSystemEyeGazeDeviceCount);

        EnsureEyeGazePoseAction();
        if (openXrEyeTrackingIsTrackedAction != null)
        {
            status.Append(" | Direct EyeTrackingOpenXR tracked: ").Append(openXrEyeTrackingIsTrackedAction.ReadValue<float>() > 0.5f);
            status.Append(" posCtl: ").Append(openXrEyeTrackingPositionAction != null && openXrEyeTrackingPositionAction.controls.Count > 0 ? openXrEyeTrackingPositionAction.controls[0].path : "none");
            status.Append(" rotCtl: ").Append(openXrEyeTrackingRotationAction != null && openXrEyeTrackingRotationAction.controls.Count > 0 ? openXrEyeTrackingRotationAction.controls[0].path : "none");
        }

        if (eyeGazePoseAction == null)
        {
            status.Append(" | EyeGaze action: null");
        }
        else
        {
            status.Append(" | EyeGaze action enabled: ").Append(eyeGazePoseAction.enabled);
            status.Append(" activeControl: ").Append(eyeGazePoseAction.activeControl != null ? eyeGazePoseAction.activeControl.path : "none");

#if USE_INPUT_SYSTEM_POSE_CONTROL
            if (eyeGazePoseAction.activeControl != null && eyeGazePoseAction.activeControl.valueType == typeof(PoseState))
            {
                PoseState pose = eyeGazePoseAction.ReadValue<PoseState>();
                status.Append(" tracked: ").Append(pose.isTracked);
                status.Append(" state: ").Append(pose.trackingState);
            }
#else
            if (eyeGazePoseAction.activeControl != null && eyeGazePoseAction.activeControl.valueType == typeof(UnityEngine.XR.OpenXR.Input.Pose))
            {
                UnityEngine.XR.OpenXR.Input.Pose pose = eyeGazePoseAction.ReadValue<UnityEngine.XR.OpenXR.Input.Pose>();
                status.Append(" tracked: ").Append(pose.isTracked);
                status.Append(" state: ").Append(pose.trackingState);
            }
#endif
        }

        status.Append(" | Vive interop: ").Append(GetViveInteropInstance() != null ? "found" : "missing");
        Type ovrPluginType = GetOvrPluginType();
        if (ovrPluginType != null)
        {
            PropertyInfo eyeTrackingSupportedProp = ovrPluginType.GetProperty("eyeTrackingSupported", BindingFlags.Public | BindingFlags.Static);
            PropertyInfo eyeTrackingEnabledProp = ovrPluginType.GetProperty("eyeTrackingEnabled", BindingFlags.Public | BindingFlags.Static);
            if (eyeTrackingSupportedProp != null && eyeTrackingEnabledProp != null)
            {
                bool isMetaEyeSupported = eyeTrackingSupportedProp.GetValue(null) is bool supported && supported;
                bool isMetaEyeEnabled = eyeTrackingEnabledProp.GetValue(null) is bool enabled && enabled;
                status.Append(" | Meta eye supported: ").Append(isMetaEyeSupported);
                status.Append(" enabled: ").Append(isMetaEyeEnabled);
            }
        }

        status.Append(" | Meta eye permission: ").Append(IsMetaEyeTrackingPermissionGranted());

        status.Append(" | Sample provider: ").Append(sample.trackingProvider);
        return status.ToString();
    }

    // Logs provider transitions to the player log so a failed run can be diagnosed after the fact (e.g. via adb logcat), without needing to read the in-headset HUD live.
    private void LogTrackingProviderTransition(EyeSample sample)
    {
        if (sample.trackingProvider == lastLoggedTrackingProvider)
        {
            return;
        }

        lastLoggedTrackingProvider = sample.trackingProvider;

        if (sample.trackingProvider == "Unavailable")
        {
            Debug.LogWarning($"EyeTracking: provider became Unavailable. {latestDiagnosticStatus}", this);
        }
        else
        {
            Debug.Log($"EyeTracking: provider active -> {sample.trackingProvider}", this);
        }
    }

    private bool TryGetEyeTrackingDevice(out XRInputDevice eyeTrackingDevice)
    {
        eyeTrackingDevice = default;

        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.EyeTracking, eyeTrackingDevices);
        foreach (XRInputDevice device in eyeTrackingDevices)
        {
            if (!device.isValid)
            {
                continue;
            }

            eyeTrackingDevice = device;
            return true;
        }

        return false;
    }

    private void PopulateCombinedData(ref EyeSample sample)
    {
        if (sample.leftOriginValid && sample.rightOriginValid)
        {
            sample.combinedOrigin = (sample.leftOrigin + sample.rightOrigin) * 0.5f;
            sample.combinedOriginValid = true;
        }
        else if (sample.leftOriginValid)
        {
            sample.combinedOrigin = sample.leftOrigin;
            sample.combinedOriginValid = true;
        }
        else if (sample.rightOriginValid)
        {
            sample.combinedOrigin = sample.rightOrigin;
            sample.combinedOriginValid = true;
        }

        if (sample.leftDirectionValid && sample.rightDirectionValid)
        {
            sample.combinedDirection = (sample.leftDirection.normalized + sample.rightDirection.normalized).normalized;
            sample.combinedDirectionValid = true;
        }
        else if (sample.leftDirectionValid)
        {
            sample.combinedDirection = sample.leftDirection.normalized;
            sample.combinedDirectionValid = true;
        }
        else if (sample.rightDirectionValid)
        {
            sample.combinedDirection = sample.rightDirection.normalized;
            sample.combinedDirectionValid = true;
        }
    }

    private void PopulateFocusData(ref EyeSample sample)
    {
        if (!sample.combinedOriginValid || !sample.combinedDirectionValid || Camera.main == null)
        {
            return;
        }

        Transform trackingSpace = GetTrackingSpace(Camera.main.transform);
        Vector3 worldOrigin = trackingSpace.TransformPoint(sample.combinedOrigin);
        Vector3 worldDirection = trackingSpace.TransformDirection(sample.combinedDirection).normalized;

        sample.worldGazeOrigin = worldOrigin;
        sample.worldGazeDirection = worldDirection;
        sample.worldGazeValid = true;

        if (Physics.Raycast(worldOrigin, worldDirection, out RaycastHit hit, Mathf.Infinity))
        {
            Debug.DrawRay(worldOrigin, worldDirection * hit.distance, Color.yellow);
            sample.focusPoint = hit.point;
            sample.focusDistance = hit.distance;
            sample.focusObject = hit.collider.gameObject.name;
            sample.focusPointValid = true;
        }
    }

    private Transform GetTrackingSpace(Transform mainCameraTransform)
    {
        if (mainCameraTransform.parent != null && mainCameraTransform.parent.parent != null)
        {
            return mainCameraTransform.parent.parent;
        }

        if (mainCameraTransform.parent != null)
        {
            return mainCameraTransform.parent;
        }

        return mainCameraTransform;
    }

    private void UpdateDebugCursor(EyeSample sample)
    {
        if (!showDebugGazeCursor)
        {
            SetDebugCursorActive(false);
            return;
        }

        if (!TryGetDebugCursorPosition(sample, out Vector3 cursorPosition))
        {
            SetDebugCursorActive(false);
            return;
        }

        EnsureDebugCursor();
        if (debugCursorObject == null)
        {
            return;
        }

        debugCursorObject.transform.position = cursorPosition;
        debugCursorObject.transform.localScale = Vector3.one * debugCursorScale;
        debugCursorObject.transform.rotation = Quaternion.identity;

        if (debugCursorMaterial != null)
        {
            debugCursorMaterial.color = sample.worldGazeValid ? debugCursorColor : debugCursorUnavailableColor;
        }

        SetDebugCursorActive(true);
    }

    private bool TryGetDebugCursorPosition(EyeSample sample, out Vector3 cursorPosition)
    {
        cursorPosition = default;

        if (sample.focusPointValid)
        {
            cursorPosition = sample.focusPoint;
            return true;
        }

        if (sample.worldGazeValid)
        {
            cursorPosition = sample.worldGazeOrigin + (sample.worldGazeDirection * debugCursorDistance);
            return true;
        }

        return false;
    }

    private void EnsureDebugCursor()
    {
        if (debugCursorObject != null)
        {
            return;
        }

        debugCursorObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        debugCursorObject.name = "EyeTrackingDebugCursor";

        Collider cursorCollider = debugCursorObject.GetComponent<Collider>();
        if (cursorCollider != null)
        {
            Destroy(cursorCollider);
        }

        debugCursorRenderer = debugCursorObject.GetComponent<Renderer>();
        if (debugCursorRenderer != null)
        {
            Shader unlitShader = Shader.Find("Unlit/Color");
            if (unlitShader != null)
            {
                debugCursorMaterial = new Material(unlitShader);
                debugCursorRenderer.material = debugCursorMaterial;
            }
            else
            {
                debugCursorMaterial = debugCursorRenderer.material;
            }
        }

        SetDebugCursorActive(false);
    }

    private void SetDebugCursorActive(bool isActive)
    {
        if (debugCursorObject != null && debugCursorObject.activeSelf != isActive)
        {
            debugCursorObject.SetActive(isActive);
        }
    }

    private void DestroyDebugCursor()
    {
        if (debugCursorObject != null)
        {
            Destroy(debugCursorObject);
            debugCursorObject = null;
        }

        if (debugCursorMaterial != null)
        {
            Destroy(debugCursorMaterial);
            debugCursorMaterial = null;
        }

        debugCursorRenderer = null;
    }

    #endregion

    #region Standalone CSV Logging

    private bool ShouldWriteStandaloneSample()
    {
        if (!writeStandaloneCsv)
        {
            return false;
        }

        if (standaloneSampleIntervalSeconds <= 0f)
        {
            return true;
        }

        if (Time.unscaledTime < nextStandaloneSampleTime)
        {
            return false;
        }

        nextStandaloneSampleTime = Time.unscaledTime + standaloneSampleIntervalSeconds;
        return true;
    }

    private void AppendStandaloneSample(EyeSample sample)
    {
        try
        {
            EnsureStandaloneWriter();
            if (standaloneWriter == null)
            {
                return;
            }

            standaloneWriter.WriteLine(string.Join(",", new[]
            {
                FormatFloat(Time.time),
                FormatFloat(sample.leftOrigin.x),
                FormatFloat(sample.leftOrigin.y),
                FormatFloat(sample.leftOrigin.z),
                FormatFloat(sample.leftDirection.x),
                FormatFloat(sample.leftDirection.y),
                FormatFloat(sample.leftDirection.z),
                FormatFloat(sample.rightOrigin.x),
                FormatFloat(sample.rightOrigin.y),
                FormatFloat(sample.rightOrigin.z),
                FormatFloat(sample.rightDirection.x),
                FormatFloat(sample.rightDirection.y),
                FormatFloat(sample.rightDirection.z),
                FormatFloat(sample.combinedOrigin.x),
                FormatFloat(sample.combinedOrigin.y),
                FormatFloat(sample.combinedOrigin.z),
                FormatFloat(sample.combinedDirection.x),
                FormatFloat(sample.combinedDirection.y),
                FormatFloat(sample.combinedDirection.z),
                FormatFloat(sample.leftOpenness),
                FormatFloat(sample.rightOpenness),
                FormatFloat(sample.leftPupilDiameter),
                FormatFloat(sample.rightPupilDiameter),
                FormatFloat(sample.focusPoint.x),
                FormatFloat(sample.focusPoint.y),
                FormatFloat(sample.focusPoint.z),
                FormatFloat(sample.focusDistance),
                EscapeCsv(sample.focusObject),
                EscapeCsv(sample.trackingProvider)
            }));
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Failed to write standalone eye tracking sample to '{standaloneLogPath}': {exception.Message}", this);
            CloseStandaloneWriter();
        }
    }

    private void EnsureStandaloneWriter()
    {
        if (standaloneWriter != null)
        {
            return;
        }

        string fileSafeObjectName = string.IsNullOrWhiteSpace(objectName)
            ? gameObject.name.Replace(" ", "_").ToLowerInvariant()
            : objectName;

        string directory = ResolveStandaloneOutputDirectory();

        standaloneLogPath = Path.Combine(
            directory,
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}_{1:yyyyMMdd_HHmmss}.csv",
                fileSafeObjectName,
                DateTime.Now));

        standaloneWriter = new StreamWriter(standaloneLogPath, false)
        {
            AutoFlush = true
        };

        standaloneWriter.WriteLine("time," + string.Join(",", CustomHeader));
        Debug.Log($"Writing standalone eye tracking samples to: {standaloneLogPath}", this);
    }

    private string ResolveStandaloneOutputDirectory()
    {
        string downloadsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "Water_Morris_Eye_Tracking_Data");

        Directory.CreateDirectory(downloadsDirectory);
        return downloadsDirectory;
    }

    private void CloseStandaloneWriter()
    {
        if (standaloneWriter == null)
        {
            return;
        }

        standaloneWriter.Flush();
        standaloneWriter.Dispose();
        standaloneWriter = null;
    }

    private static string FormatFloat(float value)
    {
        return float.IsNaN(value)
            ? "NaN"
            : value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string EscapeCsv(string value)
    {
        string safeValue = value ?? string.Empty;
        if (!safeValue.Contains(",") && !safeValue.Contains("\"") && !safeValue.Contains("\n") && !safeValue.Contains("\r"))
        {
            return safeValue;
        }

        return string.Format(CultureInfo.InvariantCulture, "\"{0}\"", safeValue.Replace("\"", "\"\""));
    }

    #endregion
}
