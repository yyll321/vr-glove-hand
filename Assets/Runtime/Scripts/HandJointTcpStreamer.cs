using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class HandJointTcpStreamer : MonoBehaviour
{
    public enum CoordinateMode
    {
        UnityWorld,
        RootLocal
    }

    [Header("TCP Server")]
    public string ListenAddress = "127.0.0.1";
    public int ListenPort = 5566;
    [Min(1)] public int SendRateHz = 30;
    public bool StartOnEnable = true;
    public bool LogConnections = true;

    [Header("Hands")]
    public bool AutoFindHands = true;
    public HandDriver[] Hands;
    public CoordinateMode Coordinates = CoordinateMode.UnityWorld;

    [Header("Debug")]
    public int ConnectedClientCount;
    public int SentFrameCount;
    public int LastPayloadBytes;
    public string LastError;

    private readonly List<TcpClient> _clients = new List<TcpClient>();
    private readonly List<HandBinding> _handBindings = new List<HandBinding>();
    private readonly StringBuilder _json = new StringBuilder(8192);
    private TcpListener _listener;
    private float _nextSendTime;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private void Reset()
    {
        AutoFindHands = true;
        SendRateHz = 30;
        ListenAddress = "127.0.0.1";
        ListenPort = 5566;
    }

    private void OnEnable()
    {
        RefreshHands();

        if (StartOnEnable)
        {
            StartServer();
        }
    }

    private void OnDisable()
    {
        StopServer();
    }

    private void Update()
    {
        AcceptPendingClients();

        if (_clients.Count == 0 || Time.unscaledTime < _nextSendTime)
        {
            ConnectedClientCount = _clients.Count;
            return;
        }

        _nextSendTime = Time.unscaledTime + 1f / Mathf.Max(1, SendRateHz);
        string payload = BuildPayload();
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        LastPayloadBytes = bytes.Length;

        for (int i = _clients.Count - 1; i >= 0; i--)
        {
            TcpClient client = _clients[i];
            try
            {
                if (!client.Connected)
                {
                    RemoveClientAt(i);
                    continue;
                }

                NetworkStream stream = client.GetStream();
                stream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                RemoveClientAt(i);
            }
        }

        ConnectedClientCount = _clients.Count;
        SentFrameCount++;
    }

    [ContextMenu("Refresh Hands")]
    public void RefreshHands()
    {
        _handBindings.Clear();

        HandDriver[] sourceHands = Hands;
        if (AutoFindHands || sourceHands == null || sourceHands.Length == 0)
        {
            sourceHands = FindObjectsOfType<HandDriver>();
        }

        if (sourceHands == null)
        {
            return;
        }

        for (int i = 0; i < sourceHands.Length; i++)
        {
            HandDriver hand = sourceHands[i];
            if (hand == null)
            {
                continue;
            }

            _handBindings.Add(new HandBinding(hand));
        }
    }

    [ContextMenu("Start TCP Server")]
    public void StartServer()
    {
        if (_listener != null)
        {
            return;
        }

        try
        {
            IPAddress address = string.IsNullOrWhiteSpace(ListenAddress)
                ? IPAddress.Any
                : IPAddress.Parse(ListenAddress);

            _listener = new TcpListener(address, ListenPort);
            _listener.Start();
            LastError = string.Empty;

            if (LogConnections)
            {
                Debug.Log($"HandJointTcpStreamer listening on {address}:{ListenPort}");
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Debug.LogError($"HandJointTcpStreamer failed to start: {ex.Message}");
            _listener = null;
        }
    }

    [ContextMenu("Stop TCP Server")]
    public void StopServer()
    {
        for (int i = _clients.Count - 1; i >= 0; i--)
        {
            RemoveClientAt(i);
        }

        if (_listener != null)
        {
            _listener.Stop();
            _listener = null;
        }

        ConnectedClientCount = 0;
    }

    private void AcceptPendingClients()
    {
        if (_listener == null)
        {
            return;
        }

        try
        {
            while (_listener.Pending())
            {
                TcpClient client = _listener.AcceptTcpClient();
                client.NoDelay = true;
                client.SendTimeout = 50;
                _clients.Add(client);

                if (LogConnections)
                {
                    Debug.Log($"HandJointTcpStreamer client connected: {client.Client.RemoteEndPoint}");
                }
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    private string BuildPayload()
    {
        if (_handBindings.Count == 0)
        {
            RefreshHands();
        }

        _json.Length = 0;
        _json.Append("{\"type\":\"hand_joints\",\"version\":1,\"timestamp\":");
        AppendFloat(_json, Time.realtimeSinceStartup);
        _json.Append(",\"coordinate\":\"");
        _json.Append(Coordinates == CoordinateMode.UnityWorld ? "unity_world" : "root_local");
        _json.Append("\",\"hands\":[");

        bool wroteHand = false;
        for (int i = 0; i < _handBindings.Count; i++)
        {
            HandBinding hand = _handBindings[i];
            if (hand.Driver == null)
            {
                continue;
            }

            if (wroteHand)
            {
                _json.Append(',');
            }

            wroteHand = true;
            AppendHand(_json, hand);
        }

        _json.Append("]}\n");
        return _json.ToString();
    }

    private void AppendHand(StringBuilder builder, HandBinding hand)
    {
        string handName = hand.Driver.Hand == HandDriver.HandType.Left ? "left" : "right";
        string source = string.IsNullOrWhiteSpace(hand.Driver.CharacterName)
            ? hand.Driver.gameObject.name
            : hand.Driver.CharacterName;

        builder.Append("{\"hand\":\"");
        builder.Append(handName);
        builder.Append("\",\"source\":\"");
        AppendEscaped(builder, source);
        builder.Append("\",\"joints\":[");

        for (int i = 0; i < hand.Joints.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            AppendJoint(builder, hand, hand.Joints[i]);
        }

        builder.Append("]}");
    }

    private void AppendJoint(StringBuilder builder, HandBinding hand, JointBinding joint)
    {
        Transform transform = joint.Transform != null ? joint.Transform : joint.Fallback;
        bool tracked = joint.Transform != null;

        builder.Append("{\"name\":\"");
        builder.Append(joint.Name);
        builder.Append("\",\"tracked\":");
        builder.Append(tracked ? "true" : "false");

        if (transform == null)
        {
            builder.Append(",\"position\":[0,0,0],\"rotation\":[0,0,0,1]}");
            return;
        }

        Vector3 position = Coordinates == CoordinateMode.RootLocal && hand.Root != null
            ? hand.Root.InverseTransformPoint(transform.position)
            : transform.position;
        Quaternion rotation = Coordinates == CoordinateMode.RootLocal && hand.Root != null
            ? Quaternion.Inverse(hand.Root.rotation) * transform.rotation
            : transform.rotation;

        builder.Append(",\"position\":");
        AppendVector3(builder, position);
        builder.Append(",\"rotation\":");
        AppendQuaternion(builder, rotation);
        builder.Append(",\"localPosition\":");
        AppendVector3(builder, transform.localPosition);
        builder.Append(",\"localRotation\":");
        AppendQuaternion(builder, transform.localRotation);
        builder.Append('}');
    }

    private void RemoveClientAt(int index)
    {
        TcpClient client = _clients[index];
        _clients.RemoveAt(index);

        try
        {
            client.Close();
        }
        catch
        {
            // Closing a broken socket can throw on some platforms.
        }
    }

    private static void AppendVector3(StringBuilder builder, Vector3 value)
    {
        builder.Append('[');
        AppendFloat(builder, value.x);
        builder.Append(',');
        AppendFloat(builder, value.y);
        builder.Append(',');
        AppendFloat(builder, value.z);
        builder.Append(']');
    }

    private static void AppendQuaternion(StringBuilder builder, Quaternion value)
    {
        builder.Append('[');
        AppendFloat(builder, value.x);
        builder.Append(',');
        AppendFloat(builder, value.y);
        builder.Append(',');
        AppendFloat(builder, value.z);
        builder.Append(',');
        AppendFloat(builder, value.w);
        builder.Append(']');
    }

    private static void AppendFloat(StringBuilder builder, float value)
    {
        builder.Append(value.ToString("G9", Invariant));
    }

    private static void AppendEscaped(StringBuilder builder, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }
    }

    private sealed class HandBinding
    {
        public readonly HandDriver Driver;
        public readonly Transform Root;
        public readonly JointBinding[] Joints;

        public HandBinding(HandDriver driver)
        {
            Driver = driver;
            Root = driver.Wrist != null ? driver.Wrist.root : driver.transform.root;

            bool isLeft = driver.Hand == HandDriver.HandType.Left;
            string suffix = isLeft ? "l" : "r";

            Transform thumbTip = FindDeepChild(Root, "finger_thumb_" + suffix + "_end");
            Transform indexTip = FindDeepChild(Root, "finger_index_" + suffix + "_end");
            Transform middleTip = FindDeepChild(Root, "finger_middle_" + suffix + "_end");
            Transform ringTip = FindDeepChild(Root, "finger_ring_" + suffix + "_end");
            Transform pinkyTip = FindDeepChild(Root, "finger_pinky_" + suffix + "_end");

            Joints = new[]
            {
                new JointBinding("wrist", driver.Wrist),

                new JointBinding("thumb_cmc", driver.Thumb1),
                new JointBinding("thumb_mcp", driver.Thumb2),
                new JointBinding("thumb_ip", driver.Thumb3),
                new JointBinding("thumb_tip", thumbTip, driver.Thumb3),

                new JointBinding("index_mcp", driver.Index1),
                new JointBinding("index_pip", driver.Index2),
                new JointBinding("index_dip", driver.Index3),
                new JointBinding("index_tip", indexTip, driver.Index3),

                new JointBinding("middle_mcp", driver.Middle1),
                new JointBinding("middle_pip", driver.Middle2),
                new JointBinding("middle_dip", driver.Middle3),
                new JointBinding("middle_tip", middleTip, driver.Middle3),

                new JointBinding("ring_mcp", driver.Ring1),
                new JointBinding("ring_pip", driver.Ring2),
                new JointBinding("ring_dip", driver.Ring3),
                new JointBinding("ring_tip", ringTip, driver.Ring3),

                new JointBinding("little_mcp", driver.Pinky1),
                new JointBinding("little_pip", driver.Pinky2),
                new JointBinding("little_dip", driver.Pinky3),
                new JointBinding("little_tip", pinkyTip, driver.Pinky3),
            };
        }
    }

    private readonly struct JointBinding
    {
        public readonly string Name;
        public readonly Transform Transform;
        public readonly Transform Fallback;

        public JointBinding(string name, Transform transform, Transform fallback = null)
        {
            Name = name;
            Transform = transform;
            Fallback = fallback;
        }
    }

    private static Transform FindDeepChild(Transform root, string name)
    {
        if (root == null)
        {
            return null;
        }

        if (root.name == name)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindDeepChild(root.GetChild(i), name);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}
