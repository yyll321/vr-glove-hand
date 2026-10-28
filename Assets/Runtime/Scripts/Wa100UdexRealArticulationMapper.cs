using System;
using UnityEngine;

public class Wa100UdexRealArticulationMapper : MonoBehaviour
{
    public enum DriveAxis { X, Y, Z }
    [Serializable]
    public class JointMapping
    {
        public string JointName;
        public string LinkName;
        public ArticulationBody Joint;
        public DriveAxis Axis;
        public bool CountsForClosure=true;
        public string PrimaryKey;
        public string SecondaryKey;
        public float PrimaryScale = 1f;
        public float SecondaryScale;
        public float OffsetDegrees;
        public float LowerLimitDegrees;
        public float UpperLimitDegrees = 90f;
        public bool Enabled = true;
        public bool UseAbsoluteDelta = true;
        public float DeadZoneDegrees = 1.5f;
        [NonSerialized] public float NeutralPrimary;
        [NonSerialized] public float NeutralSecondary;
        [NonSerialized] public float CurrentTarget;
        [NonSerialized] public float RequestedTarget;
    }

    public Network Network;
    public bool AutoFindNetwork = true;
    public bool AutoBindJoints = true;
    public string CharacterName = string.Empty;

    [Range(0f, 1f)]
    public float DistalCoupling = 0.5f;

    public bool UseArticulationDriveLimits = true;
    public bool ConfigureDriveStrength = true;
    public float DriveStiffness = 10000f;
    public float DriveDamping = 100f;
    public float DriveForceLimit = 100f;
    public bool AutoCalibrateNeutralOnFirstPacket = true;
    public bool IsNeutralCalibrated;
    public float ResponseScale = 1f;
    public float SmoothingSpeed = 18f;

    public JointMapping[] JointMappings;

    [Header("Debug")]
    public int AppliedFrameCount;
    public int BoundJointCount;
    public string LastStatus;
    [Header("HandDriver angle parity")]
    public bool MatchHandDriverAngles;
    public string ResolvedCharacterName;
    public string LastInputUtc;
    public bool HasFreshFrame {get;private set;}
    string[] frameKeys;float[] frameValues;

    private void Reset()
    {
        ResetMappingsToUdexRight();
    }

    private void Awake()
    {
        EnsureMappings();
        if(!MatchHandDriverAngles)UpgradeLegacyThumbMappings();
        AutoBind();
    }

    private void OnEnable()
    {
        if (AutoFindNetwork && Network == null)
        {
            Network = FindObjectOfType<Network>();
        }
    }

    private void FixedUpdate()
    {
        if(MatchHandDriverAngles){Apply();return;}
        if (AutoCalibrateNeutralOnFirstPacket && !IsNeutralCalibrated && HasNetworkData())
        {
            CalibrateNeutral();
        }

        Apply();
    }

    [ContextMenu("Reset Mappings To Udex Right")]
    public void ResetMappingsToUdexRight()
    {
        MatchHandDriverAngles=false;frameKeys=null;
        JointMappings = new[]
        {
            SignedMap("Thumb_J1", "Thumb_L1", "r3", null, -1f, 0f, 0f, 90f, true),
            Map("Thumb_J2", "Thumb_L2", "r2", "r1", 1.15f, 1f, 0f, 120f, true),
            Map("Thumb_J3", "Thumb_L3", "r0", null, 1.6f, 0f, 0f, 180f, true),

            Map("Index_Finger_J1", "Index_Finger_L1", "r6", null, 1f, 0f, 0f, 90f, true),
            Map("Index_Finger_J2", "Index_Finger_L2", "r5", "r4", 1f, 1f, 0f, 90f, true),

            Map("Middle_Finger_J1", "Middle_Finger_L1", "r10", null, 1f, 0f, 0f, 90f, true),
            Map("Middle_Finger_J2", "Middle_Finger_L2", "r9", "r8", 1f, 1f, 0f, 90f, true),

            Map("Ring_Finger_J1", "Ring_Finger_L1", "r14", null, 1f, 0f, 0f, 90f, true),
            Map("Ring_Finger_J2", "Ring_Finger_L2", "r13", "r12", 1f, 1f, 0f, 90f, true),

            Map("Little_Finger_J1", "Little_Finger_L1", "r18", null, 1f, 0f, 0f, 90f, true),
            Map("Little_Finger_J2", "Little_Finger_L2", "r17", "r16", 1f, 1f, 0f, 90f, true),
        };
        IsNeutralCalibrated = false;
    }

