/*实验室场景机械臂控制，初始状态YZZY为-143，0，0，0
 * 将目标定为一个transform，当该transform移动时，机械臂随之移动
 * 当按下C时isHold = true，机械臂跟随指定的Transform移动
 * 当按下V时isHold = false，机械臂回到原始的垂直状态
 */
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Pool_JXBControl : MonoBehaviour
{
    public enum ArmModel
    {
        Nothing,        // 不进行运动
        Joint_MODE,     // 关节驱动
        Pos_MODE        // 末端位置驱动（逆运动学解）
    }

    [SerializeField]
    private Transform[] Joints;       // 机械臂旋转关节组（四关节）

    [Tooltip("设置驱动机械臂的模式")]
    public ArmModel Model = ArmModel.Nothing;

    [SerializeField]
    [Tooltip("测试关节角度")]
    public float[] TestJointangles;          // 测试关节角度（四个关节）

    [SerializeField]
    [Tooltip("末端位置 Transform 对象")]
    public Transform EndEffectorTarget;      // 用于设置末端位置的 Transform 对象
    [SerializeField]
    private KeyCode PickKey1 = KeyCode.C;
    private KeyCode PickKey2 = KeyCode.V;
    private bool isHold;

    //机械臂运行速度控制
    public float smoothSpeed = 0.5f; // 越大越快
    private float[] currentAngles = new float[4]; // 当前角度记录

    void Start()
    {
        isHold = false;

    }

    void Update()
    {
        if (Input.GetKeyDown(PickKey1))
        {
            isHold = true;
            Debug.Log("按下C");
        }
        if (Input.GetKeyDown(PickKey2))
        {
            isHold = false;
            Debug.Log("按下V");
        }
        switch (Model)
        {
            case ArmModel.Nothing:
                // 不进行任何运动
                break;

            case ArmModel.Joint_MODE:
                SetAngle(TestJointangles[0], TestJointangles[1], TestJointangles[2], TestJointangles[3]);
                // 计算正运动学
                Vector3 TestPos1 = ForwardKinematicsElectric(TestJointangles[0], TestJointangles[1], TestJointangles[2]);
                //Debug.Log("正运动学计算的末端位置: " + TestPos1);
                break;

            case ArmModel.Pos_MODE:
                if (EndEffectorTarget != null && isHold)
                {
                    // 从 Transform 获取末端目标位置
                    Vector3 targetPosition = EndEffectorTarget.position;

                    // 转换目标位置到机械臂的局部坐标系
                    Vector3 localTargetPosition = transform.InverseTransformPoint(targetPosition);

                    float[] Joints = InverseKinematicsElectric(localTargetPosition); // 计算逆运动学

                    TestJointangles[0] = Joints[0];
                    TestJointangles[1] = Joints[1];
                    TestJointangles[2] = Joints[2];
                    TestJointangles[3] = 0; // 第四关节角度保持为零（不控制）

                    SetAngle(TestJointangles[0], TestJointangles[1], TestJointangles[2], TestJointangles[3]);
                }
                else
                {

                    // 设置关节角度
                    TestJointangles[0] = 0;    
                    TestJointangles[1] = -90; 
                    TestJointangles[2] = 180; 
                    TestJointangles[3] = 0;   

                    // 使用 SetAngle 方法设置关节位置
                    SetAngle(TestJointangles[0], TestJointangles[1], TestJointangles[2], TestJointangles[3]);

                    // 通过正运动学计算末端位置
                    Vector3 endEffectorPosition = ForwardKinematicsElectric(TestJointangles[0], TestJointangles[1], TestJointangles[2]);
                }
                break;
        }
    }

    //public void SetAngle(float q0, float q1, float q2, float q3)
    //{
    //    // 四关节分别绕 Y-Z-Z-Y 轴旋转
    //    Joints[0].localEulerAngles = new Vector3(0, q0, 0); // 第一关节绕 Y 轴
    //    Joints[1].localEulerAngles = new Vector3(0, 0, q1); // 第二关节绕 Z 轴
    //    Joints[2].localEulerAngles = new Vector3(0, 0, q2); // 第三关节绕 Z 轴
    //    Joints[3].localEulerAngles = new Vector3(0, q3, 0); // 第四关节绕 Y 轴（不控制角度）
    //}

    public void SetAngle(float q0, float q1, float q2, float q3)
    {
        currentAngles[0] = Mathf.LerpAngle(currentAngles[0], q0, Time.deltaTime * smoothSpeed);
        currentAngles[1] = Mathf.LerpAngle(currentAngles[1], q1, Time.deltaTime * smoothSpeed);
        currentAngles[2] = Mathf.LerpAngle(currentAngles[2], q2, Time.deltaTime * smoothSpeed);
        currentAngles[3] = Mathf.LerpAngle(currentAngles[3], q3, Time.deltaTime * smoothSpeed);

        // 四关节分别绕 Y-Z-Z-Y 轴旋转
        Joints[0].localEulerAngles = new Vector3(0, currentAngles[0], 0);
        Joints[1].localEulerAngles = new Vector3(0, 0, currentAngles[1]);
        Joints[2].localEulerAngles = new Vector3(0, 0, currentAngles[2]);
        Joints[3].localEulerAngles = new Vector3(0, currentAngles[3], 0); // 第四关节角度仍然为0或不变
    }



    private float[] InverseKinematicsElectric(Vector3 RelativePosition)
    {
        float q0, q1, q2;
        q0 = 0f;
        q1 = 0f;
        q2 = 0f;
        float x = RelativePosition.x;
        float y = RelativePosition.z;
        float z = RelativePosition.y;
        float u = Mathf.Sqrt(y * y + x * x);
        float v = z;

        float L0 = 0.135f;
        float L1 = 0.407f;
        float L2 = 0.321f;
        float Lk = Mathf.Sqrt(u * u + (v - L0) * (v - L0));
        if (Lk < L2 - L1)
        {
            Lk = L2 - L1;
        }

        q0 = Mathf.Deg2Rad * (180f) - Mathf.Atan2(y, x);
        if (Lk > L1 + L2)
        {
            q2 = 0;
            q1 = Mathf.Atan2(u, v - L0);
            return new float[] { q0 * Mathf.Rad2Deg, q1 * Mathf.Rad2Deg, q2 * Mathf.Rad2Deg };
        }

        q2 = Mathf.Deg2Rad * (180f) - Mathf.Acos((L1 * L1 + L2 * L2 - Lk * Lk) / (2 * L1 * L2));

        float a = u / L2;
        float b = L1 / L2 + Mathf.Cos(q2);
        float c = Mathf.Sin(q2);

        q1 = Mathf.Deg2Rad * (90f) - Mathf.Acos((L1 * L1 + Lk * Lk - L2 * L2) / (2 * L1 * Lk)) - Mathf.Atan2(v - L0, u);

        return new float[] { q0 * Mathf.Rad2Deg, q1 * Mathf.Rad2Deg, q2 * Mathf.Rad2Deg };
    }

    private Vector3 ForwardKinematicsElectric(float q0, float q1, float q2)
    {
        float L0 = 0.135f;
        float L1 = 0.407f;
        float L2 = 0.321f;
        float[] q = { q0 * Mathf.Deg2Rad, q1 * Mathf.Deg2Rad, q2 * Mathf.Deg2Rad };
        Vector3 O1 = new Vector3(0, 0, 0);
        Vector3 O2 = O1 + new Vector3(0, L0, 0);
        Vector3 O3 = O2 + new Vector3(L1 * Mathf.Sin(q[1]) * Mathf.Sin(q[0]), L1 * Mathf.Cos(q[1]), L1 * Mathf.Sin(q[1]));
        Vector3 O4 = O3 + new Vector3(L2 * Mathf.Sin(q[1] + q[2]) * Mathf.Sin(q[0]), L2 * Mathf.Cos(q[1] + q[2]), L2 * Mathf.Sin(q[1] + q[2]));
        return O4;
    }
}

