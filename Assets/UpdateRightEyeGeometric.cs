using UnityEngine;
using VIVE.OpenXR;
using VIVE.OpenXR.EyeTracker;

public class UpdateRightEyeGeometric : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        XR_HTC_eye_tracker.Interop.GetEyeGeometricData(out XrSingleEyeGeometricDataHTC[] out_geometrics);
        XrSingleEyeGeometricDataHTC rightGeometric = out_geometrics[(int)XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC];
        if(rightGeometric.isValid)
        {
            float rightEyeOpenness = rightGeometric.eyeOpenness;
            float rightEyeeyeSqueeze = rightGeometric.eyeSqueeze;
            float rightEyeeyeWide = rightGeometric.eyeWide;
            //Do something
        }
    }
}
