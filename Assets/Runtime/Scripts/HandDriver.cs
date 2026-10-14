using NaughtyAttributes;
using System.Collections.Generic;
using TouchSocket.Core;
using UnityEngine;

public class HandDriver : MonoBehaviour
{
    public Network Network;

    public enum HandType
    {
        Left,
        Right
    }

    public enum Axis
    {
        x,
        y,
        z,
        x_n,
        y_n,
        z_n
    }

    public string CharacterName = string.Empty;
    public HandType Hand;
    private Dictionary<string, Quaternion> _originQuaternionDic;

    public Transform Thumb1;
    public Transform Thumb2;
    public Transform Thumb3;

    public Transform Index1;
    public Transform Index2;
    public Transform Index3;

    public Transform Middle1;
    public Transform Middle2;
    public Transform Middle3;

    public Transform Ring1;
    public Transform Ring2;
    public Transform Ring3;

    public Transform Pinky1;
    public Transform Pinky2;
    public Transform Pinky3;

    public Transform Wrist;

    [Header("[Axis OffSet]")] 
    public Axis Pitch = Axis.x;

    public Axis Roll = Axis.y;

    public Axis Yaw = Axis.z;

    [Header("[IMU(On/Off)]")] public bool HasIMU = false;

    [Header("[Thumb Root Coefficient]")] [Range(0, 1)] public float coefficient = 0.6f;

    [Header("[Thumb Root OffSet]")] public Vector3 Thumb1Offset;

    [Header("[App Options]")]
    public bool NeedRealTransfrom;

    //[HideInInspector]
    public bool UsingNetwork;

    public bool UsingAndroidService;

    [Header("[Vector3 Angles]")]
    public Vector3 thumb1;
    public Vector3 thumb2;
    public Vector3 thumb3;

    public Vector3 index1;
    public Vector3 index2;
    public Vector3 index3;

    public Vector3 middle1;
    public Vector3 middle2;
    public Vector3 middle3;

    public Vector3 ring1;
    public Vector3 ring2;
    public Vector3 ring3;

    public Vector3 pinky1;
    public Vector3 pinky2;
    public Vector3 pinky3;

    [Header("[Controller Values]")]
    public float Joy_X;
    public float Joy_Y;
    public bool Button_A;
    public bool Button_B;
    public bool Button_Joystick;
    public bool Button_Menu;
    public InputData inputData = new();

    [Header("[Vibration Control]")]
    public bool ISController20;
    public string SendBackIP;
    public VibrationData vibrationData;

    [HideIf("ISController20"), BoxGroup("Vibrator 1"), Min(0), Label("Million Second")]
    public int Duration1 = 20;
    [HideIf("ISController20"), BoxGroup("Vibrator 1"), Range(4, 10)]
    public int Amplitude1 = 4;
    [HideIf("ISController20"), Button]
    public void Vibrator_1Active()
    {
        if (Network == null) return;
        SingleVirbator[] Virbators;
        if (Hand == HandType.Left)
        {
            Virbators = new SingleVirbator[2]
            {
                new SingleVirbator(1, Duration1, Amplitude1),
                new SingleVirbator()
            };
            vibrationData = new VibrationData(Virbators);
        }
        else
        {
            Virbators = new SingleVirbator[2]
            {
                new SingleVirbator(),
                new SingleVirbator(1, Duration1, Amplitude1),
            };
            vibrationData = new VibrationData(Virbators);
        }
        Network.SendVibrationMsg(CharacterName, SendBackIP, vibrationData);
    }

