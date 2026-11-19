using System;
using System.Linq;
using TankTogether.M0;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
namespace TankTogether.Editor
{
    public static class WhiteboxV2Setup
    {
        public const string ModelPath="Assets/Scenes/FishTank关卡白盒第二版.skp";
        [MenuItem("Tank Together/Whitebox/Apply Second Version Level")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying || SceneManager.GetActiveScene().path!=FormalWhiteboxSetup.ScenePath)
                throw new InvalidOperationException("Stopped formal scene required");
            var route=UnityEngine.Object.FindFirstObjectByType<WhiteboxCarryLevel>();
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if(!source)throw new InvalidOperationException("Import second version model first");
            var names=source.GetComponentsInChildren<MeshFilter>(true).Select(x=>x.name).ToArray();
            foreach(var n in new[]{"Group 23","Group 29","Group 31","Group 33","Group 35","Group 51","Group 55","Group 57","Group 69","Group 71","Group 73","Group 75","Group 87","Group 89"})
                if(names.Count(x=>x==n)!=1)throw new InvalidOperationException("Required unique model node: "+n);
            var old=route.levelRoot;
            var root=UnityEngine.Object.Instantiate(source);
            root.name="Formal whitebox second version 20261019";
            root.transform.SetPositionAndRotation(old.position,old.rotation);root.transform.localScale=old.localScale;
            foreach(var camera in root.GetComponentsInChildren<Camera>(true))UnityEngine.Object.DestroyImmediate(camera.gameObject);
            foreach(var mf in root.GetComponentsInChildren<MeshFilter>(true)) {
                var collider=mf.GetComponent<MeshCollider>();if(!collider)collider=mf.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=mf.sharedMesh;
                if(!mf.GetComponent<M0Danger>())mf.gameObject.AddComponent<M0Danger>();
            }
            route.levelRoot=root.transform;
            // Keep the previous root until all bindings and ground markers validate.
            MeshRenderer Node(string name)=>root.GetComponentsInChildren<MeshRenderer>(true).Single(x=>x.name==name);
            foreach(var name in new[]{"Group 33","Group 35","Group 55","Group 89"})
                foreach(var c in Node(name).GetComponents<Collider>())c.enabled=false;
            Node("Group 29").gameObject.SetActive(false);
            Physics.SyncTransforms();
            Vector3 Ground(string name) {
                var marker=Node(name);var origin=marker.bounds.center;origin.y=marker.bounds.min.y-.01f;
                var hits=Physics.RaycastAll(origin,Vector3.down,10,~0,QueryTriggerInteraction.Ignore)
                    .Where(h=>h.collider.transform.IsChildOf(root.transform)).OrderBy(h=>h.distance).ToArray();
                if(hits.Length==0)throw new InvalidOperationException("No ground beneath "+name);
                return hits[0].point;
            }
            var fish=Node("Group 29").bounds;var feet=fish.center;feet.y=fish.min.y-1.7f;
            var start=Ground("Group 33");var upper=Ground("Group 35");var maze=Ground("Group 55");var exit=Ground("Group 89");
            WhiteboxCarryLevel.Checkpoint Zone(string id,string label,Vector3 pos,bool top)=>new WhiteboxCarryLevel.Checkpoint { id=id,label=label,playerFeet=pos,tankFeet=pos,parkingFeet=pos,liftAtTop=top };
            route.startFeet=start;
            route.checkpoints=new[]{
                new WhiteboxCarryLevel.Checkpoint {id="start",label="初始平台",playerFeet=start,tankFeet=feet,parkingFeet=start,tankRestHeight=1.7f},
                new WhiteboxCarryLevel.Checkpoint {id="pickup",label="鱼缸初始台座",playerFeet=feet,tankFeet=feet,parkingFeet=feet,tankRestHeight=1.7f,activateOnGrip=true},
                Zone("pickup_exit","台座旁黄点",exit,false),Zone("upper","高处中途存档点",upper,true),Zone("maze","绿色区域存档点",maze,true)};
            // This marker straddles an authored 0.5m step. Keep its trigger in place,
            // but recover the whole team together on the nearest flat footprint.
            var exitSpawn=FindFlatRespawn(root.transform,exit);
            route.checkpoints[2].tankFeet=exitSpawn;route.checkpoints[2].playerFeet=exitSpawn;
            ConfigureFinish(route);route.useCheckpointZones=true;
            var lift=route.GetComponent<WhiteboxLift>();var deck=Node("Group 23");
            Rigidbody Body(MeshRenderer renderer){var b=renderer.GetComponent<Rigidbody>();if(!b)b=renderer.gameObject.AddComponent<Rigidbody>();b.isKinematic=true;b.useGravity=false;b.interpolation=RigidbodyInterpolation.Interpolate;return b;}
            lift.platform=Body(deck);lift.platformCollider=deck.GetComponent<Collider>();lift.bottomPosition=deck.transform.position;
            var control=Node("Group 51");control.transform.SetParent(deck.transform,true);lift.switchCollider=control.GetComponent<Collider>();
            lift.speed=2.5f;lift.travel=101;lift.ResetLift(false);
            var movers=route.GetComponent<WhiteboxMovingBlocks>();if(!movers)movers=route.gameObject.AddComponent<WhiteboxMovingBlocks>();movers.speed=.6f;movers.travel=2;
            movers.blocks=new[]{"Group 57","Group 69","Group 71","Group 73","Group 75"}.Select((name,i)=>{
                var renderer=Node(name);return new WhiteboxMovingBlocks.Block{body=Body(renderer),origin=renderer.transform.position,initiallyUp=i<3};}).ToArray();movers.ResetBlocks();
            FormalLevelRevisionSetup.SplitPedestal(route,feet);
            UnityEngine.Object.DestroyImmediate(old.gameObject);
            Physics.SyncTransforms();EditorUtility.SetDirty(route);EditorUtility.SetDirty(lift);EditorUtility.SetDirty(movers);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("Second version installed: "+names.Length+" meshes; "+route.checkpoints.Length+" checkpoints; five moving blocks; switch Group 51; temporary finish on Group 87");
        }
        [MenuItem("Tank Together/Whitebox/Apply Second Version Temporary Finish")]
        public static void ApplyFinish()
        {
            if(EditorApplication.isPlaying || SceneManager.GetActiveScene().path!=FormalWhiteboxSetup.ScenePath)
                throw new InvalidOperationException("Stopped formal scene required");
            var route=UnityEngine.Object.FindFirstObjectByType<WhiteboxCarryLevel>();
            ConfigureFinish(route);
            EditorUtility.SetDirty(route);EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }
        static void ConfigureFinish(WhiteboxCarryLevel route)
        {
            var platform=route.levelRoot.GetComponentsInChildren<MeshRenderer>(true).Single(x=>x.name=="Group 87");
            var collider=platform.GetComponent<MeshCollider>();var bounds=platform.bounds;
            Physics.SyncTransforms();
            if(!collider.Raycast(new Ray(bounds.center+Vector3.up*(bounds.extents.y+1),Vector3.down),out var hit,bounds.size.y+2)||hit.normal.y<.95f)
                throw new InvalidOperationException("Finish must be on the actual white platform surface");
            // Verify a 4.2 x 3.2 metre tank footprint around the authored platform center.
            foreach(float x in new[]{-2.1f,0,2.1f})foreach(float z in new[]{-1.6f,0,1.6f})
                if(!collider.Raycast(new Ray(hit.point+new Vector3(x,1,z),Vector3.down),out var corner,2)||Mathf.Abs(corner.point.y-hit.point.y)>.03f)
                    throw new InvalidOperationException("Finish footprint is not safely inside white platform");
            var finish=new WhiteboxCarryLevel.Checkpoint {id="finish",label="末端白色平台（测试终点）",playerFeet=hit.point,tankFeet=hit.point,parkingFeet=hit.point,liftAtTop=true};
            route.checkpoints=route.checkpoints.Where(c=>c.id!="finish").Concat(new[]{finish}).ToArray();
            route.geometryRevision="formal-v2-20261019-finish-r2";
            route.FinalStationIsTestFinish=true;
        }
        static Vector3 FindFlatRespawn(Transform root,Vector3 marker)
        {
            float best=float.PositiveInfinity;Vector3 result=marker;
            for(int x=-8;x<=8;x++)for(int z=-8;z<=8;z++) {
                var offset=new Vector3(x*.25f,0,z*.25f);float distance=offset.sqrMagnitude;
                if(distance>4 || distance>=best)continue;
                float low=float.PositiveInfinity,high=float.NegativeInfinity;bool valid=true;
                foreach(float dx in new[]{-1.25f,0,1.25f})foreach(float dz in new[]{-.9f,0,.9f}) {
                    var hits=Physics.RaycastAll(marker+offset+new Vector3(dx,3,dz),Vector3.down,6,~0,QueryTriggerInteraction.Ignore)
                        .Where(h=>h.collider.transform.IsChildOf(root)).OrderBy(h=>h.distance).ToArray();
                    if(hits.Length==0){valid=false;continue;}
                    low=Mathf.Min(low,hits[0].point.y);high=Mathf.Max(high,hits[0].point.y);
                    if(hits[0].normal.y<.95f)valid=false;
                }
                if(valid&&high-low<.03f){best=distance;result=marker+offset;result.y=high;}
            }
            if(float.IsInfinity(best))throw new InvalidOperationException("No flat respawn footprint within checkpoint zone");
            return result;
        }
    }
}
