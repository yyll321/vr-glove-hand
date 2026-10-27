using UnityEngine;
using UnityEngine.XR;

// Joint axes follow JXB_angles_input.SetAngle: Y, Z, Z, Y.
// Solve against the actual scene links so the user's mounting pose is preserved.
[DefaultExecutionOrder(100)]
public sealed class PicoArmPoseDriver : MonoBehaviour
{
    public UnderwaterBaseline.UnderwaterMotor Motor;
    public bool SeabedArea;
    public Transform TrackingOrigin;
    public Transform ArmBase;
    public Transform Wrist;
    public Transform[] Joints;
    public Behaviour[] PreviousControllers;
    public bool ControlEnabled = true;
    [Min(0.01f)] public float PositionScale = 1f;
    [Min(1f)] public float DegreesPerSecond = 90f;
    [Range(1, 24)] public int SolverIterations = 12;
    [Header("Reach and contact")]
    [Tooltip("Layers containing the pool floor, rocks and seabed. The arm visuals may keep their own colliders disabled.")]
    public LayerMask ContactMask = 1 << 8;
    public Wa100SurfaceContact Contact;
    [Range(0.001f, 0.2f)] public float Damping = 0.035f;
    [Range(0.1f, 1f)] public float PositionGain = 0.8f;
    [Range(0.5f, 20f)] public float MaxJointStepDegrees = 7f;
    [Min(0)] public float TargetSmoothingSeconds = 0.035f;
    [Tooltip("Software excursion limits in degrees relative to the saved mounting pose; not calibrated hardware limits.")]
    public Vector4 MinimumAngles = new Vector4(-170,-170,-170,-170);
    public Vector4 MaximumAngles = new Vector4(170,170,170,170);
    public bool PreventSurfacePenetration = true;
    public string Status = "Hold right grip to control arm";
    public float PositionError;
    public float OrientationErrorDegrees { get; private set; }
    public float RequestedPositionError { get; private set; }
    public float MaximumAppliedJointStep { get; private set; }
    public bool SurfaceContact { get; private set; }
    public Vector3 LastContactNormal { get; private set; } = Vector3.up;

    bool engaged;
    bool ownsArm;
    bool[] previousEnabled;
    Vector3 controllerAnchor;
    Vector3 wristAnchor;
    Quaternion controllerRotationAnchor;
    Quaternion wristRotationAnchor;
    Vector3 previousControllerPosition;
    float nextDiagnostic;
    readonly Vector3[] jacobian = new Vector3[3];
    readonly Quaternion[] neutral = new Quaternion[4], frameStart = new Quaternion[4], proposed = new Quaternion[4];
    readonly float[] angles = new float[4], startAngles = new float[4], candidateAngles = new float[4];
    bool initialized;
    Vector3 filteredTarget;
    Quaternion filteredRotation;
    public float GripValue { get; private set; }
    public bool GripHeld { get; private set; }
    public bool ControllerTracked { get; private set; }
    public int AppliedPoseCount { get; private set; }

    public void ResolveBindings()
    {
        if (ArmBase == null && Joints != null && Joints.Length > 0 && Joints[0] != null)
            ArmBase = Joints[0].parent;
        if (!Contact) Contact = GetComponent<Wa100SurfaceContact>();
        if (!initialized && Joints != null && Joints.Length == 4 && !System.Array.Exists(Joints,j=>!j))
        {
            for (int i=0;i<4;i++) neutral[i]=Joints[i].localRotation;
            initialized = true;
        }
    }

    void Awake()
    {
        ResolveBindings();
        SurfaceContact = false;
        if (!TrackingOrigin || !ArmBase || !Wrist || !initialized || !ControlEnabled) return;
        // Prevent the legacy controller's idle pose reset on APK startup.
        if (Application.platform == RuntimePlatform.Android) TakeOwnership();
    }

