using UnityEngine;
using VIVE.OpenXR;
using VIVE.OpenXR.EyeTracker;

public class UpdateRightPupil : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        XR_HTC_eye_tracker.Interop.GetEyePupilData(out XrSingleEyePupilDataHTC[] out_pupils);
        XrSingleEyePupilDataHTC rightPupil = out_pupils[(int)XrEyePositionHTC.XR_EYE_POSITION_RIGHT_HTC];
        if(rightPupil.isDiameterValid)
        {
            float rightPupilDiameter = rightPupil.pupilDiameter;
            //Do something
        }
        if(rightPupil.isPositionValid)
        {
            XrVector2f rightPupilPosition =  rightPupil.pupilPosition;
            //Do something
        }
    }
}
