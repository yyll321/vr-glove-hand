using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace UnderwaterBaseline.Editor
{
    [InitializeOnLoad]
    public static class UnderwaterContactFeedbackChecks
    {
        const string Key="UnderwaterContactFeedbackChecks";
        static UnderwaterContactFeedbackChecks()
        {
            EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false)){SessionState.SetBool(Key,false);new GameObject("All hand contact haptics verification").AddComponent<UnderwaterContactFeedbackProbe>();}};
        }
        [MenuItem("Tools/Underwater/Research/29 Verify All Hand Contact Haptics")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play first");
            UnderwaterPreviewCompatibility.Verify();SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
    }
    public sealed class UnderwaterContactFeedbackProbe:MonoBehaviour
    {
        [Serializable]public class Transport{public string area,details,status;public int episodes,requests,accepted,rejected;public bool actualVibrationUserVerified;}
        [Serializable]public class Report{public string time,scope="Editor real provider calls and synthetic contact fixtures; physical vibration requires wearer confirmation.";public bool passed;public string[] checks,errors;public Transport[] transports;}
        readonly List<string> checks=new List<string>(),errors=new List<string>();readonly List<Transport> routes=new List<Transport>();
        void Check(bool ok,string message){(ok?checks:errors).Add(message);}
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message);}
        IEnumerator Start()
        {
            Application.logMessageReceived+=Log;Directory.CreateDirectory("Logs/ContactFeedbackReview");
            yield return new WaitForSeconds(2);
            var motor=Object.FindObjectOfType<UnderwaterMotor>();motor.enabled=false;
            foreach(var task in Object.FindObjectsOfType<UnderwaterGraspTask>().OrderBy(t=>t.arm.SeabedArea))
            {
                task.enabled=false;task.arm.enabled=false;task.glove.enabled=false;
                foreach(var previous in task.arm.PreviousControllers)if(previous)previous.enabled=false;
                if(motor.InSeabed!=task.arm.SeabedArea)motor.SwitchArea();
                var h=task.arm.Contact.Haptics;h.Bind();
                Check(task.deliveryZone && task.deliveryZone.gameObject.activeInHierarchy && task.deliveryZone.GetComponentsInChildren<Renderer>().All(r=>!r.enabled),task.name+": delivery marker hidden, optional counting transform retained");
                var hand=task.arm.Contact.Hand;
                Check(hand.GetComponentsInChildren<ArticulationBody>().All(b=>b.GetComponent<Wa100ContactRelay>()?.owner==h),task.name+": every WA100 articulation link forwards native contacts");
                var collider=hand.GetComponentsInChildren<Collider>().First(c=>c.enabled && c.GetComponentInParent<ArticulationBody>()?.name=="Index_Finger_L2");
                var fixture=new GameObject("External fingertip tactile fixture");var box=fixture.AddComponent<BoxCollider>();box.size=Vector3.one*.012f;
                // Sensor fixture should not push the articulation into the floor.
                // Feedback queries must work even with native pairs ignored (sand).
                foreach(var shape in hand.GetComponentsInChildren<Collider>())if(shape.enabled)Physics.IgnoreCollision(shape,box,true);
                fixture.transform.position=collider.bounds.center;
                foreach(int layer in new[]{8,10,11,0})
                {
                    fixture.layer=layer;Physics.SyncTransforms();
                    Check(h.ScanContact(out var surface),task.name+": fingertip contact detected on external layer "+layer+" ("+surface+")");
                }
                fixture.layer=8;
                int requests=h.PulseRequests,accepted=h.AcceptedPulses;
                h.ReportContact(box);
                float until=Time.unscaledTime+1.2f;int contactFrames=0;
                while(Time.unscaledTime<until || contactFrames<8)
                {
                    // Keep the physical test contact in place while the solver
                    // separates it. A fixed fixture may cease touching early.
                    fixture.transform.position=collider.bounds.center;Physics.SyncTransforms();
                    h.ReportContact(box);contactFrames++;
                    yield return null;
                }
                Check(h.ContactActive && h.PulseRequests>=requests+2,task.name+": touch/stay repeats without Grip or joint motion; active="+h.ContactActive+", pulses="+(h.PulseRequests-requests)+", rendered frames="+contactFrames);
                Check(h.AcceptedPulses+h.RejectedPulses==h.PulseRequests,task.name+": every pulse records actual transport accept/reject result");
                fixture.transform.position=Vector3.one*-999;Physics.SyncTransforms();
                yield return new WaitForSeconds(.25f);
                Check(!h.ContactActive,task.name+": leaving all surfaces clears contact");
                int afterExit=h.PulseRequests;yield return new WaitForSeconds(.25f);
                Check(h.PulseRequests==afterExit,task.name+": no pulse requests in free space");
                fixture.transform.SetParent(task.arm.transform,true);fixture.transform.position=collider.bounds.center;Physics.SyncTransforms();
                Check(!h.IsExternal(box),task.name+": own ROV/hand collider excluded");
                fixture.transform.SetParent(null);box.isTrigger=true;
                Check(!h.IsExternal(box),task.name+": decorative trigger volumes excluded");box.isTrigger=false;
                fixture.transform.position=Vector3.one*-999;Physics.SyncTransforms();
                var bench=task.GetComponentInChildren<UnderwaterGraspBench>();var sandPosition=bench.sand.transform.position;
                bench.sand.transform.position=collider.bounds.center;Physics.SyncTransforms();
                bool sandTouch=h.ScanContact(out var sandName);
                Check(Physics.GetIgnoreCollision(collider,bench.sand) && sandTouch,task.name+": ignored sand collision pair still produces tactile contact ("+sandName+")");
                bench.sand.transform.position=sandPosition;Physics.SyncTransforms();
                Check(task.arm.Contact.SurfaceMask.value==1<<8 && task.arm.Contact.Skin==.003f,task.name+": solid blocking mask and original 3 mm skin unchanged");
                routes.Add(new Transport{area=task.name,details=h.TransportDetails,status=h.Status,episodes=h.ContactEpisodes,requests=h.PulseRequests,accepted=h.AcceptedPulses,rejected=h.RejectedPulses});
                Object.Destroy(fixture);yield return null;
            }
            if(motor.InSeabed)motor.SwitchArea();
            var console=Object.FindObjectOfType<UnderwaterTaskConsole>();console.SelectView(1);console.Capture();Save(console.Preview,"Logs/ContactFeedbackReview/hidden-marker.png");
            var report=new Report{time=DateTime.Now.ToString("O"),passed=errors.Count==0,checks=checks.ToArray(),errors=errors.ToArray(),transports=routes.ToArray()};
            File.WriteAllText("Logs/ContactFeedbackReview/results.json",JsonUtility.ToJson(report,true));Application.logMessageReceived-=Log;EditorApplication.isPlaying=false;
        }
        static void Save(RenderTexture rt,string path)
        {var old=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();File.WriteAllBytes(path,t.EncodeToPNG());RenderTexture.active=old;Object.Destroy(t);}
    }
}