    [ContextMenu("Use HandDriver Signed Angles")]
    public void UseHandDriverMappings()
    {
        MatchHandDriverAngles=true;AutoCalibrateNeutralOnFirstPacket=false;IsNeutralCalibrated=false;
        DistalCoupling=0;ResponseScale=1;
        JointMappings=new[]{
            SignedMap("Thumb_J1_Yaw","Thumb_L1","r3",null,-1,0,-45,45,true),
            SignedMap("Thumb_J1_Pitch","Thumb_CMC_PitchPivot","r2",null,.6f,0,0,65,true),
            SignedMap("Thumb_J2","Thumb_L2","r1",null,1,0,0,89.95438f,true),
            SignedMap("Thumb_J3","Thumb_L3","r0",null,-1,0,-89.95438f,0,true),
            SignedMap("Index_Finger_J1","Index_Finger_L1","r6",null,1,0,0,89.95438f,true),
            SignedMap("Index_Finger_J2","Index_Finger_L2","r5",null,1,0,0,89.95438f,true),
            SignedMap("Middle_Finger_J1","Middle_Finger_L1","r10",null,1,0,0,89.95438f,true),
            SignedMap("Middle_Finger_J2","Middle_Finger_L2","r9",null,1,0,0,89.95438f,true),
            SignedMap("Ring_Finger_J1","Ring_Finger_L1","r14",null,1,0,0,89.95438f,true),
            SignedMap("Ring_Finger_J2","Ring_Finger_L2","r13",null,1,0,0,89.95438f,true),
            SignedMap("Little_Finger_J1","Little_Finger_L1","r18",null,1,0,0,89.95438f,true),
            SignedMap("Little_Finger_J2","Little_Finger_L2","r17",null,1,0,0,89.95438f,true)};
        JointMappings[0].CountsForClosure=false;
        foreach(var m in JointMappings)m.DeadZoneDegrees=.5f;
        ResolvedCharacterName=LastInputUtc=null;frameKeys=null;
        AutoBind();
    }

    public static ArticulationDrive GetDrive(JointMapping m)=>m.Axis==DriveAxis.Y?m.Joint.yDrive:m.Axis==DriveAxis.Z?m.Joint.zDrive:m.Joint.xDrive;
    public static void SetDrive(JointMapping m,ArticulationDrive drive)
    {if(m.Axis==DriveAxis.Y)m.Joint.yDrive=drive;else if(m.Axis==DriveAxis.Z)m.Joint.zDrive=drive;else m.Joint.xDrive=drive;}
    public static Vector3 AxisVector(JointMapping m)=>m.Axis==DriveAxis.Y?Vector3.up:m.Axis==DriveAxis.Z?Vector3.forward:Vector3.right;
    public static int DofIndex(JointMapping m)
    {
        if(m.Joint.jointType!=ArticulationJointType.SphericalJoint)return 0;
        int index=0;
        if(m.Axis==DriveAxis.X)return 0;
        if(m.Joint.twistLock!=ArticulationDofLock.LockedMotion)index++;
        if(m.Axis==DriveAxis.Y)return index;
        if(m.Joint.swingYLock!=ArticulationDofLock.LockedMotion)index++;
        return index;
    }
    [ContextMenu("Reconnect HandDriver Role")]
    public void ReconnectHandDriverRole()
    {ResolvedCharacterName=null;LastInputUtc=null;HasFreshFrame=false;frameKeys=null;LastStatus="Waiting for complete HandDriver frame";}

