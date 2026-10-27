using UnityEngine;

namespace UnderwaterBaseline
{
    [DisallowMultipleComponent]
    public sealed class UnderwaterRadarSystem : MonoBehaviour
    {
        [Tooltip("Disable this GameObject to disable both scanners, displays, ROS interfaces and calibration target. ROV movement stays enabled.")]
        public UnderwaterGeometryRadar[] radars;
        public Transform calibrationTarget;
        public string CalibrationStatus {get;private set;}="5 m check: stop ROV, set tilt 0, then place target";
        public void ShowFiveMetreTarget(UnderwaterGeometryRadar radar)
        {
            if(!calibrationTarget || !radar)return;
            radar.RefreshMount();
            HideTarget();Physics.SyncTransforms();float nearest=5;
            foreach(var hit in Physics.RaycastAll(radar.transform.position,radar.transform.forward,5,radar.detectionMask,QueryTriggerInteraction.Ignore))
                if(!radar.ignoreRoot || !(hit.transform==radar.ignoreRoot || hit.transform.IsChildOf(radar.ignoreRoot)))nearest=Mathf.Min(nearest,hit.distance);
            calibrationTarget.SetPositionAndRotation(radar.transform.position+radar.transform.forward*5.06f,radar.transform.rotation);
            calibrationTarget.gameObject.SetActive(true);
            CalibrationStatus=nearest<4.98f?$"5 m target OCCLUDED: nearer obstacle at {nearest:F2} m":"5 m target placed: hold position; wait for complete scan";
        }
        public void HideTarget(){if(calibrationTarget)calibrationTarget.gameObject.SetActive(false);}
        void OnDisable(){HideTarget();}
    }
}
