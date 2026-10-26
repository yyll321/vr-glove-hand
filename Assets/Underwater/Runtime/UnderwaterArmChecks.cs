using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace UnderwaterBaseline
{
    static class UnderwaterArmChecks
    {
        public static IEnumerator Run(Action<bool,string> check)
        {
            foreach(var arm in UnityEngine.Object.FindObjectsOfType<PicoArmPoseDriver>())
            {
                arm.ResolveBindings();
                var rotations=arm.Joints.Select(j=>j.localRotation).ToArray();
                var contact=arm.Contact;
                bool protect=arm.PreventSurfacePenetration;
                int mask=arm.ContactMask;float smoothing=arm.TargetSmoothingSeconds;
                try
                {
                    contact.BeginMotion();
                    check(contact.ShapeCount>=15 && !contact.Hand.DisableColliders &&
                        contact.Hand.GetComponentsInChildren<Collider>().Count(c=>c.enabled)>=15,
                        arm.name+": palm and all finger meshes covered; convex articulation contacts enabled");
                    // Tests run at a separate site: no changes to the user's scenery.
                    var floor=new GameObject("WA100 contact fixture floor");floor.layer=8;
                    var slab=floor.AddComponent<BoxCollider>();slab.size=new Vector3(4,0.015f,4);
                    var renderers=contact.Hand.GetComponentsInChildren<Renderer>();
                    float bottom=renderers.Min(r=>r.bounds.min.y);
                    floor.transform.position=new Vector3(arm.Wrist.position.x,bottom-0.15f,arm.Wrist.position.z);
                    Physics.SyncTransforms();
                    check(!contact.Blocked(arm.Wrist.position,arm.Wrist.rotation),arm.name+": saved mounting pose starts free of environmental overlap");
                    check(contact.Blocked(arm.Wrist.position-Vector3.up*0.15f,arm.Wrist.rotation),
                        arm.name+": full hand query detects a 15 mm surface before the wrist centre reaches it");
                    check(contact.SweepBlocked(arm.Wrist.position,arm.Wrist.rotation,arm.Wrist.position-Vector3.up),
                        arm.name+": swept hand catches a thin surface even when the destination is beyond it");
                    check(contact.Blocked(arm.Wrist.position,Quaternion.AngleAxis(35,arm.Wrist.right)*arm.Wrist.rotation) ||
                        contact.Blocked(arm.Wrist.position,Quaternion.AngleAxis(-35,arm.Wrist.right)*arm.Wrist.rotation),
                        arm.name+": rotating the hand is checked even when its wrist centre stays fixed");
                    UnityEngine.Object.Destroy(floor);yield return null;

                    // Reachable targets come from actual forward kinematics, not a copied IK formula.
                    arm.PreventSurfacePenetration=false;arm.TargetSmoothingSeconds=0;
                    Vector3 input=new Vector3(0.2f,1.1f,0.4f);
                    for(int targetIndex=0;targetIndex<3;targetIndex++)
                    {
                        for(int i=0;i<4;i++)arm.Joints[i].localRotation=rotations[i];
                        arm.Joints[targetIndex].localRotation*=Quaternion.AngleAxis(targetIndex==1?12:-10,targetIndex==0?Vector3.up:Vector3.forward);
                        Vector3 goal=arm.Wrist.position;
                        for(int i=0;i<4;i++)arm.Joints[i].localRotation=rotations[i];
                        Vector3 before=arm.Wrist.position;
                        arm.ApplyControllerPose(input,Quaternion.identity,false,1f/72);
                        arm.ApplyControllerPose(input,Quaternion.identity,true,1f/72);
                        Vector3 offset=arm.TrackingOrigin.InverseTransformVector(goal-before)/arm.PositionScale;
                        bool rateBound=true,finite=true;
                        for(int frame=1;frame<=240;frame++)
                        {
                            arm.ApplyControllerPose(input+offset*Mathf.Min(1,frame/90f),Quaternion.identity,true,1f/72);
                            rateBound&=arm.MaximumAppliedJointStep<arm.DegreesPerSecond/72+0.06f;
                            finite&=float.IsFinite(arm.Wrist.position.x)&&float.IsFinite(arm.PositionError);
                        }
                        float error=Vector3.Distance(arm.Wrist.position,goal);
                        check(error<0.008f && rateBound && finite,
                            $"{arm.name}: DLS FK target {targetIndex} converges with per-frame speed bound ({error:F4} m)");
                    }

                    // Test an unreachable request without invalid rotations or rate spikes.
                    arm.ApplyControllerPose(input,Quaternion.identity,false,1f/72);
                    arm.ApplyControllerPose(input,Quaternion.identity,true,1f/72);
                    bool stable=true;
                    for(int frame=1;frame<=180;frame++)
                    {
                        arm.ApplyControllerPose(input+new Vector3(0,frame*0.015f,0),Quaternion.Euler(60,30,20),true,1f/72);
                        stable&=float.IsFinite(arm.PositionError)&&arm.MaximumAppliedJointStep<arm.DegreesPerSecond/72+0.06f;
                    }
                    check(stable && arm.PositionError>0.1f,arm.name+": unreachable position stays finite and speed-limited; residual is reported");

                    // A complete downward controller trajectory must stop the full hand.
                    for(int i=0;i<4;i++)arm.Joints[i].localRotation=rotations[i];
                    contact.Hand.SyncMountedRoot();
                    floor=new GameObject("WA100 trajectory fixture floor");floor.layer=8;
                    slab=floor.AddComponent<BoxCollider>();slab.size=new Vector3(4,0.02f,4);
                    bottom=renderers.Min(r=>r.bounds.min.y);
                    float floorTop=bottom-0.12f;
                    floor.transform.position=new Vector3(arm.Wrist.position.x,floorTop-0.01f,arm.Wrist.position.z);
                    Physics.SyncTransforms();
                    arm.PreventSurfacePenetration=true;
                    arm.ApplyControllerPose(input,Quaternion.identity,false,1f/72);
                    arm.ApplyControllerPose(input,Quaternion.identity,true,1f/72);
                    bool contacted=false;
                    for(int frame=1;frame<=180;frame++)
                    {
                        var offset=arm.TrackingOrigin.InverseTransformVector(Vector3.down*(frame*0.003f))/arm.PositionScale;
                        arm.ApplyControllerPose(input+offset,Quaternion.identity,true,1f/72);
                        contacted|=arm.SurfaceContact;
                    }
                    contact.Hand.SyncMountedRoot();
                    float minimum=renderers.Min(r=>r.bounds.min.y);
                    check(contacted && minimum>=floorTop-0.003f,
                        $"{arm.name}: downward arm trajectory stops palm/fingers above surface (clearance {minimum-floorTop:F4} m)");
                    UnityEngine.Object.Destroy(floor);yield return null;

                    // Continue toward the real pool floor / triangulated seabed rather
                    // than relying only on the synthetic thin-slab fixture.
                    arm.ApplyControllerPose(input,Quaternion.identity,false,1f/72);
                    arm.ApplyControllerPose(input,Quaternion.identity,true,1f/72);
                    contacted=false;
                    for(int frame=1;frame<=320;frame++)
                    {
                        var offset=arm.TrackingOrigin.InverseTransformVector(Vector3.down*(frame*0.004f))/arm.PositionScale;
                        arm.ApplyControllerPose(input+offset,Quaternion.identity,true,1f/72);
                        contacted|=arm.SurfaceContact;
                    }
                    contact.Hand.SyncMountedRoot();contact.BeginMotion();
                    check(contacted && !contact.Blocked(arm.Wrist.position,arm.Wrist.rotation),
                        arm.name+": actual pool/seabed blocks the full hand without a final overlap");
                    Vector3 atContact=arm.Wrist.position;
                    Vector3 liftedInput=input+arm.TrackingOrigin.InverseTransformVector(Vector3.down*1.28f+Vector3.up*0.06f)/arm.PositionScale;
                    for(int frame=0;frame<36;frame++)arm.ApplyControllerPose(liftedInput,Quaternion.identity,true,1f/72);
                    check(arm.Wrist.position.y-atContact.y>0.025f,
                        arm.name+": contact clutch permits immediate withdrawal without target wind-up");

                    var held=arm.Joints.Select(j=>j.localRotation).ToArray();
                    arm.ApplyControllerPose(Vector3.one,Quaternion.Euler(80,40,20),false,1f/72);
                    check(arm.Joints.Select((j,i)=>Quaternion.Angle(j.localRotation,held[i])).All(a=>a<0.01f),arm.name+": release still holds contact-limited pose");
                    for(int i=0;i<4;i++)arm.Joints[i].localRotation=rotations[i];
                    contact.Hand.SyncMountedRoot();
                    yield return FingerContact(arm,check);
                }
                finally
                {
                    arm.PreventSurfacePenetration=protect;arm.ContactMask=mask;arm.TargetSmoothingSeconds=smoothing;
                    for(int i=0;i<4;i++)arm.Joints[i].localRotation=rotations[i];
                    arm.ApplyControllerPose(Vector3.zero,Quaternion.identity,false,1f/72);
                    contact.Hand.SyncMountedRoot();
                }
            }
        }

        static IEnumerator FingerContact(PicoArmPoseDriver arm,Action<bool,string> check)
        {
            var hand=arm.Contact.Hand;
            var mapper=hand.GetComponent<Wa100UdexRealArticulationMapper>();
            var first=mapper.JointMappings.First(m=>m.LinkName=="Index_Finger_L1").Joint;
            var last=mapper.JointMappings.First(m=>m.LinkName=="Index_Finger_L2").Joint;
            var renderer=last.GetComponentInChildren<Renderer>();
            bool wasEnabled=mapper.enabled;
            var fingers=mapper.JointMappings.Select(m=>m.Joint).ToArray();
            var drives=fingers.Select(j=>j.xDrive).ToArray();
            GameObject obstacle=null;
            try
            {
                mapper.enabled=false;
                foreach(var joint in fingers)SetDrive(joint,0);
                for(int frame=0;frame<70;frame++)yield return new WaitForFixedUpdate();
                SetDrive(first,20);SetDrive(last,35);
                for(int frame=0;frame<60;frame++)yield return new WaitForFixedUpdate();
                Vector3 goal=renderer.bounds.center;
                SetDrive(first,0);SetDrive(last,0);
                for(int frame=0;frame<70;frame++)yield return new WaitForFixedUpdate();
                obstacle=new GameObject("WA100 finger physical contact fixture");obstacle.layer=8;
                var block=obstacle.AddComponent<BoxCollider>();block.size=Vector3.one*0.025f;
                var probe=obstacle.AddComponent<Wa100ContactProbe>();
                obstacle.transform.position=goal;Physics.SyncTransforms();
                float initialOverlap=0;
                foreach(var collider in hand.GetComponentsInChildren<Collider>())
                    if(collider.enabled && Physics.ComputePenetration(collider,collider.transform.position,collider.transform.rotation,
                        block,block.transform.position,block.transform.rotation,out Vector3 initialNormal,out float initialDepth))initialOverlap=Mathf.Max(initialOverlap,initialDepth);
                check(initialOverlap<0.001f,$"{arm.name}: finger obstacle starts clear of the hand ({initialOverlap:F4} m)");
                SetDrive(first,20);SetDrive(last,35);
                for(int frame=0;frame<55;frame++)
                {
                    yield return new WaitForFixedUpdate();
                }
                float worst=0;string worstShape="none";
                foreach(var collider in hand.GetComponentsInChildren<Collider>())
                {
                    if(!collider.enabled)continue;
                    if(Physics.ComputePenetration(collider,collider.transform.position,collider.transform.rotation,
                        block,block.transform.position,block.transform.rotation,out Vector3 normal,out float depth) && depth>worst){worst=depth;worstShape=collider.transform.parent.name;}
                }
                check(probe.Touched && worst<0.005f,$"{arm.name}: driven WA100 finger makes physical contact without passing through (penetration {worst:F4} m, shape {worstShape}, touched {probe.Touched})");
            }
            finally
            {
                if(obstacle)UnityEngine.Object.Destroy(obstacle);
                for(int i=0;i<fingers.Length;i++)fingers[i].xDrive=drives[i];mapper.enabled=wasEnabled;
            }
        }

        static void SetDrive(ArticulationBody body,float target)
        {var drive=body.xDrive;drive.target=Mathf.Clamp(target,drive.lowerLimit,drive.upperLimit);body.xDrive=drive;body.WakeUp();}
    }
}