    [ContextMenu("Auto Bind")]
    public void AutoBind()
    {
        EnsureMappings();

        if (!AutoBindJoints || JointMappings == null)
        {
            return;
        }

        BoundJointCount = 0;
        for (int i = 0; i < JointMappings.Length; i++)
        {
            JointMapping mapping = JointMappings[i];
            if (mapping == null)
            {
                continue;
            }

            if (mapping.Joint == null)
            {
                Transform link = FindDeepChild(transform, mapping.LinkName);
                if (link != null)
                {
                    mapping.Joint = link.GetComponent<ArticulationBody>();
                }
            }

            if (mapping.Joint != null)
            {
                BoundJointCount++;
                ConfigureDrive(mapping.Joint);
                if (UseArticulationDriveLimits)
                {
                    ArticulationDrive drive = GetDrive(mapping);
                    mapping.LowerLimitDegrees = drive.lowerLimit;
                    mapping.UpperLimitDegrees = drive.upperLimit;
                }
            }
        }

        LastStatus = $"Bound {BoundJointCount}/{JointMappings.Length} WA100 joints";
    }

    [ContextMenu("Apply Once")]
    public void Apply()
    {
        if (Network == null || JointMappings == null)
        {
            HasFreshFrame=false;
            return;
        }

        if(MatchHandDriverAngles)
        {
            if(frameKeys==null)
            {
                var keys=new System.Collections.Generic.List<string>();
                foreach(var m in JointMappings)if(m!=null){if(!string.IsNullOrEmpty(m.PrimaryKey)&&!keys.Contains(m.PrimaryKey))keys.Add(m.PrimaryKey);if(!string.IsNullOrEmpty(m.SecondaryKey)&&!keys.Contains(m.SecondaryKey))keys.Add(m.SecondaryKey);}
                frameKeys=keys.ToArray();
            }
            string role=string.IsNullOrEmpty(CharacterName)?ResolvedCharacterName:CharacterName;
            HasFreshFrame=Network.TryGetAngleSnapshot(role,frameKeys,out frameValues,out var found,out var utc) && DateTime.TryParse(utc,out var stamp) && (DateTime.UtcNow-stamp.ToUniversalTime()).TotalSeconds<1;
            if(!HasFreshFrame){LastStatus="Holding pose: complete fresh frame from pinned glove unavailable";return;}
            ResolvedCharacterName=found;LastInputUtc=utc;
        }
        int applied = 0;
        for (int i = 0; i < JointMappings.Length; i++)
        {
            JointMapping mapping = JointMappings[i];
            if (mapping == null || mapping.Joint == null)
            {
                continue;
            }

            float target = mapping.Enabled ? CalculateTarget(mapping) : 0f;
            target = Mathf.Clamp(target, mapping.LowerLimitDegrees, mapping.UpperLimitDegrees);
            mapping.RequestedTarget=target;

            if (SmoothingSpeed > 0f)
            {
                float t = 1f - Mathf.Exp(-SmoothingSpeed * Time.fixedDeltaTime);
                mapping.CurrentTarget = Mathf.Lerp(mapping.CurrentTarget, target, t);
            }
            else
            {
                mapping.CurrentTarget = target;
            }

            ArticulationDrive drive = GetDrive(mapping);
            drive.target = mapping.CurrentTarget;
            SetDrive(mapping,drive);
            applied++;
        }

        AppliedFrameCount++;
        LastStatus = MatchHandDriverAngles ? $"HandDriver {ResolvedCharacterName}: applied {applied}/{JointMappings.Length} axes" : $"Applied {applied}/{JointMappings.Length} WA100 joints";
    }

    public void EnsureMappings()
    {
        if (JointMappings == null || JointMappings.Length == 0)
        {
            ResetMappingsToUdexRight();
        }
    }

    private void UpgradeLegacyThumbMappings()
    {
        if (JointMappings == null)
        {
            return;
        }

        for (int i = 0; i < JointMappings.Length; i++)
        {
            JointMapping mapping = JointMappings[i];
            if (mapping == null)
            {
                continue;
            }

            if (mapping.JointName == "Thumb_J2" &&
                Mathf.Approximately(mapping.PrimaryScale, 0.65f) &&
                Mathf.Approximately(mapping.SecondaryScale, 0.35f))
            {
                mapping.PrimaryScale = 1.15f;
                mapping.SecondaryScale = 1f;
            }
            else if (mapping.JointName == "Thumb_J3" &&
                     Mathf.Approximately(mapping.PrimaryScale, 1f))
            {
                mapping.PrimaryScale = 1.6f;
            }
        }
    }

