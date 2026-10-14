using System;
using UnityEngine;

public class Wa100UdexRealArticulationMapper : MonoBehaviour
{
    [Serializable]
    public class JointMapping
    {
        public string JointName;
        public string LinkName;
        public ArticulationBody Joint;
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

    private void Reset()
    {
        ResetMappingsToUdexRight();
    }

    private void Awake()
    {
        EnsureMappings();
        UpgradeLegacyThumbMappings();
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
        if (AutoCalibrateNeutralOnFirstPacket && !IsNeutralCalibrated && HasNetworkData())
        {
            CalibrateNeutral();
        }

        Apply();
    }

    [ContextMenu("Reset Mappings To Udex Right")]
    public void ResetMappingsToUdexRight()
    {
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
                    ArticulationDrive drive = mapping.Joint.xDrive;
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
            return;
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

            if (SmoothingSpeed > 0f)
            {
                float t = 1f - Mathf.Exp(-SmoothingSpeed * Time.fixedDeltaTime);
                mapping.CurrentTarget = Mathf.Lerp(mapping.CurrentTarget, target, t);
            }
            else
            {
                mapping.CurrentTarget = target;
            }

            ArticulationDrive drive = mapping.Joint.xDrive;
            drive.target = mapping.CurrentTarget;
            mapping.Joint.xDrive = drive;
            applied++;
        }

        AppliedFrameCount++;
        LastStatus = $"Applied {applied}/{JointMappings.Length} WA100 joints";
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
        float primary = ReadAngle(mapping.PrimaryKey) - mapping.NeutralPrimary;
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
