using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
namespace TankTogether.Editor
{
    // Authoring tool: generated clips are ordinary editable Unity assets. Never runs in builds.
    public static class CharacterAnimationAuthoring
    {
        const string Folder="Assets/Resources/CharacterAnimation";
        static readonly string[] Names={"Idle","Walk","Run","Takeoff","Rise","Apex","Fall","LandSoft","LandHard"};
        [MenuItem("Tank Together/Animation/Rebuild Character Clips")]
        public static void Build()=>BuildClips(false);
        [MenuItem("Tank Together/Animation/Rebuild Locomotion Clips")]
        public static void BuildLocomotion()=>BuildClips(true);
        static void BuildClips(bool locomotionOnly)
        {
            if(Application.isPlaying)throw new InvalidOperationException("Stop Play before rebuilding animation assets.");
            if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Resources","CharacterAnimation");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Juese001/Juese001_URPPreview.prefab");
            if(!prefab)throw new InvalidOperationException("Missing character prefab");
            var root=UnityEngine.Object.Instantiate(prefab);root.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                var bones=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.bones).Where(b=>b).Distinct().ToArray();
                var rotations=bones.ToDictionary(b=>b,b=>b.localRotation);
                var positions=bones.ToDictionary(b=>b,b=>b.localPosition);
                var map=bones.ToDictionary(b=>b.name,b=>b);
                var clips=new Dictionary<string,AnimationClip>();
                foreach(var name in Names.Where(n=>!locomotionOnly||n=="Walk"||n=="Run"))
                {
                    var path=Folder+"/"+name+".anim";
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if(!clip){clip=new AnimationClip();AssetDatabase.CreateAsset(clip,path);}
                    clip.ClearCurves();clip.name=name;clip.frameRate=60;
                    float duration=name=="Idle"?2:name=="Walk"?.62f:name=="Run"?.44f:name=="Takeoff"?.10f:name.StartsWith("Land")?.26f:.4f;
                    bool loop=name=="Idle"||name=="Walk"||name=="Run"||name=="Rise"||name=="Apex"||name=="Fall";
                    int samples=Mathf.CeilToInt(duration*60);
                    var curves=bones.ToDictionary(b=>b,b=>new[]{new AnimationCurve(),new AnimationCurve(),new AnimationCurve(),new AnimationCurve()});
                    var pelvisCurves=new[]{new AnimationCurve(),new AnimationCurve(),new AnimationCurve()};
                    foreach(int i in Enumerable.Range(0,samples+1))
                    {
                        float t=(float)i/samples;
                        foreach(var b in bones){b.localRotation=rotations[b];b.localPosition=positions[b];}
                        Pose(root.transform,map,name,t);
                        foreach(var b in bones)
                        {
                            var q=b.localRotation;var c=curves[b];
                            if(i>0){var old=new Quaternion(c[0].keys[i-1].value,c[1].keys[i-1].value,c[2].keys[i-1].value,c[3].keys[i-1].value);if(Quaternion.Dot(old,q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);}
                            c[0].AddKey(t*duration,q.x);c[1].AddKey(t*duration,q.y);c[2].AddKey(t*duration,q.z);c[3].AddKey(t*duration,q.w);
                        }
                        var pos=map["Pelvis"].localPosition;pelvisCurves[0].AddKey(t*duration,pos.x);pelvisCurves[1].AddKey(t*duration,pos.y);pelvisCurves[2].AddKey(t*duration,pos.z);
                    }
                    foreach(var group in curves.Values)foreach(var curve in group)SmoothCurve(curve,loop);
                    foreach(var curve in pelvisCurves)SmoothCurve(curve,loop);
                    foreach(var b in bones)for(int j=0;j<4;j++)clip.SetCurve(AnimationUtility.CalculateTransformPath(b,root.transform),typeof(Transform),"localRotation."+"xyzw"[j],curves[b][j]);
                    for(int j=0;j<3;j++)clip.SetCurve(AnimationUtility.CalculateTransformPath(map["Pelvis"],root.transform),typeof(Transform),"localPosition."+"xyz"[j],pelvisCurves[j]);
                    var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;settings.loopBlend=loop;AnimationUtility.SetAnimationClipSettings(clip,settings);
                    clip.EnsureQuaternionContinuity();EditorUtility.SetDirty(clip);clips[name]=clip;
                }
                if(locomotionOnly){AssetDatabase.SaveAssets();Debug.Log("[CharacterAnimation] Walk/Run updated; controller and jump clips preserved.");return;}
                var controllerPath=Folder+"/CarryCharacter.controller";
                var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if(!controller)controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                // Rebuild our owned state graph while preserving the controller asset/GUID.
                var sm=controller.layers[0].stateMachine;
                foreach(var tr in sm.anyStateTransitions)sm.RemoveAnyStateTransition(tr);
                foreach(var st in sm.states)sm.RemoveState(st.state);
                controller.parameters=new AnimatorControllerParameter[0];
                controller.AddParameter("MotionState",AnimatorControllerParameterType.Int);
                controller.AddParameter("Speed",AnimatorControllerParameterType.Float);
                controller.AddParameter("Impact",AnimatorControllerParameterType.Float);
                controller.AddParameter("StrideRate",AnimatorControllerParameterType.Float);
                string[] states={"Locomotion","Takeoff","Rise","Apex","Fall","Land"};
                for(int i=0;i<states.Length;i++)
                {
                    var state=sm.AddState(states[i],new Vector3(250+(i%3)*240,100+(i/3)*150));state.writeDefaultValues=false;
                    if(i==0||i==5)
                    {
                        var tree=new BlendTree {name=i==0?"Speed Idle Walk Run":"Landing Impact",blendType=BlendTreeType.Simple1D,blendParameter=i==0?"Speed":"Impact",useAutomaticThresholds=false};
                        AssetDatabase.AddObjectToAsset(tree,controller);
                        if(i==0){tree.AddChild(clips["Idle"],0);tree.AddChild(clips["Walk"],4);tree.AddChild(clips["Run"],6);}
                        else{tree.AddChild(clips["LandSoft"],0);tree.AddChild(clips["LandHard"],1);}
                        state.motion=tree;
                    }
                    else state.motion=clips[states[i]];
                    if(i==0){sm.defaultState=state;state.speedParameter="StrideRate";state.speedParameterActive=true;}
                    var transition=sm.AddAnyStateTransition(state);transition.hasExitTime=false;transition.hasFixedDuration=true;transition.duration=i==1?.035f:.065f;transition.canTransitionToSelf=false;
                    transition.interruptionSource=TransitionInterruptionSource.None;transition.orderedInterruption=true;
                    transition.AddCondition(AnimatorConditionMode.Equals,i,"MotionState");
                }
                EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
                Debug.Log("[CharacterAnimation] 9 editable clips and 6-state Animator ready.");
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
        static void SmoothCurve(AnimationCurve curve,bool loop)
        {
            for(int i=0;i<curve.length;i++)curve.SmoothTangents(i,0);
            if(!loop||curve.length<3)return;
            int end=curve.length-1;
            float slope=(curve[1].value-curve[end-1].value)/((curve[1].time-curve[0].time)+(curve[end].time-curve[end-1].time));
            var first=curve[0];first.inTangent=first.outTangent=slope;curve.MoveKey(0,first);
            var last=curve[end];last.inTangent=last.outTangent=slope;curve.MoveKey(end,last);
        }
        static float Pulse(float t,float delay,float peak)
        { if(t<delay)return 0;return t<peak?Mathf.SmoothStep(0,1,(t-delay)/(peak-delay)):1-Mathf.SmoothStep(0,1,(t-peak)/(1-peak)); }
        static void Pose(Transform root,Dictionary<string,Transform> b,string name,float t)
        {
            Action<string,float> pitch=(n,a)=>{b[n].rotation=Quaternion.AngleAxis(a,root.right)*b[n].rotation;};
            Action<string,float> roll=(n,a)=>{b[n].rotation=Quaternion.AngleAxis(a,root.forward)*b[n].rotation;};
            float bob=0,thighL=0,thighR=0,kneeL=0,kneeR=0,armL=0,armR=0,chest=0,spread=10;
            if(name=="Idle") {chest=Mathf.Sin(t*Mathf.PI*2)*.6f;}
            else if(name=="Walk"||name=="Run")
            {
                float phase=t*Mathf.PI*2, stride=name=="Run"?44:23;
                thighL=Mathf.Sin(phase)*stride;thighR=-thighL;
                kneeL=Mathf.Max(0,-Mathf.Sin(phase))*(name=="Run"?65:32);
                kneeR=Mathf.Max(0,Mathf.Sin(phase))*(name=="Run"?65:32);
                bool running=name=="Run";
                armL=running?32-Mathf.Sin(phase-.45f)*29:-Mathf.Sin(phase-.4f)*27;
                armR=running?32+Mathf.Sin(phase-.45f)*29:Mathf.Sin(phase-.4f)*27;
                spread=running?23:16;
                bob=(1-Mathf.Cos(phase*2))*(running?.026f:.014f);chest=(running?-15:3)+Mathf.Sin(phase*2-.4f)*(running?2.5f:1.2f);
                roll("Pelvis",Mathf.Sin(phase)*(running?3.2f:3.8f));roll("Chest",-Mathf.Sin(phase-.35f)*2);
                pitch("Forearm.L",-10+Mathf.Sin(phase-.95f)*(running?17:11));pitch("Forearm.R",-10-Mathf.Sin(phase-.95f)*(running?17:11));
                pitch("Hand.L",Mathf.Sin(phase-1.25f)*8);pitch("Hand.R",-Mathf.Sin(phase-1.25f)*8);
            }
            else if(name=="Takeoff")
            {
                float u=Mathf.SmoothStep(0,1,t);thighL=thighR=Mathf.Lerp(4,-22,u);kneeL=kneeR=Mathf.Lerp(0,42,u);
                armL=armR=Mathf.Lerp(8,-48,u);chest=Mathf.Lerp(-3,5,u);spread=18;
            }
            else if(name=="Rise") {thighL=-26;thighR=-19;kneeL=48;kneeR=38;armL=-48;armR=-40;chest=5;spread=18;}
            else if(name=="Apex") {thighL=-14;thighR=-9;kneeL=25;kneeR=19;armL=-28;armR=-22;chest=1;spread=26;}
            else if(name=="Fall") {thighL=3;thighR=-3;kneeL=9;kneeR=12;armL=-10;armR=-14;chest=-4;spread=32;}
            else
            {
                float compression=t<.24f?Mathf.SmoothStep(0,1,t/.24f):1-Mathf.SmoothStep(0,1,(t-.24f)/.76f);
                float strength=name=="LandHard"?1:.6f;
                thighL=thighR=-32*compression*strength;kneeL=kneeR=58*compression*strength;
                float torso=Pulse(t,.08f,.34f);
                float arms=Pulse(t,.16f,.47f);
                chest=14*torso*strength;armL=armR=18*arms*strength;
                bob=-.055f*compression*strength;spread=18;
            }
            if(name=="Rise"||name=="Apex"||name=="Fall")
            {
                float balance=Mathf.Sin(t*Mathf.PI*2);
                armL+=balance*2;armR-=balance*2;
                kneeL+=balance*2;kneeR-=balance*2;
                chest+=balance*.5f;
            }
            b["Pelvis"].position+=Vector3.up*bob;
            pitch("Chest",chest);pitch("Head",-chest*.35f);
            pitch("Thigh.L",thighL);pitch("Thigh.R",thighR);pitch("Shin.L",kneeL);pitch("Shin.R",kneeR);
            pitch("Foot.L",-thighL-kneeL);pitch("Foot.R",-thighR-kneeR);
            pitch("UpperArm.L",armL);pitch("UpperArm.R",armR);roll("UpperArm.L",-spread);roll("UpperArm.R",spread);
            pitch("Forearm.L",-Mathf.Abs(armL)*.25f);pitch("Forearm.R",-Mathf.Abs(armR)*.25f);
        }
    }
}