    [ContextMenu("Calibrate Neutral From Current Glove Pose")]
    public void CalibrateNeutral()
    {
        if(MatchHandDriverAngles){LastStatus="Using HandDriver calibrated angles; no first-packet zero subtraction";return;}
        EnsureMappings();

        if (JointMappings == null)
        {
            return;
        }

        for (int i = 0; i < JointMappings.Length; i++)
        {
            JointMapping mapping = JointMappings[i];
            if (mapping == null)
            {
                continue;
            }

            mapping.NeutralPrimary = ReadAngle(mapping.PrimaryKey);
            mapping.NeutralSecondary = ReadAngle(mapping.SecondaryKey);
            mapping.CurrentTarget = 0f;
        }

        IsNeutralCalibrated = true;
        LastStatus = "Neutral pose calibrated";
    }

    private bool HasNetworkData()
    {
        return Network != null && Network.ReceivedPacketCount > 0;
    }

    private float CalculateTarget(JointMapping mapping)
    {
        float primary = ReadAngle(mapping.PrimaryKey) - (MatchHandDriverAngles?0:mapping.NeutralPrimary);
        float secondary = 0f;

        if (!string.IsNullOrWhiteSpace(mapping.SecondaryKey))
        {
            secondary = (ReadAngle(mapping.SecondaryKey) - mapping.NeutralSecondary) * DistalCoupling;
        }

        float value = primary * mapping.PrimaryScale + secondary * mapping.SecondaryScale;
        if (mapping.UseAbsoluteDelta)
        {
            value = Mathf.Abs(value);
        }

        value *= ResponseScale;

        if (Mathf.Abs(value) < mapping.DeadZoneDegrees)
        {
            value = 0f;
        }

        if (mapping.UseAbsoluteDelta && mapping.LowerLimitDegrees < 0f && mapping.UpperLimitDegrees <= 0f)
        {
            value = -Mathf.Abs(value);
        }
        else if (!mapping.UseAbsoluteDelta && mapping.LowerLimitDegrees < 0f && mapping.UpperLimitDegrees <= 0f)
        {
            value = Mathf.Min(value, 0f);
        }

        return value + mapping.OffsetDegrees;
    }

    private void ConfigureDrive(ArticulationBody body)
    {
        if (!ConfigureDriveStrength || body == null || body.jointType == ArticulationJointType.FixedJoint)
        {
            return;
        }

        ArticulationDrive drive = body.xDrive;
        drive.stiffness = DriveStiffness;
        drive.damping = DriveDamping;
        drive.forceLimit = DriveForceLimit;
        body.xDrive = drive;
    }

    private float ReadAngle(string key)
    {
        if (Network == null || string.IsNullOrWhiteSpace(key))
        {
            return 0f;
        }

        if(MatchHandDriverAngles && frameKeys!=null && frameValues!=null)
        {int index=Array.IndexOf(frameKeys,key);return index>=0?frameValues[index]:0;}
        return Network.Convert2Angle(CharacterName, key);
    }

    private static JointMapping Map(
        string jointName,
        string linkName,
        string primaryKey,
        string secondaryKey,
        float primaryScale,
        float secondaryScale,
        float lowerLimitDegrees,
        float upperLimitDegrees,
        bool enabled)
    {
        return new JointMapping
        {
            JointName = jointName,
            LinkName = linkName,
            PrimaryKey = primaryKey,
            SecondaryKey = secondaryKey,
            PrimaryScale = primaryScale,
            SecondaryScale = secondaryScale,
            LowerLimitDegrees = lowerLimitDegrees,
            UpperLimitDegrees = upperLimitDegrees,
            Enabled = enabled
        };
    }

    private static JointMapping SignedMap(
        string jointName,
        string linkName,
        string primaryKey,
        string secondaryKey,
        float primaryScale,
        float secondaryScale,
        float lowerLimitDegrees,
        float upperLimitDegrees,
        bool enabled)
    {
        JointMapping mapping = Map(
            jointName,
            linkName,
            primaryKey,
            secondaryKey,
            primaryScale,
            secondaryScale,
            lowerLimitDegrees,
            upperLimitDegrees,
            enabled);
        mapping.UseAbsoluteDelta = false;
        mapping.DeadZoneDegrees = 1f;
        return mapping;
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
        {
            return null;
        }

        if (root.name == childName)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), childName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