    [HideIf("ISController20"), BoxGroup("Vibrator 2"), Min(0), Label("Million Second")]
    public int Duration2 = 20;
    [HideIf("ISController20"), BoxGroup("Vibrator 2"), Range(4, 10)]
    public int Amplitude2 = 4;
    [HideIf("ISController20"), Button]
    public void Vibrator_2Active()
    {
        if (Network == null) return;
        SingleVirbator[] Virbators;
        if (Hand == HandType.Left)
        {
            Virbators = new SingleVirbator[2]
            {
                new SingleVirbator(2, Duration2, Amplitude2),
                new SingleVirbator()
            };
            vibrationData = new VibrationData(Virbators);
        }
        else
        {
            Virbators = new SingleVirbator[2]
            {
                new SingleVirbator(),
                new SingleVirbator(2, Duration2, Amplitude2),
            };
            vibrationData = new VibrationData(Virbators);
        }
        Network.SendVibrationMsg(CharacterName, SendBackIP, vibrationData);
    }
    [HideIf("ISController20"), Button]
    public void BothActiveWithVibrator_1Parameters()
    {
        if (Network == null) return;
        SingleVirbator[] Virbators;
        if (Hand == HandType.Left)
        {
            Virbators = new SingleVirbator[2]
            {
                new SingleVirbator(3, Duration1, Amplitude1),
                new SingleVirbator()
            };
            vibrationData = new VibrationData(Virbators);
        }
        else
        {
            Virbators = new SingleVirbator[2]
            {
                new SingleVirbator(),
                new SingleVirbator(3, Duration1, Amplitude1),
            };
            vibrationData = new VibrationData(Virbators);
        }
        Network.SendVibrationMsg(CharacterName, SendBackIP, vibrationData);
    }

    [ShowIf("ISController20"), BoxGroup("Vibrator"), Range(40, 30000), Label("Million Second")]
    public int Duration = 200;
    [ShowIf("ISController20"), BoxGroup("Vibrator"), Range(1, 1000)]
    public int Amplitude = 1000;
    [ShowIf("ISController20"), BoxGroup("Vibrator"), Range(7, 3000)]
    public int Rate = 2000;

    [ShowIf("ISController20"), Button]
    public void Vibrator_20_Active()
    {
        if (Network == null) return;
        SingleVirbator_20[] Virbator_20;
        if (Hand == HandType.Left)
        {
            Virbator_20 = new SingleVirbator_20[2]
            {
                new SingleVirbator_20(1,Duration, Amplitude,Rate),
                new SingleVirbator_20()
            };
            vibrationData = new VibrationData(Virbator_20);
        }
        else
        {
            Virbator_20 = new SingleVirbator_20[2]
            {
                new SingleVirbator_20(),
                new SingleVirbator_20(1,Duration, Amplitude,Rate)
            };
            vibrationData = new VibrationData(Virbator_20);
        }
        Network.SendVibrationMsg(CharacterName, SendBackIP, vibrationData, true);
    }


    // Start is called before the first frame update
    void Start()
    {
        if (UsingAndroidService || UsingNetwork)
        {
            Network = GetOrCreateNetwork();
        }

        _originQuaternionDic = new Dictionary<string, Quaternion>();
        InitJoints();

        if (string.IsNullOrEmpty(SendBackIP) || UsingAndroidService)
        {
            SendBackIP = "127.0.0.1";
        }

        var Virbators = new SingleVirbator[2]
        {
            new SingleVirbator(0, 0, 1),
            new SingleVirbator(0, 0, 1)
        };

        vibrationData = new VibrationData(Virbators);

        if(UsingAndroidService)
        {
            CharacterName = "AndroidService";
        }
    }

    private Network GetOrCreateNetwork()
    {
        GameObject networkObject = GameObject.Find("Network");
        if (networkObject == null)
        {
            networkObject = new GameObject("Network");
        }

        Network network = networkObject.GetComponent<Network>();
        if (network == null)
        {
            network = networkObject.AddComponent<Network>();
        }

        return network;
    }

