using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TankTogether.M0;

namespace TankTogether.Editor
{
    public static class WhiteboxRouteAudit
    {
        [Serializable] public class Leg
        {
            public string name, scope, outcome, message;
            public Vector3 start, target, end;
            public float seconds, maxGripError, maxTilt, cameraOccludedSeconds;
            public bool reached, broken;
            public int initialGrounded, initialGrips;
        }
        [Serializable] public class Report { public string utc, baseline; public List<Leg> legs=new List<Leg>(); public bool complete; }
        static Report report;
        static string reportFile="Logs/whitebox-route-audit-v2.json";
        static M0CarryLab lab;
        static Transform terrain;
        static bool lastReached, rampOnly;
        [MenuItem("Tank Together/Whitebox/Run Ramp Retest")]
        public static void RampRetest(){rampOnly=true;Focused();}
        static IEnumerator Steps(int count){for(int i=0;i<count;i++)yield return new WaitForFixedUpdate();}
        static void Flush(){File.WriteAllText(reportFile,JsonUtility.ToJson(report,true));}
        static IEnumerator Setup(Vector3 feet, bool slope=false)
        {
            lab.ResetLab(); lab.Tank.transform.parent.position+=feet;
            Physics.SyncTransforms();
            if(slope)
            {
                foreach(Transform child in lab.Tank.transform.parent) if(child.name=="Safe cotton stand"||child.name=="Stand base")child.gameObject.SetActive(false);
                if(!Floor(feet,out var centre))throw new InvalidOperationException("No ramp under fixture");
                var normal=centre.normal;
                lab.Tank.rotation=Quaternion.FromToRotation(Vector3.up,normal)*Quaternion.Euler(0,-90,0);
                lab.Tank.position=new Vector3(feet.x,centre.point.y+lab.standingHeight+lab.tankSize.y/(2*normal.y)+.01f,feet.z);
                for(int i=0;i<4;i++)
                {
                    var pos=lab.Players[i].body.position;
                    if(!Floor(pos,out var ground))throw new InvalidOperationException("No ramp under actor");
                    lab.PlacePlayer(i,new Vector3(pos.x,ground.point.y+.01f,pos.z));
                }
                Physics.SyncTransforms();
                for(int i=0;i<4;i++)lab.SetInput(i,Vector3.zero,true);
            }
            yield return Steps(60);
            for(int i=0;i<4;i++)lab.SetInput(i,Vector3.zero,true);
            yield return Steps(20);
        }
        static bool Floor(Vector3 p,out RaycastHit hit)
        {
            foreach(var h in Physics.RaycastAll(p+Vector3.up*1.1f,Vector3.down,3))
                if(h.collider.transform.IsChildOf(terrain)&&h.normal.y>.5f){hit=h;return true;}
            hit=default;return false;
        }
        static IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var t=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes("Logs/route-"+name+".png",t.EncodeToPNG());UnityEngine.Object.Destroy(t);
        }
        static IEnumerator Move(string name,Vector3 target,string scope,float jumpX=999,float limit=16)
        {
            var item=new Leg {name=name,scope=scope,start=lab.Tank.position,target=target};
            for(int i=0;i<4;i++)
            {
                if(lab.IsGripping(i))item.initialGrips++;
                if(Physics.Raycast(lab.Players[i].body.position+Vector3.up*.08f,Vector3.down,.16f))item.initialGrounded++;
            }
            if(lab.Broken||item.initialGrips!=4||item.initialGrounded!=4)
            {
                item.outcome="invalid_fixture_not_a_route_failure";item.end=lab.Tank.position;report.legs.Add(item);Flush();lastReached=false;yield break;
            }
            float best=float.MaxValue, noProgress=0, jumpCooldown=0;bool explicitJump=false;
            for(int step=0;step<limit/Time.fixedDeltaTime;step++)
            {
                Vector3 delta=Vector3.ProjectOnPlane(target-lab.Tank.position,Vector3.up);float distance=delta.magnitude;
                if(distance<.4f){item.reached=true;break;}
                if(distance<best-.12f){best=distance;noProgress=0;}else noProgress+=Time.fixedDeltaTime;
                Vector3 direction=delta.normalized;
                bool jump=false;int grounded=0;float front=float.MaxValue;
                for(int i=0;i<4;i++)
                {
                    var a=lab.Players[i];front=Mathf.Min(front,a.body.position.x);
                    if(Physics.Raycast(a.body.position+Vector3.up*.08f,Vector3.down,.16f))grounded++;
                    if(Floor(a.body.position,out var here) && (!Floor(a.body.position+direction*.85f,out var next) || next.point.y-here.point.y>.22f)) jump=true;
                }
                if(jumpX!=999){jump=!explicitJump&&front<=jumpX; if(jump)explicitJump=true;}
                else jump &= grounded==4&&jumpCooldown<=0;
                if(jump)jumpCooldown=.9f;else jumpCooldown-=Time.fixedDeltaTime;
                float input=jumpX==-99999?1:Mathf.Min(1,distance*.7f);
                for(int i=0;i<4;i++)lab.SetInput(i,direction*input,true,jump);
                lab.SetExternalView(0,Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg,24);
                yield return new WaitForFixedUpdate();
                item.seconds+=Time.fixedDeltaTime;
                item.maxGripError=Mathf.Max(item.maxGripError,lab.MaxGripError);
                item.maxTilt=Mathf.Max(item.maxTilt,Vector3.Angle(lab.Tank.transform.up,Vector3.up));
                var focus=lab.Players[0].body.position+Vector3.up*1.25f;
                foreach(var h in Physics.RaycastAll(focus,(Camera.main.transform.position-focus).normalized,Vector3.Distance(Camera.main.transform.position,focus)))
                    if(h.collider.transform.IsChildOf(terrain)){item.cameraOccludedSeconds+=Time.fixedDeltaTime;break;}
                bool fell=false;
                foreach(var a in lab.Players)if(a.body.position.y<Mathf.Min(item.start.y-lab.baseline.carryBottomHeight-lab.tankSize.y/2,target.y)-5)fell=true;
                if(lab.Broken){item.outcome="tank_broken";break;}
                if(fell){item.outcome="fell_below_route";break;}
                if(noProgress>3){item.outcome="stuck_over_3_seconds";break;}
            }
            for(int i=0;i<4;i++)lab.SetInput(i,Vector3.zero,true);
            if(item.reached)yield return Steps(40);
            item.broken=lab.Broken;item.end=lab.Tank.position;item.message=lab.Message;
            item.reached &= !lab.Broken;
            if(item.reached)
            {
                foreach(var a in lab.Players)if(!Physics.Raycast(a.body.position+Vector3.up*.08f,Vector3.down,.16f))item.reached=false;
                if(!item.reached)item.outcome="unstable_after_braking";
            }
            if(string.IsNullOrEmpty(item.outcome))item.outcome=item.reached?"reached_and_braked":"timeout";
            report.legs.Add(item);Flush();lastReached=item.reached;
            yield return Shot(name);
        }
        [MenuItem("Tank Together/Whitebox/Run Full Route Audit")]
        public static void Start()
        {
            lab=UnityEngine.Object.FindFirstObjectByType<M0CarryLab>();
            if(!EditorApplication.isPlaying||!lab||!lab.GetComponent<WhiteboxCarryLevel>()||lab.Testing)throw new InvalidOperationException("Play idle whitebox first");
            terrain=lab.GetComponent<WhiteboxCarryLevel>().levelRoot;
            EditorApplication.ExecuteMenuItem("Window/General/Game");lab.StartCoroutine(Run());
        }
        [MenuItem("Tank Together/Whitebox/Run Focused Route Audit")]
        public static void Focused()
        {
            lab=UnityEngine.Object.FindFirstObjectByType<M0CarryLab>();
            if(!EditorApplication.isPlaying||!lab||!lab.GetComponent<WhiteboxCarryLevel>()||lab.Testing)throw new InvalidOperationException("Play idle whitebox first");
            terrain=lab.GetComponent<WhiteboxCarryLevel>().levelRoot;
            EditorApplication.ExecuteMenuItem("Window/General/Game");lab.StartCoroutine(RunFocused());
        }
        static IEnumerator RunFocused()
        {
            reportFile=rampOnly?"Logs/whitebox-route-ramp.json":"Logs/whitebox-route-focused.json";
            report=new Report {utc=DateTime.UtcNow.ToString("O"),baseline=lab.baseline.Signature};lab.Testing=true;
            try
            {
                yield return Setup(new Vector3(-94,-15.5f,-10));
                yield return Move("18_ramp_approach",new Vector3(-89,-13.68f,-5),"walk_from_flat_no_slope_teleport",-99999,12);
                if(lastReached)yield return Move("19_uphill",new Vector3(-65,-1.5f,-5),"continuous_uphill_no_jump",-99999,30);
                if(lastReached)yield return Move("20_downhill",new Vector3(-90,-14.2f,-5),"continuous_return_downhill_no_jump",-99999,20);
                if(rampOnly){report.complete=true;Flush();yield break;}
                yield return Setup(new Vector3(-165,-36.5f,4));
                yield return Move("21_final_path_entry",new Vector3(-185,-36.5f,6),"walk_into_narrow_section_from_wide_platform",999,16);
                yield return Setup(new Vector3(-245,64.5f,5));
                yield return Move("22_finish_detour",new Vector3(-245,64.5f,-6),"isolated_upper_platform_detour_around_hole",999,12);
                if(lastReached)yield return Move("23_finish_marker",new Vector3(-231,65.5f,-6),"upper_platform_approach_marker",999,15);
                yield return Setup(new Vector3(-126,-29.5f,4));
                var before=lab.Players[3].body.position;
                lab.PlacePlayer(3,new Vector3(-126,-61,4));yield return Steps(30);
                report.legs.Add(new Leg {name="24_single_death_recovery",scope="forced_death_fixture_in_centre",start=before,end=lab.Players[3].body.position,outcome="observe_respawn_distance",message="Tank="+lab.Tank.position+" broken="+lab.Broken});Flush();
                report.complete=true;Flush();
            }
            finally {lab.ResetLab();lab.Testing=false;rampOnly=false;}
        }
        static IEnumerator Run()
        {
            reportFile="Logs/whitebox-route-audit-v2.json";
            report=new Report {utc=DateTime.UtcNow.ToString("O"),baseline=lab.baseline.Signature};lab.Testing=true;
            try
            {
                yield return Setup(Vector3.zero);
                yield return Move("01_start_side",new Vector3(0,0,-4),"continuous_from_start",999,8);
                if(lastReached)yield return Move("02_start_to_bridge",new Vector3(-20,-.5f,-4),"continuous_from_start");
                if(lastReached)yield return Move("03_first_gap",new Vector3(-33,-.5f,-4),"continuous_from_start",-26.25f);
                if(lastReached)yield return Move("04_bridge_align",new Vector3(-35.5f,-.5f,-5f),"continuous_from_start",999,8);
                if(lastReached)yield return Move("05_second_gap",new Vector3(-44,-1.5f,-6.25f),"continuous_from_start",-37.8f);
                yield return Setup(new Vector3(-41,-1.5f,-6.25f));
                yield return Move("06_split_left",new Vector3(-61,-.5f,-6.25f),"isolated_after_blocker");
                yield return Setup(new Vector3(-41,-.5f,6.25f));
                yield return Move("07_split_right",new Vector3(-61,-.5f,6.25f),"isolated_after_blocker");
                yield return Setup(new Vector3(-65,-1.5f,0),true);
                yield return Move("08_down_ramp",new Vector3(-90,-14.2f,0),"isolated_after_blocker",999,20);
                if(lastReached)yield return Move("09_up_ramp",new Vector3(-64,-1,0),"return_on_same_physics",999,20);
                yield return Setup(new Vector3(-94,-15.5f,-10));
                yield return Move("10_inner_turn",new Vector3(-104,-18.5f,-16),"isolated_after_blocker");
                yield return Setup(new Vector3(-105,-20,-27));
                yield return Move("11_arc_platforms",new Vector3(-118,-21.5f,-32),"isolated_after_blocker");
                yield return Setup(new Vector3(-138,-23,-32));
                yield return Move("12_arc_to_centre",new Vector3(-139,-27.5f,-19),"isolated_after_blocker");
                yield return Setup(new Vector3(-126,-29.5f,4));
                yield return Move("13_centre_exit",new Vector3(-151,-36.5f,4),"isolated_after_blocker",999,20);
                yield return Setup(new Vector3(-151,-36.5f,4));
                yield return Move("14_exit_bridge",new Vector3(-171,-36.5f,4),"isolated_after_blocker");
                yield return Setup(new Vector3(-173,-36.5f,6.5f));
                yield return Move("15_final_narrow_path",new Vector3(-223,-36.5f,6.5f),"isolated_after_blocker",999,25);
                yield return Setup(new Vector3(-234.7f,-36.5f,0));
                var before=lab.Tank.position;yield return Steps(300);
                report.legs.Add(new Leg {name="16_terminal_lift_wait",scope="isolated_after_blocker",start=before,end=lab.Tank.position,seconds=6,outcome="observe_static_terminal",message="Check actual displacement; no automatic lift or checkpoint assumed"});Flush();yield return Shot("16_terminal_lift_wait");
                yield return Setup(new Vector3(-245,64.5f,5));
                yield return Move("17_upper_finish",new Vector3(-231,65.5f,-6),"isolated_upper_platform_not_reached_from_below");
                report.complete=true;Flush();
            }
            finally {lab.ResetLab();lab.Testing=false;rampOnly=false;}
        }
    }
}
