using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TankTogether.M0
{
    public static class M1InteractionChecks
    {
        [Serializable] public class Check { public string name, evidence; public bool passed; }
        [Serializable] public class Report { public string utc, platform, baseline; public bool passed; public List<Check> checks = new List<Check>(); }
        const int HangingPlayer = 1; // P2 spawns on the right, in line with the pit.
        static IEnumerator Steps(int count) { for(int i=0;i<count;i++) yield return new WaitForFixedUpdate(); }
        static void Input(M0CarryLab lab, Vector3 move, bool hold=true, bool jump=false, bool crouch=false)
        { for(int i=0;i<lab.LocalPlayerCount;i++) lab.SetInput(i,move,hold,jump,crouch); }
        static IEnumerator Photo(string name)
        {
            if(Application.isBatchMode) yield break;
            yield return new WaitForEndOfFrame();
            var texture=ScreenCapture.CaptureScreenshotAsTexture();
            Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/m1-"+name+".png",texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);
        }
        static IEnumerator Approach(M1InteractionLab test, float z, bool jumpOverPosts)
        {
            var lab=test.Lab;
            for(int step=0;step<900;step++)
            {
                float error=z-lab.Tank.position.z;
                // Test driver emits ordinary individual commands. It never moves a body or tank.
                float desired=Mathf.Clamp(error*1.8f,-2.5f,2.5f);
                bool jump=jumpOverPosts && step%60==30 && Mathf.Abs(error)>.12f && lab.Tank.position.z>2.5f;
                Input(lab, Vector3.forward*(desired/lab.baseline.carrySpeed),true,jump);
                yield return Steps(1);
                if(lab.Broken || (Mathf.Abs(error)<.06f && Mathf.Abs(lab.Tank.linearVelocity.z)<.12f)) break;
            }
            Input(lab,Vector3.zero);
        }
        static IEnumerator Hang(M1InteractionLab test)
        {
            test.SelectLane(true);
            yield return Steps(60);
            var trace=new List<string>();
            for(int step=0;step<400;step++)
            {
                Input(test.Lab,Vector3.forward*.3f);
                yield return Steps(1);
                if(step%10==0) trace.Add(step+" tank="+test.Lab.Tank.position+" roll="+test.Lab.Tank.rotation.eulerAngles+" p2="+test.Lab.Players[HangingPlayer].body.position+" grips="+test.Lab.IsGripping(0)+test.Lab.IsGripping(1)+test.Lab.IsGripping(2));
                if(test.Lab.Broken||(!test.Grounded(HangingPlayer)&&test.Lab.Players[HangingPlayer].body.position.y<-.04f))break;
            }
            Input(test.Lab,Vector3.zero);
            Directory.CreateDirectory("Logs");File.WriteAllLines("Logs/m1-hang-entry.txt",trace);
        }
        public static IEnumerator Run(M1InteractionLab test,bool quit=false)
        {
            var lab=test.Lab;
            lab.Testing=true;
            var r=new Report { utc=DateTime.UtcNow.ToString("O"),platform=Application.platform.ToString(),baseline=lab.baseline.Signature };
            void Record(string name,bool passed,string evidence) => r.checks.Add(new Check{name=name,passed=passed,evidence=evidence});
            test.SelectLane(false);
            yield return Steps(80);
            Record("initial_four_safe_top_contacts",test.Contacts.Mask(0)==15&&!lab.Broken,"mask="+test.Contacts.Mask(0));
            // Negative cases may arrange the scene; the positive driving/rescue cases do not teleport.
            var offset=test.parkingCenter+Vector3.up*.5f;
            lab.Tank.position+=offset;
            for(int i=0;i<lab.LocalPlayerCount;i++) lab.PlacePlayer(i,lab.Players[i].body.position+test.parkingCenter);
            lab.Tank.isKinematic=true;
            yield return Steps(60);
            Record("hovering_over_parking_does_not_count",!test.ParkingCandidate&&test.Contacts.Mask(1)==0,"mask="+test.Contacts.Mask(1));

            test.SelectLane(false);
            yield return Steps(30);
            lab.Tank.position+=test.parkingCenter;
            for(int i=0;i<lab.LocalPlayerCount;i++) lab.PlacePlayer(i,lab.Players[i].body.position+test.parkingCenter);
            test.Posts[6].gameObject.SetActive(false); test.Posts[7].gameObject.SetActive(false);
            yield return Steps(100);
            Record("two_posts_do_not_count",!test.ParkingCandidate&&test.Contacts.Mask(1)!=15,"mask="+test.Contacts.Mask(1));

            test.SelectLane(false);
            yield return Steps(60);
            // Start on original stands, drive with the documented controls into the target stands.
            yield return Approach(test,test.parkingCenter.z,true);
            Input(lab,Vector3.zero,false,false,true);
            yield return Steps(100);
            Record("drive_and_park_with_existing_controls",test.ParkingCandidate&&!lab.Broken,"z="+lab.Tank.position.z+"; mask="+test.Contacts.Mask(1)+"; error="+lab.MaxGripError);
            yield return Photo("parked");
            Input(lab,Vector3.zero,false);
            yield return Steps(40);
            Input(lab,Vector3.zero,true);
            yield return Steps(40);
            Record("parking_does_not_require_release",test.ParkingCandidate&&lab.IsGripping(0)&&lab.IsGripping(HangingPlayer),"grippedMask="+test.Contacts.Mask(1));
            Input(lab,Vector3.forward);
            yield return Steps(100);
            Record("leave_parking_without_magnet_or_new_button",!test.ParkingCandidate&&test.Contacts.Mask(1)==0&&!lab.Broken&&lab.Tank.position.z>test.parkingCenter.z+1.5f,"z="+lab.Tank.position.z+"; mask="+test.Contacts.Mask(1));

            yield return Hang(test);
            var p=lab.Players[HangingPlayer];
            Record("one_player_reaches_real_hole",!test.Grounded(HangingPlayer)&&p.body.position.y<-.04f,"feet="+p.body.position+"; tank="+lab.Tank.position);
            float minY=p.body.position.y,maxError=lab.MaxGripError,maxPenetration=0;
            for(int step=0;step<50;step++)
            {
                yield return Steps(1); minY=Mathf.Min(minY,p.body.position.y); maxError=Mathf.Max(maxError,lab.MaxGripError);
                foreach(var floor in test.Floors)
                    if(Physics.ComputePenetration(p.capsule,p.body.position,p.body.rotation,floor,floor.transform.position,floor.transform.rotation,out _,out float distance)) maxPenetration=Mathf.Max(maxPenetration,distance);
            }
            Record("finite_hanging_keeps_grip",lab.IsGripping(HangingPlayer)&&!lab.Broken&&minY>-1&&maxError<.4f,"minFeetY="+minY+"; maxHeadAnchor="+maxError);
            Record("hanging_has_no_platform_penetration",maxPenetration<.025f,"maxPenetration="+maxPenetration);
            lab.SetExternalView(HangingPlayer,0,32); yield return Photo("hanging");
            for(int step=0;step<220;step++)
            {
                for(int i=0;i<lab.LocalPlayerCount;i++) lab.SetInput(i,i==HangingPlayer?Vector3.zero:Vector3.back,true);
                yield return Steps(1);
                if(test.Grounded(HangingPlayer)&&p.body.position.z<2.2f) break;
            }
            Input(lab,Vector3.zero);
            yield return Steps(50);
            Record("two_players_rescue_without_teleport",test.Grounded(HangingPlayer)&&lab.IsGripping(HangingPlayer)&&!lab.Broken&&p.body.position.y>-.03f,"rescuedFeet="+p.body.position+"; maxError="+lab.MaxGripError);
            yield return Photo("rescued");

            yield return Hang(test);
            int failures=lab.FailureCount;
            Vector3 tankBefore=lab.Tank.position;
            lab.SetInput(HangingPlayer,Vector3.zero,false);
            bool released=!lab.IsGripping(HangingPlayer);
            yield return Steps(100);
            Record("release_falls_and_respawns_only_that_player",released&&!lab.IsGripping(HangingPlayer)&&lab.Players[HangingPlayer].body.position.z<0&&!lab.Broken&&lab.FailureCount==failures,
                "player="+lab.Players[HangingPlayer].body.position+"; tankMoved="+Vector3.Distance(tankBefore,lab.Tank.position));
            Record("other_grips_survive_single_fall",lab.IsGripping(0)&&lab.IsGripping(2),"remaining two relations");

            r.passed=r.checks.TrueForAll(c=>c.passed);
            string path=Path.GetFullPath("Logs/m1-interaction-checks.json");
            var args=Environment.GetCommandLineArgs(); int arg=Array.IndexOf(args,"-m1-report"); if(arg>=0&&arg+1<args.Length)path=args[arg+1];
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path,JsonUtility.ToJson(r,true));
            Debug.Log("[M1] "+r.checks.FindAll(c=>c.passed).Count+"/"+r.checks.Count+"; "+path);
            test.SelectLane(false); lab.Testing=false;
            if(quit) Application.Quit(r.passed?0:1);
        }
    }
}