    private void InitJoints()
    {
        _originQuaternionDic.AddOrUpdate(Thumb1.name, Thumb1.localRotation);
        _originQuaternionDic.AddOrUpdate(Thumb2.name, Thumb2.localRotation);
        _originQuaternionDic.AddOrUpdate(Thumb3.name, Thumb3.localRotation);

        _originQuaternionDic.AddOrUpdate(Index1.name, Index1.localRotation);
        _originQuaternionDic.AddOrUpdate(Index2.name, Index2.localRotation);
        _originQuaternionDic.AddOrUpdate(Index3.name, Index3.localRotation);

        _originQuaternionDic.AddOrUpdate(Middle1.name, Middle1.localRotation);
        _originQuaternionDic.AddOrUpdate(Middle2.name, Middle2.localRotation);
        _originQuaternionDic.AddOrUpdate(Middle3.name, Middle3.localRotation);

        _originQuaternionDic.AddOrUpdate(Ring1.name, Ring1.localRotation);
        _originQuaternionDic.AddOrUpdate(Ring2.name, Ring2.localRotation);
        _originQuaternionDic.AddOrUpdate(Ring3.name, Ring3.localRotation);

        _originQuaternionDic.AddOrUpdate(Pinky1.name, Pinky1.localRotation);
        _originQuaternionDic.AddOrUpdate(Pinky2.name, Pinky2.localRotation);
        _originQuaternionDic.AddOrUpdate(Pinky3.name, Pinky3.localRotation);

        _originQuaternionDic.AddOrUpdate(Wrist.name, Wrist.localRotation);
    }

    private void Rotate(Transform tran, float angle, Axis angleType)
    {
        if (!NeedRealTransfrom) return;

        float angleX = 0;
        float angleY = 0;
        float angleZ = 0;

        switch (angleType)
        {
            case Axis.x_n:
                angleX = -angle;
                break;
            case Axis.y_n:
                angleY = -angle;
                break;
            case Axis.z_n:
                angleZ = -angle;
                break;
            case Axis.x:
                angleX = angle;
                break;
            case Axis.y:
                angleY = angle;
                break;
            case Axis.z:
                angleZ = angle;
                break;
        }

        tran.Rotate(angleX, angleY, angleZ);
    }

    private Vector3 ConvertAngleToVec3(Vector3 current, float angle, Axis angleType)
    {
        float angleX = 0;
        float angleY = 0;
        float angleZ = 0;

        switch (angleType)
        {
            case Axis.x_n:
                angleX = -angle;
                break;
            case Axis.y_n:
                angleY = -angle;
                break;
            case Axis.z_n:
                angleZ = -angle;
                break;
            case Axis.x:
                angleX = angle;
                break;
            case Axis.y:
                angleY = angle;
                break;
            case Axis.z:
                angleZ = angle;
                break;
        }

        return current + new Vector3(angleX, angleY, angleZ);
    }

    private void ResetRotation(Transform trans)
    {
        if (_originQuaternionDic.TryGetValue(trans.name, out Quaternion rot))
        {
            trans.localRotation = rot;
        }
    }

    public void GetVec3Value(Vector3[] value)
    {
        thumb1 = value[0];
        thumb2 = value[1];
        thumb3 = value[2];

        index1 = value[3];
        index2 = value[4];
        index3 = value[5];

        middle1 = value[6];
        middle2 = value[7];
        middle3 = value[8];

        ring1 = value[9];
        ring2 = value[10];
        ring3 = value[11];

        pinky1 = value[12];
        pinky2 = value[13];
        pinky3 = value[14];
    }

    // Update is called once per frame
    void Update()
    {
        if (UsingNetwork || UsingAndroidService)
        {
            UpdateThumb();
            UpdateIndex();
            UpdateMiddle();
            UpdateRing();
            UpdatePinky();

            UpdateWrist();
            UpdateController();
        }
    }

    private void UpdateWrist()
    {
        if (HasIMU)
        {
            if (Hand == HandType.Left)
            {
                Quaternion quat_r = new Quaternion(
                    Network.Convert2Angle(CharacterName, "l26"),//x
                    Network.Convert2Angle(CharacterName, "l25"),//y
                    Network.Convert2Angle(CharacterName, "l27"),//z
                    Network.Convert2Angle(CharacterName, "l24"));//w
                ResetRotation(Wrist);
                quat_r = ConvertQuaternion(quat_r);
                Wrist.rotation = quat_r;
            }
            else
            {
                Quaternion quat_r = new Quaternion(Network.Convert2Angle(CharacterName, "r26"),
                    Network.Convert2Angle(CharacterName, "r25"),
                    Network.Convert2Angle(CharacterName, "r27"), Network.Convert2Angle(CharacterName, "r24"));
                ResetRotation(Wrist);
                quat_r = ConvertQuaternion(quat_r);
                Wrist.rotation = quat_r;
            }
        }
    }

