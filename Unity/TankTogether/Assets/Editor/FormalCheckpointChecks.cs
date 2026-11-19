using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using TankTogether.Formal;

namespace TankTogether.Editor
{
    public static class FormalCheckpointChecks
    {
        public static string Result = "Not run";
        [MenuItem("Tank Together/Formal/Run S0 Checkpoint Checks")]
        public static void Run()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Run during Play; uses an isolated physics scene");
            var rows = new List<string>(); bool passed = true;
            void Check(string name, bool ok) { rows.Add(name + "=" + ok); passed &= ok; }
            var contacts = new Dictionary<int, int>();
            CheckpointStepRules Ready() { var r = new CheckpointStepRules(); r.Resolve(0, new Dictionary<int,int>(), false, 2); return r; }
            var rules = Ready();
            contacts[1] = 7;
            Check("three_posts_no_activation", rules.Resolve(1, contacts, false, 2).outcome == CheckpointStepRules.Outcome.None);
            contacts[1] = 15;
            Check("four_immediate", rules.Resolve(2, contacts, false, 2).outcome == CheckpointStepRules.Outcome.Checkpoint);
            Check("repeat_no_activation", rules.Resolve(3, contacts, false, 2).outcome == CheckpointStepRules.Outcome.None);
            Check("next_step_damage_preserves_cp", rules.Resolve(4, contacts, true, 2).outcome == CheckpointStepRules.Outcome.Broken && rules.CurrentCheckpoint == 1);
            Check("broken_once", rules.Resolve(5, contacts, true, 2).outcome == CheckpointStepRules.Outcome.None);
            rules = Ready();
            Check("same_step_danger_wins", rules.Resolve(1, contacts, true, 2).outcome == CheckpointStepRules.Outcome.Broken && rules.CurrentCheckpoint == 0);
            rules = Ready(); bool sequential = true;
            for (int i=0;i<4;i++) { contacts[1] = 1<<i; sequential &= rules.Resolve(i+1,contacts,false,2).outcome == CheckpointStepRules.Outcome.None; }
            Check("sequential_not_accumulated", sequential);
            rules = new CheckpointStepRules(); contacts[1] = 15;
            Check("restored_contacts_suppressed", rules.Resolve(0, contacts, false, 2).outcome == CheckpointStepRules.Outcome.None);
            contacts.Clear(); rules.Resolve(1,contacts,false,2); contacts[2]=15;
            Check("finish_once", rules.Resolve(2,contacts,false,2).outcome==CheckpointStepRules.Outcome.Finish && rules.Resolve(3,contacts,false,2).outcome==CheckpointStepRules.Outcome.None);
            Check("later_damage_no_revoke_finish",rules.Resolve(4,contacts,true,2).outcome==CheckpointStepRules.Outcome.None);
            rules=Ready(); Check("finish_same_step_danger",rules.Resolve(1,contacts,true,2).outcome==CheckpointStepRules.Outcome.Broken);
            bool rejected=false;try{rules.Resolve(1,contacts,false,2);}catch(ArgumentOutOfRangeException){rejected=true;}Check("duplicate_tick_rejected",rejected);

            Scene scene = SceneManager.CreateScene("FormalS0-Isolated", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            try
            {
                var physics = scene.GetPhysicsScene();
                GameObject Cube(string name, Vector3 p, Vector3 size)
                { var go=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(go,scene);go.name=name;go.transform.position=p;go.transform.localScale=size;return go; }
                var tank=Cube("S0 tank", new Vector3(0,1.23f,0),new Vector3(3,.5f,3));
                var body=tank.AddComponent<Rigidbody>();body.mass=24;body.constraints=RigidbodyConstraints.FreezeRotation;
                var probe=tank.AddComponent<CheckpointContactProbe>();
                var pillars=new Collider[4];
                for(int i=0;i<4;i++) {var g=Cube("post"+i,new Vector3(i%2==0?-1:1,.5f,i<2?-1:1),new Vector3(.4f,1,.4f));pillars[i]=g.GetComponent<Collider>();probe.RegisterPost(pillars[i],1,i);}
                var hazard=Cube("danger",new Vector3(0,-10,0),new Vector3(.4f,1,.4f));probe.RegisterDanger(hazard.GetComponent<Collider>());
                Physics.SyncTransforms();
                int Mask(){var m=probe.ReadContacts();return m.TryGetValue(1,out int value)?value:0;}
                for(int i=0;i<12;i++)physics.Simulate(.02f);
                Check("physics_four_contacts",Mask()==15);
                body.Sleep();physics.Simulate(.02f);Check("sleeping_contacts_preserved",Mask()==15);
                pillars[3].enabled=false;Check("disabled_post_removed",Mask()==7);
                pillars[3].enabled=true;body.WakeUp();Physics.SyncTransforms();physics.Simulate(.02f);Check("reenabled_requires_contact",Mask()==15);
                body.position=new Vector3(0,3,0);body.linearVelocity=Vector3.zero;Physics.SyncTransforms();physics.Simulate(.02f);Check("exit_clears_contacts",Mask()==0);
                rules=Ready();body.position=new Vector3(0,1.23f,0);body.linearVelocity=Vector3.zero;hazard.transform.position=new Vector3(0,.5f,0);Physics.SyncTransforms();physics.Simulate(.02f);
                var sample=probe.ReadContacts();Check("physics_four_and_danger_same_step",Mask()==15&&probe.Danger);
                Check("physical_danger_wins",rules.Resolve(1,sample,probe.Danger,2).outcome==CheckpointStepRules.Outcome.Broken&&rules.CurrentCheckpoint==0);
            }
            catch(Exception e){passed=false;rows.Add("exception="+e);}
            finally { SceneManager.UnloadSceneAsync(scene); }
            Result=string.Join("\n",rows)+"\npassed="+passed;
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/formal-s0-checkpoint.txt",Result);Debug.Log(Result);
        }
    }
}
