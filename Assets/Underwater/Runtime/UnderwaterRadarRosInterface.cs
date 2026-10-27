using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace UnderwaterBaseline
{
    // Transport-independent ROSBridge contract. No network connection is opened.
    // A future WebSocket adapter sends Outgoing messages and passes received JSON
    // to ReceiveConfiguration. All Unity mutations stay on the main thread.
    public sealed class UnderwaterRadarRosInterface : MonoBehaviour
    {
        public UnderwaterGeometryRadar radar;
        public string configurationTopic="/geometry_radar/config",scanTopic="/geometry_radar/frame";
        public string requestTopic="/geometry_radar/config_request";
        public event Action<string> Outgoing;
        readonly ConcurrentQueue<string> incoming=new ConcurrentQueue<string>();
        string previous;
        [Serializable] sealed class StringMessage {public string data;}
        [Serializable] sealed class Envelope {public string op="publish",topic;public StringMessage msg;}
        void OnEnable(){if(!radar)radar=GetComponent<UnderwaterGeometryRadar>();if(radar)radar.ScanCompleted+=OnFrame;}
        void OnDisable(){if(radar)radar.ScanCompleted-=OnFrame;}
        public string Wrap(string topic,string json)=>JsonUtility.ToJson(new Envelope{topic=topic,msg=new StringMessage{data=json}});
        public string AdvertiseJson(string topic)=>"{\"op\":\"advertise\",\"topic\":\""+topic+"\",\"type\":\"std_msgs/String\"}";
        public string SubscribeJson()=>"{\"op\":\"subscribe\",\"topic\":\""+configurationTopic+"\",\"type\":\"std_msgs/String\"}";
        public void ReceiveConfiguration(string json)
        {
            if(string.IsNullOrWhiteSpace(json) || json.Length>16384 || incoming.Count>=8)return;
            incoming.Enqueue(json);
        }
        void Update()
        {
            if(!radar)return;
            while(incoming.TryDequeue(out string json))
            {
                try
                {
                    if(json.Contains("\"msg\"")){var e=JsonUtility.FromJson<Envelope>(json);if(e.topic!=configurationTopic || e.msg==null)continue;json=e.msg.data;}
                    radar.ApplyConfiguration(json);
                }
                catch(Exception e){Debug.LogWarning("Radar configuration rejected: "+e.Message);}
            }
            string config=radar.ConfigurationJson();
            if(config!=previous){previous=config;Outgoing?.Invoke(Wrap(requestTopic,config));}
        }
        void OnFrame(UnderwaterGeometryRadar.Snapshot frame){Outgoing?.Invoke(Wrap(scanTopic,JsonUtility.ToJson(frame)));}
    }
}