    private void UpdateController()
    {
        if (Hand == HandType.Left)
        {
            Joy_X = Network.Convert2Angle(CharacterName, "l_joyX");
            Joy_Y = Network.Convert2Angle(CharacterName, "l_joyY");
            Button_A = Network.Convert2Bool(CharacterName, "l_aButton");
            Button_B = Network.Convert2Bool(CharacterName, "l_bButton");
            Button_Joystick = Network.Convert2Bool(CharacterName, "l_joyButton");
            Button_Menu = Network.Convert2Bool(CharacterName, "l_menu");
            inputData.joyX = Joy_X;
            inputData.joyY = Joy_Y;
            inputData.aButton = Button_A;
            inputData.bButton = Button_B;
            inputData.joyButton = Button_Joystick;
            inputData.menu = Button_Menu;
        }
        else
        {
            Joy_X = Network.Convert2Angle(CharacterName, "r_joyX");
            Joy_Y = Network.Convert2Angle(CharacterName, "r_joyY");
            Button_A = Network.Convert2Bool(CharacterName, "r_aButton");
            Button_B = Network.Convert2Bool(CharacterName, "r_bButton");
            Button_Joystick = Network.Convert2Bool(CharacterName, "r_joyButton");
            Button_Menu = Network.Convert2Bool(CharacterName, "r_menu");
            inputData.joyX = Joy_X;
            inputData.joyY = Joy_Y;
            inputData.aButton = Button_A;
            inputData.bButton = Button_B;
            inputData.joyButton = Button_Joystick;
            inputData.menu = Button_Menu;
        }
    }

    //z轴朝上的右手坐标系 四元数 转换为 Y轴朝上的左手坐标系 四元数
    private Quaternion ConvertQuaternion(Quaternion quat)
    {
        Quaternion quat_r = new Quaternion(quat.x, quat.y, quat.z, quat.w);
        quat_r = Quaternion.Euler(quat_r.eulerAngles.z, -quat_r.eulerAngles.x, quat_r.eulerAngles.y);
        return quat_r;
    }

    private void UpdateThumb()
    {
        if (NeedRealTransfrom)
        {
            ResetRotation(Thumb1);
            ResetRotation(Thumb2);
            ResetRotation(Thumb3);
        }
        if (Hand == HandType.Left)
        {
            var thumb3Pitch = Network.Convert2Angle(CharacterName, "l0");
            var thumb2Pitch = Network.Convert2Angle(CharacterName, "l1");
            var thumb1Pitch = Network.Convert2Angle(CharacterName, "l2") * coefficient + Thumb1Offset.y;
            var thumb1Yaw = Network.Convert2Angle(CharacterName, "l3") + Thumb1Offset.z;
            var thumb1Roll = Network.Convert2Angle(CharacterName, "l20") + Thumb1Offset.x;

            thumb3 = ConvertAngleToVec3(Vector3.zero, thumb3Pitch, Pitch);
            thumb2 = ConvertAngleToVec3(Vector3.zero, thumb2Pitch, Pitch);
            thumb1 = ConvertAngleToVec3(Vector3.zero, thumb1Pitch, Pitch);
            thumb1 = ConvertAngleToVec3(thumb1, thumb1Yaw, Yaw);
            thumb1 = ConvertAngleToVec3(thumb1, thumb1Roll, Roll);

            Rotate(Thumb3, thumb3Pitch, Pitch);
            Rotate(Thumb2, thumb2Pitch, Pitch);
            Rotate(Thumb1, thumb1Pitch, Pitch);
            Rotate(Thumb1, thumb1Yaw, Yaw);
            Rotate(Thumb1, thumb1Roll, Roll);
        }
        else
        {

            var thumb3Pitch = Network.Convert2Angle(CharacterName, "r0");
            var thumb2Pitch = Network.Convert2Angle(CharacterName, "r1");
            var thumb1Pitch = Network.Convert2Angle(CharacterName, "r2") * coefficient + Thumb1Offset.y;
            var thumb1Yaw = Network.Convert2Angle(CharacterName, "r3") + Thumb1Offset.z;
            var thumb1Roll = Network.Convert2Angle(CharacterName, "r20") + Thumb1Offset.x;

            thumb3 = ConvertAngleToVec3(Vector3.zero, thumb3Pitch, Pitch);
            thumb2 = ConvertAngleToVec3(Vector3.zero, thumb2Pitch, Pitch);
            thumb1 = ConvertAngleToVec3(Vector3.zero, thumb1Pitch, Pitch);
            thumb1 = ConvertAngleToVec3(thumb1, thumb1Yaw, Yaw);
            thumb1 = ConvertAngleToVec3(thumb1, thumb1Roll, Roll);

            Rotate(Thumb3, thumb3Pitch, Pitch);
            Rotate(Thumb2, thumb2Pitch, Pitch);
            Rotate(Thumb1, thumb1Pitch, Pitch);
            Rotate(Thumb1, thumb1Yaw, Yaw);
            Rotate(Thumb1, thumb1Roll, Roll);
        }
    }

