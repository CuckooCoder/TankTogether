using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using TankTogether.M0;

namespace TankTogether.Editor
{
    public static class WhiteboxLongGapDemo
    {
        [Serializable] public class Result
        {
            public string utc,baseline,message;
            public bool complete,success,firstJump,secondJump,broken;
            public int firstGrounded,secondGrounded,finalGrounded;
            public float maxTilt,firstTakeoffX,secondTakeoffX,speed;
            public Vector3 end;
        }
        static bool Grounded(M0CarryLab.Actor a)=>Physics.Raycast(a.body.position+Vector3.up*.08f,Vector3.down,.16f,~0,QueryTriggerInteraction.Ignore);
        static IEnumerator Steps(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
        static IEnumerator Shot(string path)
        {
            yield return new WaitForEndOfFrame();
            var image=ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.Destroy(image);
        }
        [MenuItem("Tank Together/Whitebox/Run Opening Jump Route")]
        public static void Start()
        {
            var lab=UnityEngine.Object.FindFirstObjectByType<M0CarryLab>();
            if(!EditorApplication.isPlaying||!lab||!lab.GetComponent<WhiteboxCarryLevel>()||lab.Testing)throw new InvalidOperationException("Play idle whitebox");
            lab.StartCoroutine(Run(lab));
        }
        static IEnumerator Run(M0CarryLab lab)
        {
            var r=new Result {utc=DateTime.UtcNow.ToString("O"),baseline=lab.baseline.Signature};
            float oldScale=Time.timeScale;Time.timeScale=1;lab.Testing=true;
            try
            {
                lab.ResetLab();yield return Steps(60);
                for(int i=0;i<4;i++)lab.SetInput(i,Vector3.zero,true);
                yield return Steps(20);
                // Walk sideways onto the intended lane, then take the complete bridge approach.
                for(int n=0;n<250;n++)
                {
                    float dz=-2-lab.Tank.position.z;
                    for(int i=0;i<4;i++)lab.SetInput(i,Vector3.forward*Mathf.Clamp(dz*.7f,-1,1),true);
                    yield return Steps(1);
                    if(Mathf.Abs(dz)<.05f&&Mathf.Abs(lab.Tank.linearVelocity.z)<.1f)break;
                }
                for(int i=0;i<4;i++)lab.SetInput(i,Vector3.zero,true);
                yield return Steps(30);
                int secondStep=-1;bool landed=false,apexShot=false;
                for(int step=0;step<1600;step++)
                {
                    float front=float.MaxValue;
                    for(int i=0;i<4;i++)front=Mathf.Min(front,lab.Players[i].body.position.x);
                    bool jump=false;
                    if(!r.firstJump&&front<=-26.55f)
                    {
                        jump=true;r.firstJump=true;r.firstTakeoffX=front;
                        for(int i=0;i<4;i++)if(Grounded(lab.Players[i]))r.firstGrounded++;
                    }
                    if(!r.secondJump&&front<=-37.65f)
                    {
                        jump=true;r.secondJump=true;r.secondTakeoffX=front;secondStep=step;
                        for(int i=0;i<4;i++)if(Grounded(lab.Players[i]))r.secondGrounded++;
                    }
                    for(int i=0;i<4;i++)lab.SetInput(i,Vector3.left,true,jump,false,true);
                    yield return Steps(1);
                    r.maxTilt=Mathf.Max(r.maxTilt,Vector3.Angle(lab.Tank.transform.up,Vector3.up));
                    if(r.secondJump&&!apexShot&&step-secondStep>=20)
                    {apexShot=true;yield return Shot("Logs/longgap-demo-air.png");}
                    bool all=r.secondJump;
                    for(int i=0;i<4;i++)all &= Grounded(lab.Players[i])&&lab.Players[i].body.position.x< -40.35f;
                    if(all){landed=true;break;}
                    if(lab.Broken)break;
                    bool fell=false;for(int i=0;i<4;i++)fell |= lab.Players[i].body.position.y< -7;
                    if(fell)break;
                }
                for(int i=0;i<4;i++)lab.SetInput(i,Vector3.zero,true);
                yield return Steps(100);
                for(int i=0;i<4;i++)if(Grounded(lab.Players[i]))r.finalGrounded++;
                r.speed=lab.Tank.linearVelocity.magnitude;r.end=lab.Tank.position;r.broken=lab.Broken;
                r.success=landed&&!lab.Broken&&r.finalGrounded==4&&r.speed<.15f;
                r.message=lab.Message;r.complete=true;
                File.WriteAllText("Logs/whitebox-longgap-demo.json",JsonUtility.ToJson(r,true));
                yield return Shot("Logs/longgap-demo-land.png");
            }
            finally{lab.ResetLab();lab.Testing=false;Time.timeScale=oldScale;}
        }
    }
}
