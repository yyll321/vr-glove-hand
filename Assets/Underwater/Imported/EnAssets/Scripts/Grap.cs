using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Grap : MonoBehaviour
{
    
    [SerializeField]
    private KeyCode PickKey2 = KeyCode.G;  // 抓取的按键
    [SerializeField]
    private GameObject LeftGrip;          // 左夹爪
    [SerializeField]
    private GameObject RightGrip;         // 右夹爪
    [SerializeField]
    private bool isHold;                  // 是否正在抓取物体
    private bool sthIn;                   // 是否有物体在触发范围内
    private float smoothTime = 0.02f;     // 平滑移动时间
    private Vector3 velocity = Vector3.zero; // 平滑移动的速度
    public Transform CenterPoint;        // 爪子中心的 Transform
    public Transform TargetObject;       // 目标物体中心
    public Transform car;

    private Vector3 initialLocalRotation; // 记录物体的初始局部旋转

    void Start()
    {
        isHold = false;
        sthIn = false;
    }

    void Update()
    {
        // 按下抓取键抓取或释放物体
        if (Input.GetKeyDown(PickKey2))
        {
            if (sthIn && !isHold)
            {
                isHold = true; // 抓取物体
                initialLocalRotation = car.eulerAngles; // 记录局部旋转
                Debug.Log("正在抓取 ，记录初始局部旋转: " + initialLocalRotation);
            }
            else if (isHold)
            {
                isHold = false; // 释放物体
                Debug.Log("正在释放");
            }
        }

        // 控制夹爪开合 (例如按 W 和 S)
        if (Input.GetKey(KeyCode.W)) // 夹爪关闭
        {
            SetGrip(0.5f);
        }
        if (Input.GetKey(KeyCode.S)) // 夹爪打开
        {
            SetGrip(-0.5f);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("CanPickup"))
        {
            sthIn = true; // 有物体进入范围
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("CanPickup"))
        {
            sthIn = false; // 物体离开范围
            isHold = false; // 物体释放
            
            other.transform.SetParent(null); // 解除父子关系
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("CanPickup") && isHold)
        {
            // 平滑移动物体到爪子中心
            if (Vector3.Distance(TargetObject.position, CenterPoint.position) < 0.05f)
            {
                TargetObject.position = CenterPoint.position;
            }
            else
            {
                Vector3 newPosition = Vector3.SmoothDamp(TargetObject.position, CenterPoint.position, ref velocity, smoothTime);
                TargetObject.position = newPosition;
                
            }
            // 将物体设置为爪子的子对象
            other.transform.SetParent(CenterPoint);

            //if (Input.GetKeyDown(PickKey1))
            //{
            //    // 确保物体保持初始局部旋转
            //    other.transform.eulerAngles = initialLocalRotation;
            //}
        }

        if (!isHold)
        {
            other.transform.SetParent(null);
        }
    }

    // 控制夹爪的开合
    public void SetGrip(float gripForce)
    {
        if (gripForce > 0) // 夹爪关闭
        {
            LeftGrip.GetComponent<Rigidbody>().AddForce(LeftGrip.transform.forward.normalized * gripForce);
            RightGrip.GetComponent<Rigidbody>().AddForce(RightGrip.transform.forward.normalized * gripForce);
        }
        else if (gripForce < 0) // 夹爪打开
        {
            float xLeft = -0.05215725f;
            float xRight = 0.0489346f;

            // 限制夹爪打开的范围
            if (LeftGrip.transform.localPosition.x < xLeft)
            {
                LeftGrip.transform.localPosition = new Vector3(xLeft, LeftGrip.transform.localPosition.y, LeftGrip.transform.localPosition.z);
                LeftGrip.GetComponent<Rigidbody>().velocity = Vector3.zero;
                LeftGrip.GetComponent<Rigidbody>().angularVelocity = Vector3.zero;

                RightGrip.transform.localPosition = new Vector3(xRight, RightGrip.transform.localPosition.y, RightGrip.transform.localPosition.z);
                RightGrip.GetComponent<Rigidbody>().velocity = Vector3.zero;
                RightGrip.GetComponent<Rigidbody>().angularVelocity = Vector3.zero;
            }
            else
            {
                LeftGrip.GetComponent<Rigidbody>().AddForce(LeftGrip.transform.forward.normalized * gripForce);
                RightGrip.GetComponent<Rigidbody>().AddForce(RightGrip.transform.forward.normalized * gripForce);
            }
        }
    }
}