    private void UpdateIndex()
    {
        if (NeedRealTransfrom)
        {
            ResetRotation(Index1);
            ResetRotation(Index2);
            ResetRotation(Index3);
        }
        if (Hand == HandType.Left)
        {
            var index3Pitch = Network.Convert2Angle(CharacterName, "l4");
            var index2Pitch = Network.Convert2Angle(CharacterName, "l5");
            var index1Pitch = Network.Convert2Angle(CharacterName, "l6");
            var index1Yaw = Network.Convert2Angle(CharacterName, "l7");
            var index1Roll = Network.Convert2Angle(CharacterName, "l21");

            index3 = ConvertAngleToVec3(Vector3.zero, index3Pitch, Pitch);
            index2 = ConvertAngleToVec3(Vector3.zero, index2Pitch, Pitch);
            index1 = ConvertAngleToVec3(Vector3.zero, index1Pitch, Pitch);
            index1 = ConvertAngleToVec3(index1, index1Yaw, Yaw);
            index1 = ConvertAngleToVec3(index1, index1Roll, Roll);

            Rotate(Index3, index3Pitch, Pitch);
            Rotate(Index2, index2Pitch, Pitch);
            Rotate(Index1, index1Pitch, Pitch);
            Rotate(Index1, index1Yaw, Yaw);
            Rotate(Index1, index1Roll, Roll);
        }
        else
        {
            var index3Pitch = Network.Convert2Angle(CharacterName, "r4");
            var index2Pitch = Network.Convert2Angle(CharacterName, "r5");
            var index1Pitch = Network.Convert2Angle(CharacterName, "r6");
            var index1Yaw = Network.Convert2Angle(CharacterName, "r7");
            var index1Roll = Network.Convert2Angle(CharacterName, "r21");

            index3 = ConvertAngleToVec3(Vector3.zero, index3Pitch, Pitch);
            index2 = ConvertAngleToVec3(Vector3.zero, index2Pitch, Pitch);
            index1 = ConvertAngleToVec3(Vector3.zero, index1Pitch, Pitch);
            index1 = ConvertAngleToVec3(index1, index1Yaw, Yaw);
            index1 = ConvertAngleToVec3(index1, index1Roll, Roll);

            Rotate(Index3, index3Pitch, Pitch);
            Rotate(Index2, index2Pitch, Pitch);
            Rotate(Index1, index1Pitch, Pitch);
            Rotate(Index1, index1Yaw, Yaw);
            Rotate(Index1, index1Roll, Roll);
        }
    }

