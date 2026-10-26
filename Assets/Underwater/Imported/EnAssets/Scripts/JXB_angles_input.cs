/* ��Get_JXB_Jointangles����ĽǶȸ�ֵ�������е�ۣ�ʹ�仹ԭ��ʵ����
 * ע����Ҫ����ROV�ܵ��Լ���е��֮��λ�ù�ϵ
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class JXB_angles_input : MonoBehaviour
{
    // Start is called before the first frame update
    public MonoBehaviour ROS_data;
    float ReadRos(string name) { var field = ROS_data.GetType().GetField(name); return field == null ? 0 : System.Convert.ToSingle(field.GetValue(ROS_data)); }

    public enum ArmModel
    {
        Nothing,        // �������˶�
        Joint_MODE,     // �ؽ�����
        Pos_MODE        // ĩ��λ�����������˶�ѧ�⣩
    }
    [Tooltip("����������е�۵�ģʽ")]
    public ArmModel Model = ArmModel.Nothing;

    [SerializeField]
    private Transform[] Joints;       // ��е����ת�ؽ��飨�Ĺؽڣ�

    [SerializeField]
    [Tooltip("�ؽڽǶ�")]
    public float[] Jointangles;          // �ؽڽǶȣ��ĸ��ؽڣ�

    [SerializeField]
    [Tooltip("ĩ��λ�� Transform ����")]
    public Transform EndEffectorTarget;      // ��������ĩ��λ�õ� Transform ����

    private KeyCode PickKey1 = KeyCode.C;
    private KeyCode PickKey2 = KeyCode.V;
    private bool isHold;

    public float x;
    public float y;
    public float z;
    //public float w;

    void Start()
    {
        isHold = false;
        if (Jointangles == null || Jointangles.Length < 4)
        {
            Jointangles = new float[4];
        }
    }

    // Update is called once per frame
    void Update()
    {
        //���������ݸ�ֵx,y,z,w
        if (ROS_data != null)
        {
            x = - ReadRos("arm1")  * Mathf.Rad2Deg - 90;
            y = ReadRos("arm2") * Mathf.Rad2Deg - 90;
            z = - ReadRos("arm3") * Mathf.Rad2Deg;
            //����ʵ��е�ۣ����ƥ��

            Debug.Log("�Ѹ�ֵ  X: " + x + ", Y: " + y + ", Z: " + z);
        }

        if (Input.GetKeyDown(PickKey1))
        {
            isHold = true;
            Debug.Log("����C");
        }
        if (Input.GetKeyDown(PickKey2))
        {
            isHold = false;
            Debug.Log("����V");
        }
        switch (Model)
        {
            case ArmModel.Nothing:
                // �������κ��˶�
                break;

            case ArmModel.Joint_MODE:
                if (x != 0 && y != 0 && z != 0)
                {
                    Debug.Log($"Current Mode: {Model}, x: {x}, y: {y}, z: {z}");

                    Jointangles[0] = x;
                    Jointangles[1] = y;
                    Jointangles[2] = z;
                    Jointangles[3] = -57.528f; // ���ĹؽڽǶȱ���Ϊ�㣨�����ƣ�
                    SetAngle(Jointangles[0], Jointangles[1], Jointangles[2], Jointangles[3]);
                    //Vector3 endEffectorPosition = ForwardKinematicsElectric(Jointangles[0], Jointangles[1], Jointangles[2]);
                }
                break;

            case ArmModel.Pos_MODE:
                if (EndEffectorTarget != null && isHold)
                {
                    // �� Transform ��ȡĩ��Ŀ��λ��
                    Vector3 targetPosition = EndEffectorTarget.position;

                    // ת��Ŀ��λ�õ���е�۵ľֲ�����ϵ
                    Vector3 localTargetPosition = transform.InverseTransformPoint(targetPosition);

                    float[] Joints = InverseKinematicsElectric(localTargetPosition); // �������˶�ѧ

                    Jointangles[0] = Joints[0];
                    Jointangles[1] = Joints[1];
                    Jointangles[2] = Joints[2];
                    Jointangles[3] = 0; // ���ĹؽڽǶȱ���Ϊ�㣨�����ƣ�

                    SetAngle(Jointangles[0], Jointangles[1], Jointangles[2], Jointangles[3]);
                }
                else
                {

                    Jointangles[0] = 0;
                    Jointangles[1] = -90;
                    Jointangles[2] = 180;
                    Jointangles[3] = 0;

                    // ʹ�� SetAngle �������ùؽ�λ��
                    SetAngle(Jointangles[0], Jointangles[1], Jointangles[2], Jointangles[3]);

                    // ͨ�����˶�ѧ����ĩ��λ��
                    Vector3 endEffectorPosition = ForwardKinematicsElectric(Jointangles[0], Jointangles[1], Jointangles[2]);

                }
                break;
        }
    }

    public void SetAngle(float q0, float q1, float q2, float q3)
    {
        // �Ĺؽڷֱ��� Y-Z-Z-Y ����ת
        Joints[0].localEulerAngles = new Vector3(0, q0, 0); // ��һ�ؽ��� Y ��
        Joints[1].localEulerAngles = new Vector3(0, 0, q1); // �ڶ��ؽ��� Z ��
        Joints[2].localEulerAngles = new Vector3(0, 0, q2); // �����ؽ��� Z ��
        Joints[3].localEulerAngles = new Vector3(0, q3, 0); // ���Ĺؽ��� Y �ᣨ�����ƽǶȣ�
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
        float L1 = 0.424f;
        float L2 = 0.268f;
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