    void LateUpdate()
    {
        bool pico = Motor != null && Motor.XrActive && Motor.InSeabed==SeabedArea;
        if (!pico)
        {
            engaged = false;
            if (Application.platform != RuntimePlatform.Android) ReleaseOwnership();
            Status = "PICO controller input inactive";
            return;
        }
        TakeOwnership();
        if (!ControlEnabled || TrackingOrigin == null || ArmBase == null || Wrist == null ||
            Joints == null || Joints.Length != 4 || System.Array.Exists(Joints, j => j == null))
        {
            engaged = false;
            Status = "Arm control disabled or bindings missing";
            return;
        }

        var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        ControllerTracked = false; GripHeld = false; GripValue = 0;
        if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked ||
            !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 localPosition) ||
            !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion localRotation))
        {
            engaged = false;
            Status = "Right controller tracking unavailable; arm held";
            return;
        }
        ControllerTracked = true;
        device.TryGetFeatureValue(CommonUsages.grip, out float analogGrip);
        bool grip = Motor.Controllers!=null && Motor.Controllers.RightGrip;
        GripValue = analogGrip; GripHeld = grip;
        ApplyControllerPose(localPosition, localRotation, grip, Time.unscaledDeltaTime);
        if (Time.unscaledTime >= nextDiagnostic)
        {
            nextDiagnostic = Time.unscaledTime + 2;
            Debug.Log($"[UEM Arm] device={device.name} grip={grip}/{analogGrip:F2} frames={AppliedPoseCount} wrist={Wrist.position:F3} error={PositionError:F3} status={Status}");
        }
    }

    // The same path is exercised by the editor's rig regression check.
    public void ApplyControllerPose(Vector3 localPosition, Quaternion localRotation, bool grip, float deltaTime)
    {
        ResolveBindings();
        if (!grip)
        {
            engaged = false;
            Status = "Hold RIGHT GRIP to move / twist wrist; release to hold";
            return;
        }
        // XR poses are local to the tracking origin, not Unity world coordinates.
        Vector3 controllerPosition = ArmBase.InverseTransformPoint(TrackingOrigin.TransformPoint(localPosition));
        Quaternion controllerRotation = Quaternion.Inverse(ArmBase.rotation) * TrackingOrigin.rotation * localRotation;
        if (!engaged || Vector3.Distance(controllerPosition, previousControllerPosition) > 0.25f)
        {
            controllerAnchor = controllerPosition;
            controllerRotationAnchor = controllerRotation;
            wristAnchor = ArmBase.InverseTransformPoint(Wrist.position);
            wristRotationAnchor = Quaternion.Inverse(ArmBase.rotation) * Wrist.rotation;
            previousControllerPosition = controllerPosition;
            engaged = true;
            filteredTarget = Wrist.position;
            filteredRotation = Wrist.rotation;
            Status = "Arm calibrated at current pose";
            return;
        }
        previousControllerPosition = controllerPosition;
        Vector3 target = ArmBase.TransformPoint(wristAnchor + (controllerPosition - controllerAnchor) * PositionScale);
        Quaternion targetRotation = ArmBase.rotation * controllerRotation *
                                    Quaternion.Inverse(controllerRotationAnchor) * wristRotationAnchor;
        float dt = Mathf.Clamp(deltaTime,0,0.05f);
        float blend = TargetSmoothingSeconds>0 ? 1-Mathf.Exp(-dt/TargetSmoothingSeconds) : 1;
        filteredTarget = Vector3.Lerp(filteredTarget,target,blend);
        filteredRotation = Quaternion.Slerp(filteredRotation,targetRotation,blend);
        CaptureJointState();
        if (PreventSurfacePenetration && Contact) { Contact.SurfaceMask=ContactMask; Contact.BeginMotion(); }
        SolvePosition(filteredTarget, dt);
        SolveWristRotation(filteredRotation, dt);
        ApplyContactConstraint();
        AppliedPoseCount++;
        PositionError = Vector3.Distance(Wrist.position,filteredTarget);
        RequestedPositionError = Vector3.Distance(Wrist.position,target);
        OrientationErrorDegrees = Quaternion.Angle(Wrist.rotation,targetRotation);
        if(SurfaceContact)
        {
            // Contact is a clutch: discard the rejected motion instead of accumulating
            // an unreachable target below the floor. Lifting the controller can withdraw
            // immediately, without first undoing all the previous pushing distance.
            controllerAnchor=controllerPosition;
            controllerRotationAnchor=controllerRotation;
            wristAnchor=ArmBase.InverseTransformPoint(Wrist.position);
            wristRotationAnchor=Quaternion.Inverse(ArmBase.rotation)*Wrist.rotation;
            filteredTarget=Wrist.position;filteredRotation=Wrist.rotation;
        }
        Status = SurfaceContact ? "Surface contact: hand motion blocked; right-hand pulse" :
            PositionError < 0.025f ? "Following position and available wrist twist (4 DOF)" : "Following; reach / joint-speed / software limit";
    }

    static Vector3 Axis(int i) => i==0 || i==3 ? Vector3.up : Vector3.forward;

    void CaptureJointState()
    {
        for(int i=0;i<4;i++)
        {
            frameStart[i]=Joints[i].localRotation;
            Quaternion q=Quaternion.Inverse(neutral[i])*frameStart[i];
            float projection=Vector3.Dot(new Vector3(q.x,q.y,q.z),Axis(i));
            angles[i]=startAngles[i]=Mathf.DeltaAngle(0,2*Mathf.Atan2(projection,q.w)*Mathf.Rad2Deg);
        }
    }

    void SetJointAngle(int i,float value,float frameBudget)
    {
        float lo=Mathf.Max(MinimumAngles[i],startAngles[i]-frameBudget);
        float hi=Mathf.Min(MaximumAngles[i],startAngles[i]+frameBudget);
        // If a user manually placed a link outside a software limit, approach the limit
        // at the configured speed instead of snapping to it in a single frame.
        if(lo>hi) { lo=startAngles[i]-frameBudget; hi=startAngles[i]+frameBudget; }
        angles[i]=Mathf.Clamp(value,lo,hi);
        Joints[i].localRotation=frameStart[i]*Quaternion.AngleAxis(angles[i]-startAngles[i],Axis(i));
    }

    void ApplyContactConstraint()
    {
        MaximumAppliedJointStep=0;
        for(int i=0;i<4;i++) proposed[i]=Joints[i].localRotation;
        if(PreventSurfacePenetration && Contact && Contact.ShapeCount>0)
        {
            for(int i=0;i<4;i++)Joints[i].localRotation=frameStart[i];
            Vector3 previous=Wrist.position;Quaternion previousRotation=Wrist.rotation;
            float greatest=0;for(int i=0;i<4;i++)greatest+=Quaternion.Angle(frameStart[i],proposed[i]);
            int steps=Mathf.Clamp(Mathf.CeilToInt(greatest/0.5f),1,80);
            // A pose already inside a contact skin may move out of it. Never snap the
            // entire robot or translate the head to repair a contact.
            bool startedBlocked=Contact.Blocked(previous,previousRotation);
            float accepted=0;
            for(int step=1;step<=steps;step++)
            {
                float fraction=step/(float)steps;
                for(int i=0;i<4;i++)Joints[i].localRotation=Quaternion.Slerp(frameStart[i],proposed[i],fraction);
                bool blocked=Contact.Blocked(Wrist.position,Wrist.rotation);
                // Allow withdrawal from a physical contact, but do not allow a new
                // overlap or sweep through a thin surface.
                if(blocked || (!startedBlocked && Contact.SweepBlocked(previous,previousRotation,Wrist.position)))
                {
                    SurfaceContact=true;LastContactNormal=Contact.Normal;
                    if(!startedBlocked || Vector3.Dot(Wrist.position-previous,Contact.Normal)<=0)
                    {
                        for(int i=0;i<4;i++)Joints[i].localRotation=Quaternion.Slerp(frameStart[i],proposed[i],accepted);
                        break;
                    }
                }
                accepted=fraction;previous=Wrist.position;previousRotation=Wrist.rotation;
                startedBlocked=blocked;
            }
            if(SurfaceContact)Contact.Pulse();
        }
        for(int i=0;i<4;i++)MaximumAppliedJointStep=Mathf.Max(MaximumAppliedJointStep,Quaternion.Angle(frameStart[i],Joints[i].localRotation));
    }

    void SolvePosition(Vector3 target, float deltaTime)
    {
        // Damped least-squares Jacobian solve. The old CCD update treated each joint
        // independently and could overshoot when the wrist approached a singular pose.
        // DLS distributes the Cartesian error across the real JXB Y-Z-Z axes and remains
        // well behaved near a straight arm or a surface constraint.
        float budget = Mathf.Max(0,DegreesPerSecond*deltaTime);
        float maxStep = Mathf.Min(MaxJointStepDegrees,budget);
        if(budget<=0)return;
        for (int iteration = 0; iteration < SolverIterations; iteration++)
        {
            Vector3 error = target - Wrist.position;
            if (error.sqrMagnitude < 0.000001f) break;

            for (int i = 0; i < 3; i++)
            {
                Transform joint = Joints[i];
                Vector3 localAxis = i == 0 ? Vector3.up : Vector3.forward;
                Vector3 worldAxis = joint.TransformDirection(localAxis);
                jacobian[i] = Vector3.Cross(worldAxis, Wrist.position - joint.position);
            }

            Matrix4x4 normal = Matrix4x4.zero;
            normal.m33=1; // Embed an invertible 3x3 system in a valid homogeneous matrix.
            float lambda2 = Damping * Damping;
            normal.m00 = normal.m11 = normal.m22 = lambda2;
            for (int i = 0; i < 3; i++)
            {
                Vector3 j = jacobian[i];
                normal.m00 += j.x * j.x; normal.m01 += j.x * j.y; normal.m02 += j.x * j.z;
                normal.m10 += j.y * j.x; normal.m11 += j.y * j.y; normal.m12 += j.y * j.z;
                normal.m20 += j.z * j.x; normal.m21 += j.z * j.y; normal.m22 += j.z * j.z;
            }
            Vector3 solved = normal.inverse.MultiplyVector(Vector3.ClampMagnitude(error,0.08f) * PositionGain);
            for(int i=0;i<3;i++)candidateAngles[i]=angles[i];
            float oldError=error.sqrMagnitude;
            // Backtracking prevents a nonlinear update from increasing Cartesian error.
            for(int line=0;line<6;line++)
            {
                for(int i=0;i<3;i++)
                {
                    float change=Mathf.Clamp(Vector3.Dot(jacobian[i],solved)*Mathf.Rad2Deg,-maxStep,maxStep)*Mathf.Pow(0.5f,line);
                    SetJointAngle(i,candidateAngles[i]+change,budget);
                }
                if((Wrist.position-target).sqrMagnitude<=oldError)break;
                for(int i=0;i<3;i++)SetJointAngle(i,candidateAngles[i],budget);
            }
        }
    }

    void SolveWristRotation(Quaternion target, float deltaTime)
    {
        // Exact swing/twist projection onto the real fourth joint axis.
        Transform wristJoint = Joints[3];
        Quaternion delta = target * Quaternion.Inverse(Wrist.rotation);
        float projection = Vector3.Dot(new Vector3(delta.x, delta.y, delta.z), wristJoint.up);
        if (projection * projection + delta.w * delta.w < 1e-10f) return;
        float twist = Mathf.DeltaAngle(0, 2 * Mathf.Atan2(projection, delta.w) * Mathf.Rad2Deg);
        SetJointAngle(3,angles[3]+twist,Mathf.Max(0,DegreesPerSecond*deltaTime));
    }

    void TakeOwnership()
    {
        if (ownsArm) return;
        previousEnabled = new bool[PreviousControllers == null ? 0 : PreviousControllers.Length];
        for (int i = 0; i < previousEnabled.Length; i++)
        {
            if (PreviousControllers[i] == null) continue;
            previousEnabled[i] = PreviousControllers[i].enabled;
            PreviousControllers[i].enabled = false;
        }
        ownsArm = true;
    }

    void ReleaseOwnership()
    {
        if (!ownsArm) return;
        for (int i = 0; i < previousEnabled.Length; i++)
            if (PreviousControllers[i] != null) PreviousControllers[i].enabled = previousEnabled[i];
        ownsArm = false;
    }

    void OnDisable()
    {
        engaged = false;
        ReleaseOwnership();
    }
}