    private void UpdateMiddle()
    {
        if (NeedRealTransfrom)
        {
            ResetRotation(Middle1);
            ResetRotation(Middle2);
            ResetRotation(Middle3);
        }
        if (Hand == HandType.Left)
        {
            var middle3Pitch = Network.Convert2Angle(CharacterName, "l8");
            var middle2Pitch = Network.Convert2Angle(CharacterName, "l9");
            var middle1Pitch = Network.Convert2Angle(CharacterName, "l10");
            var middle1Yaw = Network.Convert2Angle(CharacterName, "l11");

            middle3 = ConvertAngleToVec3(Vector3.zero, middle3Pitch, Pitch);
            middle2 = ConvertAngleToVec3(Vector3.zero, middle2Pitch, Pitch);
            middle1 = ConvertAngleToVec3(Vector3.zero, middle1Pitch, Pitch);
            middle1 = ConvertAngleToVec3(middle1, middle1Yaw, Yaw);

            Rotate(Middle3, middle3Pitch, Pitch);
            Rotate(Middle2, middle2Pitch, Pitch);
            Rotate(Middle1, middle1Pitch, Pitch);
            Rotate(Middle1, middle1Yaw, Yaw);
        }
        else
        {
            var middle3Pitch = Network.Convert2Angle(CharacterName, "r8");
            var middle2Pitch = Network.Convert2Angle(CharacterName, "r9");
            var middle1Pitch = Network.Convert2Angle(CharacterName, "r10");
            var middle1Yaw = Network.Convert2Angle(CharacterName, "r11");

            middle3 = ConvertAngleToVec3(Vector3.zero, middle3Pitch, Pitch);
            middle2 = ConvertAngleToVec3(Vector3.zero, middle2Pitch, Pitch);
            middle1 = ConvertAngleToVec3(Vector3.zero, middle1Pitch, Pitch);
            middle1 = ConvertAngleToVec3(middle1, middle1Yaw, Yaw);

            Rotate(Middle3, middle3Pitch, Pitch);
            Rotate(Middle2, middle2Pitch, Pitch);
            Rotate(Middle1, middle1Pitch, Pitch);
            Rotate(Middle1, middle1Yaw, Yaw);
        }
    }

    private void UpdateRing()
    {
        if (NeedRealTransfrom)
        {
            ResetRotation(Ring1);
            ResetRotation(Ring2);
            ResetRotation(Ring3);
        }
        if (Hand == HandType.Left)
        {
            var ring3Pitch = Network.Convert2Angle(CharacterName, "l12");
            var ring2Pitch = Network.Convert2Angle(CharacterName, "l13");
            var ring1Pitch = Network.Convert2Angle(CharacterName, "l14");
            var ring1Yaw = Network.Convert2Angle(CharacterName, "l15");

            ring3 = ConvertAngleToVec3(Vector3.zero, ring3Pitch, Pitch);
            ring2 = ConvertAngleToVec3(Vector3.zero, ring2Pitch, Pitch);
            ring1 = ConvertAngleToVec3(Vector3.zero, ring1Pitch, Pitch);
            ring1 = ConvertAngleToVec3(ring1, ring1Yaw, Yaw);

            Rotate(Ring3, ring3Pitch, Pitch);
            Rotate(Ring2, ring2Pitch, Pitch);
            Rotate(Ring1, ring1Pitch, Pitch);
            Rotate(Ring1, ring1Yaw, Yaw);
        }
        else
        {
            var ring3Pitch = Network.Convert2Angle(CharacterName, "r12");
            var ring2Pitch = Network.Convert2Angle(CharacterName, "r13");
            var ring1Pitch = Network.Convert2Angle(CharacterName, "r14");
            var ring1Yaw = Network.Convert2Angle(CharacterName, "r15");

            ring3 = ConvertAngleToVec3(Vector3.zero, ring3Pitch, Pitch);
            ring2 = ConvertAngleToVec3(Vector3.zero, ring2Pitch, Pitch);
            ring1 = ConvertAngleToVec3(Vector3.zero, ring1Pitch, Pitch);
            ring1 = ConvertAngleToVec3(ring1, ring1Yaw, Yaw);

            Rotate(Ring3, ring3Pitch, Pitch);
            Rotate(Ring2, ring2Pitch, Pitch);
            Rotate(Ring1, ring1Pitch, Pitch);
            Rotate(Ring1, ring1Yaw, Yaw);
        }
    }

