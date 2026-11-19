using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TankTogether.M0;
using TankTogether.Networking;
using UnityEditor;
using UnityEngine;
namespace TankTogether.Editor {
 public static class RevisionLiftCarryChecks {
  public static WhiteboxLiftChecks.Evidence Last;
  static IEnumerator Steps(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
  [MenuItem("Tank Together/Whitebox/Check Lift Carry")]
  public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();if(!EditorApplication.isPlaying||s.Mode!=SteamCarrySession.EntryMode.Home)throw new InvalidOperationException("Play idle revision lobby");s.StartCoroutine(Run(s));}
  static IEnumerator Run(SteamCarrySession s){
   Last=new WhiteboxLiftChecks.Evidence();s.StartLocalTest();var lab=s.lab;lab.Testing=true;var lift=lab.GetComponent<WhiteboxLift>();
   for(int count=2;count<=lab.LocalPlayerCount;count++){
    lab.DynamicRoster=true;lab.SessionTeamSize=count;lab.ConfigureSessionRoster(new[]{true,true,count==3,false},true);lab.SetSessionPaused(false);lift.ResetLift(false);
    var b=lift.platformCollider.bounds;var center=new Vector3(b.center.x+2,b.max.y,b.center.z+2);
    lab.Tank.position=center+Vector3.up*(lab.baseline.carryBottomHeight+lab.tankSize.y/2+.02f);lab.Tank.rotation=Quaternion.identity;lab.Tank.linearVelocity=Vector3.zero;lab.Tank.angularVelocity=Vector3.zero;
    for(int i=0;i<count;i++){
     float x=count==2||i==2?0:i==0?-.9f:.9f;
     float z=i==count-1?.55f:count==2?-.55f:-.275f;
     lab.PlacePlayer(i,center+new Vector3(x,.02f,z));
    }
    yield return Steps(10);
    var original=lab.Players[0].body.position;lab.PlacePlayer(0,WhiteboxLiftChecks.OperatorPoint(lift));
    if(!lift.TryInteract(0,out var reason))throw new InvalidOperationException(reason);
    lab.PlacePlayer(0,original);for(int i=0;i<count;i++)lab.SetInput(i,Vector3.zero,true);
    float elapsed=0,maxTilt=0;
    if(count==2){
     Last.checks.Add(new M0CarryChecks.Check{name="two_grips_board_moving_lift",passed=lift.Moving&&lab.IsGripping(0)&&lab.IsGripping(1),evidence="moving="+lift.Moving});
     bool held=true,platformContact=false;
     while(!lab.Broken&&elapsed<10){
      yield return new WaitForFixedUpdate();elapsed+=Time.fixedDeltaTime;maxTilt=Mathf.Max(maxTilt,Vector3.Angle(lab.Tank.rotation*Vector3.up,Vector3.up));
      if(!lab.Broken)held&=lab.IsGripping(0)&&lab.IsGripping(1);
      platformContact|=lab.TankContacts.Touches(lift.platformCollider);
     }
     bool expectedOutcome=lift.platformCollider.GetComponent<M0Danger>() ? lab.Broken&&lab.LastFailureWasDanger&&!lab.IsGripping(0)&&!lab.IsGripping(1) : !lab.Broken;
     Last.checks.Add(new M0CarryChecks.Check{name="two_grips_tip_on_lift",passed=maxTilt>18&&platformContact&&held&&expectedOutcome,evidence="maxTilt="+maxTilt+" platformContact="+platformContact+" heldUntilImpact="+held+" broken="+lab.Broken});
     continue;
    }
    while(!lab.Broken&&!lift.AtTop&&elapsed<48){yield return new WaitForFixedUpdate();elapsed+=Time.fixedDeltaTime;maxTilt=Mathf.Max(maxTilt,Vector3.Angle(lab.Tank.transform.up,Vector3.up));}
    yield return Steps(60);
    bool landed=Enumerable.Range(0,count).All(i=>Mathf.Abs(lab.Players[i].body.position.y-lift.platformCollider.bounds.max.y)<.15f);
    Last.checks.Add(new M0CarryChecks.Check{name="carry_full_lift_"+count,passed=!lab.Broken&&lift.AtTop&&landed,evidence="distance="+lift.Distance+" broken="+lab.Broken+" allFeetOnLift="+landed+" maxTilt="+maxTilt+" tank="+lab.Tank.position});
    original=lab.Players[0].body.position;lab.SetInput(0,Vector3.zero,false);lab.PlacePlayer(0,WhiteboxLiftChecks.OperatorPoint(lift));
    if(!lift.TryInteract(0,out reason))throw new InvalidOperationException(reason);
    lab.PlacePlayer(0,original);lab.SetInput(0,Vector3.zero,true);elapsed=0;maxTilt=0;
    while(!lab.Broken&&lift.Moving&&elapsed<48){yield return new WaitForFixedUpdate();elapsed+=Time.fixedDeltaTime;maxTilt=Mathf.Max(maxTilt,Vector3.Angle(lab.Tank.transform.up,Vector3.up));}
    yield return Steps(60);landed=Enumerable.Range(0,count).All(i=>Mathf.Abs(lab.Players[i].body.position.y-lift.platformCollider.bounds.max.y)<.15f);
    Last.checks.Add(new M0CarryChecks.Check{name="carry_full_descent_"+count,passed=!lab.Broken&&lift.Distance==0&&!lift.Moving&&landed,evidence="distance="+lift.Distance+" broken="+lab.Broken+" allFeetOnLift="+landed+" maxTilt="+maxTilt});
   }
   Last.complete=true;Last.passed=Last.checks.All(c=>c.passed);File.WriteAllText("Logs/revision3-lift-carry-checks.json",JsonUtility.ToJson(Last,true));s.ReturnToLobby();Debug.Log("[RevisionLiftCarryChecks] "+Last.passed);
  }
 }
}
