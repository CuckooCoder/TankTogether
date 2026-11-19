using System.Collections;
using System.IO;
using UnityEngine;
using TankTogether.Networking;
using TankTogether.M0;
namespace TankTogether.Editor
{
    public static class FormalRouteChecks
    {
        public static string Result="Not run";
        public static void Begin(){var session=Object.FindFirstObjectByType<SteamCarrySession>();session.StartCoroutine(Run(session));}
        static IEnumerator Run(SteamCarrySession session)
        {
            session.ReturnToLobby();yield return null;yield return null;
            bool started=session.StartLocalTest();var lab=session.lab;var route=lab.GetComponent<WhiteboxCarryLevel>();
            int finish=0;System.Action handler=()=>finish++;route.Finished+=handler;
            for(int i=0;i<4;i++)lab.SetActorActive(i,false);
            yield return new WaitForSeconds(.15f);
            int last=route.checkpoints.Length-1;var cp=route.checkpoints[last];
            lab.Tank.position=cp.parkingFeet+Vector3.up*(lab.baseline.carryBottomHeight+lab.baseline.tankSize.y/2+.002f);
            lab.Tank.rotation=Quaternion.Euler(0,cp.tankYaw,0);lab.Tank.linearVelocity=Vector3.right*.2f;lab.Tank.angularVelocity=Vector3.zero;Physics.SyncTransforms();
            yield return new WaitForSeconds(.25f);
            bool activated=route.CurrentCheckpoint==last;int first=finish;
            yield return new WaitForSeconds(.25f);
            Result="local_started="+started+"\nreal_route_four_posts="+activated+"\nfinish_once="+(first==1&&finish==1)+"\nbroken="+lab.Broken+"\nphysicsTicks="+TankTogether.Formal.AfterPhysicsStep.Tick+"\npassed="+(started&&activated&&first==1&&finish==1&&!lab.Broken)+"\nfixture=isolated placement at authored final posts; not full route traversal";
            route.Finished-=handler;route.SelectCheckpoint("start");session.ReturnToLobby();File.WriteAllText("Logs/formal-route.txt",Result);Debug.Log(Result);
        }
    }
}
