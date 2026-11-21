using System;
using UnityEngine;
using TankTogether.Formal;

namespace TankTogether.M0
{
    // Positions are authored in the scene from the revision model markers, in metres.
    public sealed class WhiteboxCarryLevel : MonoBehaviour
    {
        [Serializable] public class Checkpoint
        {
            public string id, label;
            public Vector3 playerFeet, tankFeet, parkingFeet;
            public float tankYaw;
            public bool liftAtTop;
            [Tooltip("有原模型台座时的缸底离地高度；0 表示在玩家头顶恢复。") ]
            public float tankRestHeight;
            [Tooltip("鱼缸初始就位的检查点：首次成功抓取允许激活，避免生成时自动保存。") ]
            public bool activateOnGrip;
        }
        public Transform levelRoot;
        public string levelId = "whitebox-route";
        [Tooltip("关卡几何修订号；参与联机兼容校验，避免混用修复前后的地图。")]
        public string geometryRevision = "";
        public Vector3 startFeet;
        public float startYaw = -90;
        public float killHeight = -60;
        public Checkpoint[] checkpoints = Array.Empty<Checkpoint>();
        public int CurrentCheckpoint { get; private set; }
        public string CheckpointId => checkpoints.Length == 0 ? "start" : checkpoints[CurrentCheckpoint].id;
        public string CheckpointLabel => checkpoints.Length == 0 ? "起点" : checkpoints[CurrentCheckpoint].label;
        public Func<string,bool> PersistCheckpoint;
        public string CompatibilitySignature {
            get {
                var lift=GetComponent<WhiteboxLift>();
                string data=levelId+"|"+string.Join("|",Array.ConvertAll(checkpoints,c=>JsonUtility.ToJson(c)));
                if(!string.IsNullOrEmpty(geometryRevision)) data+="|geometry="+geometryRevision;
                data+="|finish="+FinalStationIsTestFinish;
                data+="|zones="+useCheckpointZones+"|"+checkpointRadius.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+checkpointHeight.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                if(lift) data+="|"+lift.speed.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+lift.travel.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+lift.delay.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                var movers=GetComponent<WhiteboxMovingBlocks>();
                if(movers)data+="|moving-blocks="+movers.Signature;
                return Hash128.Compute(data).ToString();
            }
        }
        public Quaternion StartRotation => Quaternion.Euler(0, startYaw, 0);
        public Vector3 Forward => StartRotation * Vector3.forward;
        Transform stations;
        M0CarryLab lab;
        CheckpointContactProbe contacts;
        CheckpointStepRules checkpointRules;
        public bool FinalStationIsTestFinish = true;
        [Tooltip("新白盒：保留模型中央单柱，存档使用黄点范围，不生成四柱。")]
        public bool useCheckpointZones;
        [Min(.5f), Tooltip("鱼缸中心到黄点的水平距离（米）；完整鱼缸进入才存档。")]
        public float checkpointRadius = 2;
        [Min(1), Tooltip("黄点地面以上允许的缸底高度（米），覆盖最高身形搬运。")]
        public float checkpointHeight = 6;
        bool zoneArmed;
        internal void DisarmCheckpointZone() => zoneArmed = false;
        public event Action Finished;
        void OnEnable() => AfterPhysicsStep.Completed += ResolvePhysicsStep;
        void OnDisable() => AfterPhysicsStep.Completed -= ResolvePhysicsStep;
        float stableTime;
        int candidate = -1;
        public bool HasCheckpoint(string id) => checkpoints.Length == 0 ? id == "start" : Array.Exists(checkpoints, c => c.id == id);
        public bool IsParkingCandidate(string id) => candidate >= 0 && candidate < checkpoints.Length && checkpoints[candidate].id == id && !lab.Broken;
        public bool SelectCheckpoint(string id)
        {
            if (!HasCheckpoint(id)) return false;
            CurrentCheckpoint = checkpoints.Length == 0 ? 0 : Array.FindIndex(checkpoints,c => c.id == id);
            stableTime = 0; candidate = -1; return true;
        }
        public void PlaceAtStart(M0CarryLab carry, Transform actors)
        {
            lab = carry;
            if (useCheckpointZones && levelRoot)
            {
                var pedestal = levelRoot.Find("Group 31/Authored central pedestal safe contact");
                if (pedestal && !pedestal.GetComponent<TankSupportSurface>()) pedestal.gameObject.AddComponent<TankSupportSurface>();
            }
            GetComponent<WhiteboxMovingBlocks>()?.ResetBlocks();
            GetComponent<WhiteboxLift>()?.ResetLift(checkpoints.Length > 0 && checkpoints[CurrentCheckpoint].liftAtTop);
            if (checkpoints.Length == 0) actors.SetPositionAndRotation(startFeet, StartRotation);
            else
            {
                var cp = checkpoints[CurrentCheckpoint];
                actors.SetPositionAndRotation(cp.tankFeet, Quaternion.Euler(0,cp.tankYaw,0));
                foreach (Transform child in actors)
                    if (child.name == "Safe cotton stand" || child.name == "Stand base") { child.gameObject.SetActive(false); Destroy(child.gameObject); }
                if (stations) { stations.gameObject.SetActive(false); Destroy(stations.gameObject); }
                stations = new GameObject("Revision checkpoint safe supports").transform;
                contacts = lab.Tank.gameObject.AddComponent<CheckpointContactProbe>();
                checkpointRules = new CheckpointStepRules(CurrentCheckpoint);
                zoneArmed = false;
                for(int j=0;!useCheckpointZones && j<checkpoints.Length;j++)
                {
                    // Authored spawn positions and yellow parking targets are deliberately separate.
                    if(j == CurrentCheckpoint && (checkpoints[j].tankFeet-checkpoints[j].parkingFeet).sqrMagnitude > .0001f) BuildSupports(checkpoints[j].tankFeet,checkpoints[j].tankYaw,-1,actors);
                    BuildSupports(checkpoints[j].parkingFeet,checkpoints[j].tankYaw,j,actors);
                }

                Physics.SyncTransforms();
                for(int i=0;i<4;i++) if(lab.Players[i].active&&lab.Players[i].body) { lab.Players[i].body.transform.position = RespawnPoint(i); lab.PlacePlayer(i,RespawnPoint(i)); }
                if (useCheckpointZones && (CurrentCheckpoint == 0 || cp.tankRestHeight > 0)) {
                    // Initial fish tank follows the authored pedestal, not the legacy four-post GM height.
                    lab.Tank.position = cp.tankFeet + Vector3.up * ((cp.tankRestHeight > 0 ? cp.tankRestHeight : lab.baseline.carryBottomHeight) + lab.tankSize.y/2 + .01f);
                }
                if (useCheckpointZones && CurrentCheckpoint > 0 && cp.tankRestHeight <= 0) {
                    // Restore on the actual participants' flat heads; no hidden columns or automatic grip.
                    lab.Tank.position = cp.tankFeet + Vector3.up * (lab.standingHeight + lab.tankSize.y/2 + .02f);
                    lab.Tank.linearVelocity = lab.Tank.angularVelocity = Vector3.zero;
                }
                stableTime = 0; candidate = -1;
            }
            Physics.SyncTransforms();
            lab.SetExternalView(lab.Selected, startYaw, 24);
        }
        void BuildSupports(Vector3 feet,float yaw,int group,Transform actors)
        {
            var b=lab.baseline;
            var source=actors.Find("Safe cotton stand");
            var material=source.GetComponent<Renderer>().sharedMaterial;
            var physics=source.GetComponent<Collider>().sharedMaterial;
            var rotation=Quaternion.Euler(0,yaw,0);
            for(int i=0;i<4;i++)
            {
                var offset=new Vector3((i%2==0?-1:1)*(b.tankSize.x/2-b.standInset.x),b.carryBottomHeight/2,(i<2?-1:1)*(b.tankSize.z/2-b.standInset.y));
                var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=group<0?"Spawn cotton support":"Checkpoint "+checkpoints[group].id+" support "+i;
                go.transform.SetParent(stations,false);go.transform.SetPositionAndRotation(feet+rotation*offset,rotation);
                go.transform.localScale=new Vector3(b.standWidth,b.carryBottomHeight,b.standWidth);
                go.GetComponent<Renderer>().sharedMaterial=material;go.GetComponent<Collider>().sharedMaterial=physics;
                var post=go.AddComponent<M1SupportPost>();post.group=group;post.index=i;
                if(group >= 0) contacts.RegisterPost(go.GetComponent<Collider>(),group,i);
            }
        }
        void ResolvePhysicsStep()
        {
            if(!lab||!contacts||checkpointRules==null||!lab.Simulate||lab.SessionPaused)return;
            if (useCheckpointZones) { ResolveCheckpointZone(); return; }
            var decision=checkpointRules.Resolve(AfterPhysicsStep.Tick,contacts.ReadContacts(),lab.Broken,FinalStationIsTestFinish?checkpoints.Length-1:-1);
            if(decision.outcome!=CheckpointStepRules.Outcome.Checkpoint && decision.outcome!=CheckpointStepRules.Outcome.Finish)return;
            candidate=decision.station;
            string id=checkpoints[candidate].id;
            if(lab.DynamicRoster && (PersistCheckpoint==null || !PersistCheckpoint(id)))return;
            SelectCheckpoint(id);lab.ReportCheckpoint(CheckpointLabel);
            Debug.Log("[Checkpoint] Same authoritative physics step four contacts: "+id);
            if(decision.outcome==CheckpointStepRules.Outcome.Finish)Finished?.Invoke();
        }
        public bool TankInsideCheckpoint(int index)
        {
            if (!lab || !lab.Tank || index < 0 || index >= checkpoints.Length) return false;
            var d = lab.Tank.position - checkpoints[index].parkingFeet;
            float bottom = d.y - lab.tankSize.y/2;
            return d.x*d.x+d.z*d.z <= checkpointRadius*checkpointRadius && bottom >= .1f && bottom <= checkpointHeight;
        }
        void ResolveCheckpointZone()
        {
            if (lab.Broken) return; // AfterPhysicsStep: dangerous collision wins over arrival.
            if (!zoneArmed) {
                bool inside=false; for(int i=0;i<checkpoints.Length;i++) inside |= TankInsideCheckpoint(i);
                if (!inside) zoneArmed=true;
            }
            for(int i=CurrentCheckpoint+1;i<checkpoints.Length;i++) {
                if (!TankInsideCheckpoint(i)) continue;
                bool grabbed=false;
                if(checkpoints[i].activateOnGrip) for(int player=0;player<lab.Players.Length;player++)
                    grabbed |= lab.Players[player].active && lab.IsGripping(player);
                if(!zoneArmed && !grabbed) continue;
                candidate=i;
                string id=checkpoints[i].id;
                if(lab.DynamicRoster && (PersistCheckpoint==null || !PersistCheckpoint(id))) { candidate=-1; return; }
                SelectCheckpoint(id); lab.ReportCheckpoint(CheckpointLabel);
                Debug.Log("[Checkpoint] Tank activated checkpoint: "+id);
                if(FinalStationIsTestFinish && i==checkpoints.Length-1) { lab.ReportCompletion(); Finished?.Invoke(); }
                return;
            }
        }
        Vector3 CheckpointPlayerOffset(int index)
        {
            int count=0,order=0;
            for(int i=0;i<lab.Players.Length;i++) if(lab.Players[i].active) {if(i<index)order++;count++;}
            if(count<=1)return Vector3.up*.02f;
            if(count==2)return new Vector3(0,.02f,(order==0?-1:1)*lab.baseline.spawnHalfSpacing.y);
            return lab.CarryFormationOffset(order)+Vector3.up*.02f;
        }
        public Vector3 RespawnPoint(int index)
        {
            if(useCheckpointZones && CurrentCheckpoint>0 && lab)return checkpoints[CurrentCheckpoint].tankFeet + Quaternion.Euler(0,checkpoints[CurrentCheckpoint].tankYaw,0)*CheckpointPlayerOffset(index);
            if(checkpoints.Length==0)return startFeet+StartRotation*new Vector3(-1.05f+index*.7f,.05f,-4);
            return checkpoints[CurrentCheckpoint].playerFeet+new Vector3(index%2==0?-.7f:.7f,.03f,index<2?-.7f:.7f);
        }
        void OnDestroy(){if(stations)Destroy(stations.gameObject);}
    }
}
