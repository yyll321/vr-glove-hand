using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using TouchSocket.Core;
using TouchSocket.Sockets;
using UnityEngine;

public class Network : MonoBehaviour
{

    public int Port = 5555;
    public bool LogPackets;
    public int ReceivedPacketCount;
    public string LastEndpoint;
    public string LastRoleName;
    public string LastPacketUtc;

    private UdpSession _udpClient;

    private ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _deviceReadMessages;

    private StreamWriter writer;
    private string _txtPath;
    void Start()
    {
        _txtPath = Application.dataPath + "stream.data";
        _deviceReadMessages = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>();
        _udpClient = new UdpSession();

        _udpClient.Received += ReceiveMsg;

        _udpClient.Setup(new TouchSocketConfig()
                .SetBindIPHost(new IPHost(Port))
                .SetUdpDataHandlingAdapter(() => new NormalUdpDataHandlingAdapter()))
            .Start();

        Debug.Log("UDP Client Start!!");

    }


    private void WriteIntoTxt(string message)
    {
        FileInfo file = new FileInfo(_txtPath);
        if (!file.Exists)
        {
            writer = file.CreateText();
        }
        else
        {
            writer = file.AppendText();
        }

        writer.WriteLine(message);
        writer.Flush();
        writer.Dispose();
        writer.Close();
    }

    private void ReceiveMsg(EndPoint endpoint, ByteBlock byteblock, IRequestInfo requestinfo)
    {
        string msg = Encoding.UTF8.GetString(byteblock.Buffer, 0, byteblock.Len);
        ReceivedPacketCount++;
        LastEndpoint = endpoint?.ToString();
        LastPacketUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        if (LogPackets)
        {
            Debug.Log(msg);
        }
        //WriteIntoTxt(msg);
        try
        {
            Dictionary<string, object> obj = JsonFastConverter.JsonFrom(msg);
            foreach (var jp in obj)
            {
                string role_name = jp.Key;
                LastRoleName = role_name;

                if (jp.Value is not Dictionary<string, object> token ||
                    !token.TryGetValue("Parameter", out object parameter) ||
                    parameter is not List<object> array)
                {
                    continue;
                }

                ConcurrentDictionary<string, string> _deviceMsg = new ConcurrentDictionary<string, string>();
                for (int i = 0; i < array.Count; i++)
                {
                    if (array[i] is not Dictionary<string, object> obj1)
                    {
                        continue;
                    }

                    obj1.TryGetValue("Name", out object nameObj);
                    obj1.TryGetValue("Value", out object valueObj);
                    string key = Convert.ToString(nameObj, CultureInfo.InvariantCulture);
                    string value = Convert.ToString(valueObj, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(key))
                    {
                        _deviceMsg[key] = value;
                    }
                }

                _deviceReadMessages[role_name] = _deviceMsg;
            }
        }
        catch (Exception ex)
        {
            if (LogPackets)
            {
                Debug.LogWarning($"HandDriver packet ignored: {ex.Message}");
            }
        }
        //JToken token = obj.GetValue("Device1_"+Port);
        //JArray array = token["Parameter"] as JArray;
        //for (int i = 0; i < array.Count; i++)
        //{
        //    JObject obj1 = array[i] as JObject;
        //    string key = obj1.GetValue("Name").ToString();
        //    string value = obj1.GetValue("Value").ToString();
        //    _device1ReadMessages.AddOrUpdate(key, value);
        //}

        //JToken token2 = obj.GetValue("Device2_"+Port);
        //JArray array2 = token2["Parameter"] as JArray;
        //for (int i = 0; i < array.Count; i++)
        //{
        //    JObject obj1 = array2[i] as JObject;
        //    string key = obj1.GetValue("Name").ToString();
        //    string value = obj1.GetValue("Value").ToString();
        //    _device2ReadMessages.AddOrUpdate(key, value);
        //}
    }

    // Update is called once per frame
    void Update()
    {

    }

    public float Convert2Angle(string role_name, string key)
    {
        if (!TryGetValue(role_name, key, out string str))
        {
            return 0;
        }

        return float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out float angle)
            ? angle
            : 0;
    }

    private bool TryGetValue(string role_name, string key, out string value)
    {
        value = null;
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(role_name) &&
            _deviceReadMessages.TryGetValue(role_name, out var namedDeviceMessages) &&
            namedDeviceMessages.TryGetValue(key, out value) &&
            !string.IsNullOrEmpty(value))
        {
            return true;
        }

        foreach (var deviceMessages in _deviceReadMessages.Values)
        {
            if (deviceMessages.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
            {
                return true;
            }
        }

        return false;
    }

    public bool Convert2Bool(string role_name, string key)
    {
        if (!TryGetValue(role_name, key, out string str))
        {
            return false;
        }

        return bool.TryParse(str, out bool flag) && flag;
    }

    public void SendVibrationMsg(string RoleName, string IP, VibrationData data, bool is_controller20 = false)
    {
        StringBuilder parameterArrayLeft = new StringBuilder();
        StringBuilder parameterArrayRight = new StringBuilder();

        if (!is_controller20)
        {
            AppendParameter(parameterArrayLeft, "Vibrators", data.Virbators[0].ActiveCommand);
            AppendParameter(parameterArrayLeft, "Duration", data.Virbators[0].Duration);
            AppendParameter(parameterArrayLeft, "Amplitude", data.Virbators[0].Amplitude);

            AppendParameter(parameterArrayRight, "Vibrators", data.Virbators[1].ActiveCommand);
            AppendParameter(parameterArrayRight, "Duration", data.Virbators[1].Duration);
            AppendParameter(parameterArrayRight, "Amplitude", data.Virbators[1].Amplitude);
        }
        else
        {
            AppendParameter(parameterArrayLeft, "Vibrators", data.Virbator_20[0].ActiveCommand);
            AppendParameter(parameterArrayLeft, "Duration", data.Virbator_20[0].Duration);
            AppendParameter(parameterArrayLeft, "Amplitude", data.Virbator_20[0].Amplitude);
            AppendParameter(parameterArrayLeft, "Rate", data.Virbator_20[0].Rate);

            AppendParameter(parameterArrayRight, "Vibrators", data.Virbator_20[1].ActiveCommand);
            AppendParameter(parameterArrayRight, "Duration", data.Virbator_20[1].Duration);
            AppendParameter(parameterArrayRight, "Amplitude", data.Virbator_20[1].Amplitude);
            AppendParameter(parameterArrayRight, "Rate", data.Virbator_20[1].Rate);
        }

        string message = $"{{\"{EscapeJson(RoleName)}\":{{\"LeftHand\":[{parameterArrayLeft}],\"RightHand\":[{parameterArrayRight}]}}}}";

        Debug.Log(message);
        _udpClient.Send(new IPEndPoint(IPAddress.Parse(IP), 8920), Encoding.UTF8.GetBytes(message));
    }

    private static void AppendParameter(StringBuilder builder, string name, object value)
    {
        if (builder.Length > 0)
        {
            builder.Append(',');
        }

        builder.Append("{\"Name\":\"");
        builder.Append(EscapeJson(name));
        builder.Append("\",\"Value\":");
        builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        builder.Append('}');
    }

    private static string EscapeJson(string value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private void OnDestroy()
    {
        if (_udpClient == null) return;
        _udpClient.Received -= ReceiveMsg;
        _udpClient.Stop();
        _udpClient.Dispose();
    }
}