    private void UpdatePinky()
    {
        if (NeedRealTransfrom)
        {
            ResetRotation(Pinky1);
            ResetRotation(Pinky2);
            ResetRotation(Pinky3);
        }
        if (Hand == HandType.Left)
        {
            var pinky3Pitch = Network.Convert2Angle(CharacterName, "l16");
            var pinky2Pitch = Network.Convert2Angle(CharacterName, "l17");
            var pinky1Pitch = Network.Convert2Angle(CharacterName, "l18");
            var pinky1Yaw = Network.Convert2Angle(CharacterName, "l19");
            var pinky1Roll = Network.Convert2Angle(CharacterName, "l22");

            pinky3 = ConvertAngleToVec3(Vector3.zero, pinky3Pitch, Pitch);
            pinky2 = ConvertAngleToVec3(Vector3.zero, pinky2Pitch, Pitch);
            pinky1 = ConvertAngleToVec3(Vector3.zero, pinky1Pitch, Pitch);
            pinky1 = ConvertAngleToVec3(pinky1, pinky1Yaw, Yaw);
            pinky1 = ConvertAngleToVec3(pinky1, pinky1Roll, Roll);

            Rotate(Pinky3, pinky3Pitch, Pitch);
            Rotate(Pinky2, pinky2Pitch, Pitch);
            Rotate(Pinky1, pinky1Pitch, Pitch);
            Rotate(Pinky1, pinky1Yaw, Yaw);
            Rotate(Pinky1, pinky1Roll, Roll);
        }
        else
        {
            var pinky3Pitch = Network.Convert2Angle(CharacterName, "r16");
            var pinky2Pitch = Network.Convert2Angle(CharacterName, "r17");
            var pinky1Pitch = Network.Convert2Angle(CharacterName, "r18");
            var pinky1Yaw = Network.Convert2Angle(CharacterName, "r19");
            var pinky1Roll = Network.Convert2Angle(CharacterName, "r22");

            pinky3 = ConvertAngleToVec3(Vector3.zero, pinky3Pitch, Pitch);
            pinky2 = ConvertAngleToVec3(Vector3.zero, pinky2Pitch, Pitch);
            pinky1 = ConvertAngleToVec3(Vector3.zero, pinky1Pitch, Pitch);
            pinky1 = ConvertAngleToVec3(pinky1, pinky1Yaw, Yaw);
            pinky1 = ConvertAngleToVec3(pinky1, pinky1Roll, Roll);

            Rotate(Pinky3, pinky3Pitch, Pitch);
            Rotate(Pinky2, pinky2Pitch, Pitch);
            Rotate(Pinky1, pinky1Pitch, Pitch);
            Rotate(Pinky1, pinky1Yaw, Yaw);
            Rotate(Pinky1, pinky1Roll, Roll);
        }
    }

    public void ServiceVibrationControl(int VirbatorIndex = 1, int DurationSecond = 20, int Strength = 10)
    {
        if (Network == null || !UsingAndroidService) return;

        SingleVirbator[] Virbators;
        if (Hand == HandType.Left)
        {
            Virbators = new SingleVirbator[2]
            {
                new SingleVirbator(VirbatorIndex, DurationSecond, Strength),
                new SingleVirbator()
            };
            vibrationData = new VibrationData(Virbators);
        }
        else
        {
            Virbators = new SingleVirbator[2]
            {
                new SingleVirbator(),
                new SingleVirbator(VirbatorIndex, DurationSecond, Strength),
            };
            vibrationData = new VibrationData(Virbators);
        }
        Network.SendVibrationMsg("AndroidService", "127.0.0.1", vibrationData);
    }
}
