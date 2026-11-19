using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TankTogether.M0;

namespace TankTogether.Editor
{
    // Continuous input-only route audit. Reset is allowed ONLY when beginning a new run at spawn.
    public static class WhiteboxContinuousRun
    {
        [Serializable] public class Sample
        {
            public float time,tilt,speed,gripError;
            public Vector3 tank;
            public Vector3[] players;
            public int grounded,grips,failures;
            public bool broken,cameraOccluded;
        }
        [Serializable] public class Command
        {
            public string name,outcome;
            public Vector3 target;
            public Sample start,end;
        }
        [Serializable] public class Report
        {
            public string utc,id,baseline,scope="Continuous from normal spawn; only SetInput/SetExternalView; no intermediate placement, no physics overrides",conclusion;
            public bool complete;
            public List<Command> commands=new List<Command>();
            public List<Sample> samples=new List<Sample>();
        }
        static M0CarryLab lab;
        static Transform terrain;
        static Report report;
        static bool active,busy;
        static float startTime;
        static string file;
        public static bool Busy=>busy;
        public static string Status=>report==null?"No run":JsonUtility.ToJson(new Command {name=busy?"running":"idle",outcome=lab?lab.Message:"no lab",end=lab?Read():null},true);
        static bool Grounded(M0CarryLab.Actor a)=>Physics.Raycast(a.body.position+Vector3.up*.08f,Vector3.down,.16f,~0,QueryTriggerInteraction.Ignore);
        static Sample Read()
        {
            var s=new Sample {time=Time.time-startTime,tank=lab.Tank.position,tilt=Vector3.Angle(lab.Tank.transform.up,Vector3.up),speed=lab.Tank.linearVelocity.magnitude,gripError=lab.MaxGripError,broken=lab.Broken,failures=lab.FailureCount,players=new Vector3[4]};
            for(int i=0;i<4;i++){s.players[i]=lab.Players[i].body.position;if(Grounded(lab.Players[i]))s.grounded++;if(lab.IsGripping(i))s.grips++;}
            var camera=Camera.main;
            if(camera)
            {
                var focus=lab.Players[0].body.position+Vector3.up*1.25f;
                foreach(var h in Physics.RaycastAll(focus,(camera.transform.position-focus).normalized,Vector3.Distance(camera.transform.position,focus)))
                    if(h.collider.transform.IsChildOf(terrain)){s.cameraOccluded=true;break;}
            }
            return s;
        }
        static void Flush()=>File.WriteAllText(file,JsonUtility.ToJson(report,true));
        static IEnumerator Steps(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
        static void Input(Vector3 move,bool jump=false,bool sprint=false,bool crouch=false)
        {
            for(int i=0;i<4;i++)lab.SetInput(i,move,true,jump,crouch,sprint);
            if(move.sqrMagnitude>.01f)lab.SetExternalView(0,Mathf.Atan2(move.x,move.z)*Mathf.Rad2Deg,24);
        }
        static IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var image=ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes("Logs/continuous-"+report.id+"-"+name+".png",image.EncodeToPNG());UnityEngine.Object.Destroy(image);
        }
        static IEnumerator Monitor()
        {
            int tick=0;
            while(active&&lab&&EditorApplication.isPlaying)
            {
                yield return new WaitForFixedUpdate();tick++;
                if(tick%5==0)report.samples.Add(Read());
                if(tick%250==0)Flush();
            }
        }
        public static void Begin(string id)
        {
            if(active||busy)throw new InvalidOperationException("Finish current continuous run before restarting at spawn");
            lab=UnityEngine.Object.FindFirstObjectByType<M0CarryLab>();
            if(!EditorApplication.isPlaying||!lab||!lab.GetComponent<WhiteboxCarryLevel>()||lab.Testing)throw new InvalidOperationException("Play idle whitebox first");
            terrain=lab.GetComponent<WhiteboxCarryLevel>().levelRoot;
            if(Mathf.Abs(Time.timeScale-1)>.001f)throw new InvalidOperationException("Use normal time scale");
            if(id.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new ArgumentException("Invalid run id");
            report=new Report {utc=DateTime.UtcNow.ToString("O"),id=id,baseline=lab.baseline.Signature};
            file="Logs/continuous-"+id+".json";
            lab.Testing=true;lab.ResetLab();startTime=Time.time;active=true;busy=true;
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            lab.StartCoroutine(Monitor());lab.StartCoroutine(Opening());
        }
        static IEnumerator Opening()
        {
            var c=new Command {name="01_spawn_to_second_gap",target=new Vector3(-43,0,-2),start=Read()};report.commands.Add(c);
            try
            {
                yield return Steps(60);Input(Vector3.zero);yield return Steps(20);
                for(int n=0;n<250;n++)
                {
                    float dz=-2-lab.Tank.position.z;Input(Vector3.forward*Mathf.Clamp(dz*.7f,-1,1));yield return Steps(1);
                    if(Mathf.Abs(dz)<.05f&&Mathf.Abs(lab.Tank.linearVelocity.z)<.1f)break;
                }
                Input(Vector3.zero);yield return Steps(30);yield return Shot("start_lane");
                bool first=false,second=false;
                for(int step=0;step<1600;step++)
                {
                    float front=float.MaxValue;for(int i=0;i<4;i++)front=Mathf.Min(front,lab.Players[i].body.position.x);
                    bool jump=false;
                    if(!first&&front<=-26.55f){jump=true;first=true;}
                    if(!second&&front<=-37.65f){jump=true;second=true;}
                    Input(Vector3.left,jump,true);yield return Steps(1);
                    bool all=second;for(int i=0;i<4;i++)all &= Grounded(lab.Players[i])&&lab.Players[i].body.position.x< -40.35f;
                    if(all){c.outcome="crossed_both_gaps";break;}
                    if(lab.Broken){c.outcome="broken";break;}
                    if(Read().players[0].y< -7){c.outcome="fell";break;}
                }
                Input(Vector3.zero);yield return Steps(100);c.end=Read();
                if(c.outcome==null)c.outcome="timeout";
                Flush();yield return Shot("second_gap_landed");
            }
            finally{Input(Vector3.zero);busy=false;Flush();}
        }
        public static void Move(string name,Vector3 target,float seconds=12,bool sprint=false,float jumpFrontX=999,float jumpDistance=-1,bool crouch=false)
        {
            RequireIdle();busy=true;lab.StartCoroutine(Moving(name,target,seconds,sprint,jumpFrontX,jumpDistance,crouch));
        }
        static void RequireIdle(){if(!active||busy||!lab||lab.Broken)throw new InvalidOperationException("Need active unbroken idle run");}
        static IEnumerator Moving(string name,Vector3 target,float seconds,bool sprint,float jumpFrontX,float jumpDistance,bool crouch)
        {
            var c=new Command {name=name,target=target,start=Read()};report.commands.Add(c);
            bool jumped=false;float travelled=0;Vector3 previous=lab.Tank.position;
            try
            {
                for(int n=0;n<seconds/Time.fixedDeltaTime;n++)
                {
                    Vector3 delta=Vector3.ProjectOnPlane(target-lab.Tank.position,Vector3.up);float distance=delta.magnitude;
                    if(distance<.16f){c.outcome="target_reached";break;}
                    float front=float.MaxValue;for(int i=0;i<4;i++)front=Mathf.Min(front,lab.Players[i].body.position.x);
                    bool jump=!jumped&&((jumpFrontX!=999&&front<=jumpFrontX)||(jumpDistance>=0&&travelled>=jumpDistance));
                    if(jump)jumped=true;
                    float strength=Mathf.Min(1,distance*.8f);
                    Input(delta.normalized*strength,jump,sprint,crouch);yield return Steps(1);
                    travelled+=Vector3.ProjectOnPlane(lab.Tank.position-previous,Vector3.up).magnitude;previous=lab.Tank.position;
                    if(lab.Broken){c.outcome="broken";break;}
                    bool fell=false;foreach(var a in lab.Players)if(a.body.position.y<Mathf.Min(c.start.tank.y-lab.baseline.carryBottomHeight-lab.tankSize.y/2,target.y)-5)fell=true;
                    if(fell){c.outcome="fell_below_approach";break;}
                }
                Input(Vector3.zero);yield return Steps(75);c.end=Read();if(c.outcome==null)c.outcome="timeout_or_blocked";
                Flush();yield return Shot(name);
            }
            finally{Input(Vector3.zero);busy=false;Flush();}
        }
        public static void Drive(string name,Vector3 direction,float seconds,bool jump=false,bool sprint=false,float turn=0,bool crouch=false)
        {
            RequireIdle();busy=true;lab.StartCoroutine(Driving(name,direction,seconds,jump,sprint,turn,crouch));
        }
        static IEnumerator Driving(string name,Vector3 direction,float seconds,bool jump,bool sprint,float turn,bool crouch)
        {
            var c=new Command {name=name,target=direction,start=Read()};report.commands.Add(c);
            try
            {
                for(int n=0;n<seconds/Time.fixedDeltaTime;n++)
                {
                    if(turn==0)Input(direction,jump&&n==0,sprint,crouch);
                    else for(int i=0;i<4;i++)
                    {
                        var radial=Vector3.ProjectOnPlane(lab.Players[i].body.position-lab.Tank.position,Vector3.up).normalized;
                        lab.SetInput(i,Vector3.ClampMagnitude(direction+Vector3.Cross(Vector3.up,radial)*turn,1),true,jump&&n==0,crouch,sprint);
                    }
                    yield return Steps(1);if(lab.Broken)break;
                }
                Input(Vector3.zero);yield return Steps(75);c.end=Read();c.outcome=lab.Broken?"broken":"input_completed";
                Flush();yield return Shot(name);
            }
            finally{Input(Vector3.zero);busy=false;Flush();}
        }
        public static void ReplayToRamp(string id)
        {
            Begin(id);lab.StartCoroutine(RampSequence());
        }
        static IEnumerator WaitIdle(){while(busy)yield return new WaitForFixedUpdate();}
        static IEnumerator RampSequence()
        {
            yield return WaitIdle();if(lab.Broken)yield break;
            Move("02_approach_lane_change",new Vector3(-47.5f,-.5f,-2),8);yield return WaitIdle();if(lab.Broken)yield break;
            Move("03_jump_sideways_to_inner_right",new Vector3(-47.6f,-1.5f,2),8,true,999,.3f);yield return WaitIdle();if(lab.Broken)yield break;
            Move("04_up_one_metre_step",new Vector3(-57,-.5f,2),10,true,-49);yield return WaitIdle();if(lab.Broken)yield break;
            Move("05_jump_lane_exit",new Vector3(-66,-2.3f,2),10,true,-60.1f);yield return WaitIdle();if(lab.Broken)yield break;
            if(lab.Tank.position.x> -65)
            {Drive("06_recover_exit_step",new Vector3(-1,0,-.4f),1.2f,true,true);yield return WaitIdle();if(lab.Broken)yield break;}
            Move("07_down_ramp_to_platform",new Vector3(-96,-15.5f,-12.5f),12);yield return WaitIdle();
        }
        public static void ReplayToRing(string id)
        {
            Begin(id);lab.StartCoroutine(RingSequence());
        }
        static IEnumerator RingSequence()
        {
            yield return RampSequence();if(lab.Broken)yield break;
            Move("08_descend_first_ring_platform",new Vector3(-103,-18.5f,-17),10,false,999,2);yield return WaitIdle();if(lab.Broken)yield break;
            Move("09_jump_ring_seam",new Vector3(-108,-20,-26),12,false,999,4);yield return WaitIdle();if(lab.Broken)yield break;
            Move("10_ring_runup",new Vector3(-107.8f,-20,-28.5f),8);yield return WaitIdle();if(lab.Broken)yield break;
            Move("11_run_jump_diagonal_gap",new Vector3(-119,-21.5f,-32),12,true,-110.4f);yield return WaitIdle();
        }
        public static void Finish(string conclusion)
        {
            if(!active||busy)throw new InvalidOperationException("Wait for current input to complete");
            Input(Vector3.zero);report.samples.Add(Read());report.complete=true;report.conclusion=conclusion;active=false;Flush();lab.Testing=false;
        }
    }
}