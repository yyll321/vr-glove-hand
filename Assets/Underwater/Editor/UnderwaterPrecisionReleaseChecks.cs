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
    public static class UnderwaterPrecisionReleaseChecks
    {
        [MenuItem("Tools/Underwater/Research/31 Verify Precision, Finger Response and Natural Drop")]
        public static void Run(){UnderwaterHybridChecks.Run();}
        public static IEnumerator Verify(UnderwaterGraspTask task,Action<bool,string> check)
        {
            var source=task.arm.MasterSlave;
            var fixture=new GameObject("Isolated precision mapping fixture");
            var map=fixture.AddComponent<UnderwaterMasterSlaveMapper>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source),map);map.task=null;map.sand=null;map.recordTelemetry=false;
            map.mode=UnderwaterMasterSlaveMapper.Mode.Position;map.Begin(Vector3.zero,Vector3.zero,Quaternion.identity);
            Vector3 actual=Vector3.zero,previous=Vector3.zero;float maximumSpeed=0,maximumAcceleration=0;
            for(int i=0;i<540;i++)
            {
                actual=map.Step(Vector3.right*.08f,actual,1f/90);
                maximumSpeed=Mathf.Max(maximumSpeed,map.Velocity.magnitude);
                maximumAcceleration=Mathf.Max(maximumAcceleration,(map.Velocity-previous).magnitude*90);previous=map.Velocity;
            }
            float expected=(.08f-map.deadZoneMetres.x)/(map.masterHalfRange.x-map.deadZoneMetres.x)*map.slavePositionRange.x;
            check(Mathf.Abs(actual.x-expected)<.0001f && actual.x<.026f,task.name+": 80 mm hand input maps to "+(actual.x*1000).ToString("F2")+" mm fine tool displacement");
            check(maximumSpeed<=.0351f && maximumAcceleration<=.141f,task.name+": fine velocity/acceleration caps measured "+maximumSpeed.ToString("F4")+" m/s, "+maximumAcceleration.ToString("F4")+" m/s2");
            Object.Destroy(fixture);

            var hold=task.glove.GetComponent<Wa100JointPoseHold>();hold.TestCommands=true;
            var mapping=task.glove.JointMappings.First(m=>m.LinkName=="Index_Finger_L2");
            float savedClose=hold.maximumCommandSpeed,savedOpen=hold.maximumOpeningSpeed;
            float oldTime=-1,newTime=-1,openTime=-1;
            var rows=new List<string>{"profile,fixedTime,commandDegrees,acceptedDegrees,actualDegrees"};
            foreach(bool fast in new[]{false,true})
            {
                hold.maximumCommandSpeed=savedClose;hold.maximumOpeningSpeed=savedOpen;
                for(int i=0;i<100;i++){Zero(task);yield return new WaitForFixedUpdate();}
                hold.maximumCommandSpeed=fast?savedClose:55;
                float start=Time.fixedTime,achieved=-1;
                for(int i=0;i<180;i++)
                {
                    Zero(task);mapping.CurrentTarget=60;yield return new WaitForFixedUpdate();
                    float angle=mapping.Joint.jointPosition[0]*Mathf.Rad2Deg;
                    rows.Add(FormattableString.Invariant($"{(fast?"new":"old-55deg-s")},{Time.fixedTime-start:F4},60,{hold.Accepted(mapping.Joint):F4},{angle:F4}"));
                    if(angle>=54){achieved=Time.fixedTime-start;break;}
                }
                if(fast)newTime=achieved;else oldTime=achieved;
            }
            check(oldTime>0 && newTime>0 && newTime<oldTime*.65f,task.name+": physical index joint reaches 90% of 60 deg command: old="+oldTime.ToString("F3")+" s, new="+newTime.ToString("F3")+" s");
            float openStart=Time.fixedTime;
            for(int i=0;i<100;i++)
            {
                Zero(task);yield return new WaitForFixedUpdate();
                if(Mathf.Abs(mapping.Joint.jointPosition[0]*Mathf.Rad2Deg)<6){openTime=Time.fixedTime-openStart;break;}
            }
            check(openTime>0 && openTime<.5f,task.name+": physical reopening below 6 deg within "+openTime.ToString("F3")+" s");
            File.WriteAllLines("Logs/HybridReview/finger-response-"+(task.arm.SeabedArea?"seabed":"pool")+".csv",rows);
            hold.maximumCommandSpeed=savedClose;hold.maximumOpeningSpeed=savedOpen;
            for(int i=0;i<70;i++){Zero(task);yield return new WaitForFixedUpdate();}
            var bench=task.GetComponentInChildren<UnderwaterGraspBench>();
            float resistance=bench.Resistance(.06f,.002f,.06f,false);
            check(resistance>0 && 1/(1+resistance/bench.fingerForceBudget)>1/(1+resistance/9),task.name+": increased actuator effort retains nonzero depth/area sand resistance");
            foreach(var target in task.targets)
            {
                Vector3 home=target.transform.position;Quaternion rotation=target.transform.rotation;
                foreach(int layer in new[]{11,8,0})
                {
                    GameObject surface=null;Collider ground;Vector3 centre;
                    if(layer==11){ground=bench.sand;centre=ground.bounds.center;}
                    else
                    {
                        surface=new GameObject("Dynamic release landing fixture "+layer);surface.layer=layer;
                        surface.transform.position=bench.sand.bounds.center+Vector3.right*1.5f;
                        var box=surface.AddComponent<BoxCollider>();box.size=new Vector3(.6f,.08f,.6f);ground=box;centre=surface.transform.position;
                    }
                    target.Attach(task.arm.Wrist);target.IgnoreRobotContacts(task.arm.GetComponentsInChildren<Collider>());
                    target.transform.SetPositionAndRotation(new Vector3(centre.x,ground.bounds.max.y+.35f+target.Shape.bounds.extents.y,centre.z),rotation);
                    Physics.SyncTransforms();float start=target.transform.position.y;target.Release();
                    check(!target.Body.isKinematic && target.Body.useGravity && target.Body.detectCollisions && target.Body.collisionDetectionMode==CollisionDetectionMode.ContinuousDynamic,target.name+": release restores gravity, dynamic body and CCD on landing layer "+layer);
                    for(int i=0;i<180;i++)yield return new WaitForFixedUpdate();
                    float clearance=target.Shape.bounds.min.y-ground.bounds.max.y;
                    check(start-target.transform.position.y>.25f && Mathf.Abs(clearance)<.009f && target.Body.velocity.magnitude<.1f,target.name+": lands without tunnelling on layer "+layer+", clearance="+clearance.ToString("F4")+" m");
                    check(target.PendingHandPairs==0,target.name+": release fixture restores all robot collision pairs");
                    if(surface)Object.Destroy(surface);
                }
                target.Body.isKinematic=true;target.transform.SetPositionAndRotation(home,rotation);Physics.SyncTransforms();
                target.Body.isKinematic=false;target.Body.velocity=target.Body.angularVelocity=Vector3.zero;
            }
        }
        static void Zero(UnderwaterGraspTask task){foreach(var m in task.glove.JointMappings)if(m.Joint)m.CurrentTarget=0;}
    }
}
