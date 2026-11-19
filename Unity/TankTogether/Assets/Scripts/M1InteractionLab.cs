using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TankTogether.M0
{
    // M1 experiments only. Candidate parking is observable, never a saved checkpoint.
    public sealed class M1InteractionLab : MonoBehaviour
    {
        public bool hangingLane;
        public Vector3 parkingCenter = new Vector3(0, 0, 5.5f);
        public float candidateLinearSpeed = 0.10f, candidateAngularSpeed = 0.10f, contactSettleSeconds = 0.20f;
        public M0CarryLab Lab { get; private set; }
        public M1SupportContacts Contacts { get; private set; }
        public bool ParkingCandidate { get; private set; }
        public int CandidateCount { get; private set; }
        public readonly List<Collider> Floors = new List<Collider>();
        public readonly List<M1SupportPost> Posts = new List<M1SupportPost>();
        float stableTime;
        Font font;
        [System.NonSerialized] GUIStyle style;
        System.Collections.IEnumerator Start()
        {
            yield return null;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-m1-autotest") >= 0)
                StartCoroutine(M1InteractionChecks.Run(this,true));
        }

        public void BuildWorld(Transform world)
        {
            Lab = GetComponent<M0CarryLab>();
            stableTime = 0; ParkingCandidate = false; CandidateCount = 0;
            Floors.Clear(); Posts.Clear();
            var ground = world.Find("Danger floor").gameObject;
            var floorMaterial = ground.GetComponent<Renderer>().sharedMaterial;
            var physicsMaterial = ground.GetComponent<Collider>().sharedMaterial;
            world.Find("Danger wall").gameObject.SetActive(false);
            world.Find("Corner obstacle").gameObject.SetActive(false);
            foreach (Transform child in world)
                if (child.name == "Safe cotton stand")
                {
                    var p = child.gameObject.AddComponent<M1SupportPost>(); p.group = 0; p.index = Posts.Count;
                    Posts.Add(p);
                }
            var postMaterial = Posts[0].GetComponent<Renderer>().sharedMaterial;
            for (int i = 0; i < 4; i++)
            {
                var b = Lab.baseline;
                var pos = parkingCenter + new Vector3((i % 2 == 0 ? -1 : 1) * (b.tankSize.x / 2 - b.standInset.x), b.carryBottomHeight / 2, (i < 2 ? -1 : 1) * (b.tankSize.z / 2 - b.standInset.y));
                var go = Cube(world, "Candidate parking post " + (i + 1), pos, new Vector3(b.standWidth, b.carryBottomHeight, b.standWidth), postMaterial, physicsMaterial);
                var p = go.AddComponent<M1SupportPost>(); p.group = 1; p.index = i; Posts.Add(p);
            }
            if (hangingLane)
            {
                ground.SetActive(false);
                foreach (Transform child in world) if (child.name == "Grid") child.gameObject.SetActive(false);
                // Actual hole: x=[0.1,1.7], z=[2.5,4.5].
                Floor(-12, 0.1f, -12, 12); Floor(1.7f, 12, -12, 12);
                Floor(0.1f, 1.7f, -12, 2.5f); Floor(0.1f, 1.7f, 4.5f, 12);
                var bottom = Cube(world, "Pit death reference", new Vector3(0.9f, -4.65f, 3.5f), new Vector3(1.6f, 0.3f, 2), floorMaterial, physicsMaterial);
                bottom.AddComponent<M0Danger>();
                for (int i = 4; i < Posts.Count; i++) Posts[i].gameObject.SetActive(false);
                // Experiment fixture: P3 starts 0.30 m farther forward at a legal free grip point.
                // It does not move actors during a fall, lock positions, or change the M0 baseline.
                Lab.PlacePlayer(2, Lab.Players[2].body.position + Vector3.forward * 0.30f);
            }
            else Floors.Add(ground.GetComponent<Collider>());
            Contacts = Lab.Tank.gameObject.AddComponent<M1SupportContacts>();

            void Floor(float x0, float x1, float z0, float z1)
            {
                var go = Cube(world, "Pit platform", new Vector3((x0+x1)/2, -0.15f, (z0+z1)/2), new Vector3(x1-x0, 0.3f, z1-z0), floorMaterial, physicsMaterial);
                go.AddComponent<M0Danger>(); Floors.Add(go.GetComponent<Collider>());
            }
        }
        static GameObject Cube(Transform parent, string name, Vector3 pos, Vector3 scale, Material material, PhysicsMaterial physicsMaterial)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent, false); go.transform.position = pos; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material; go.GetComponent<Collider>().sharedMaterial = physicsMaterial;
            return go;
        }
        public void SelectLane(bool hanging) { hangingLane = hanging; GetComponent<M0CarryLab>().ResetLab(); }
        public bool Grounded(int i) => Physics.Raycast(Lab.Players[i].body.position + Vector3.up * 0.08f, Vector3.down, 0.16f, ~0, QueryTriggerInteraction.Ignore);
        void Update()
        {
            if (!Lab || Lab.Testing || Lab.ExternalControl || Keyboard.current == null) return;
            if (Keyboard.current.f5Key.wasPressedThisFrame) SelectLane(false);
            if (Keyboard.current.f6Key.wasPressedThisFrame) SelectLane(true);
        }
        void FixedUpdate()
        {
            if (!Lab || !Contacts) return;
            bool bearing = !Lab.Broken && Contacts.Mask(1) == 15 && Lab.Tank.linearVelocity.magnitude <= candidateLinearSpeed && Lab.Tank.angularVelocity.magnitude <= candidateAngularSpeed;
            stableTime = bearing ? stableTime + Time.fixedDeltaTime : 0;
            bool candidate = stableTime >= contactSettleSeconds;
            if (candidate && !ParkingCandidate) { CandidateCount++; Debug.Log("[M1] Candidate parking: four real top contacts; not a checkpoint activation"); }
            ParkingCandidate = candidate;
        }
        void OnGUI()
        {
            if (!Lab || !Contacts) return;
            if (style == null || !style.font || style.fontSize != 16)
            {
                if(!font) font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 16);
                style = new GUIStyle(GUI.skin.label) { font = font, fontSize = 16, wordWrap = true, normal = { textColor = Color.white } };
            }
            var oldMatrix = GUI.matrix; var oldColor = GUI.color;
            float scale = Mathf.Min(Screen.width/1280f, Screen.height/720f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one*scale);
            GUI.color = new Color(0.03f,0.06f,0.08f,0.95f); GUI.DrawTexture(new Rect(24,125,400,120),Texture2D.whiteTexture); GUI.color = oldColor;
            string text = hangingLane ? "M1 · 悬挂 / 救援实验\n前方右侧有坑洞。踩空的玩家保持抓取，\n队友后退尝试救回；松手会坠落。" : "M1 · 停车候选实验（不保存检查点）\n目标四点接触：" + System.Convert.ToString(Contacts.Mask(1),2).PadLeft(4,'0') + "   " + (ParkingCandidate ? "已形成候选承托" : "尚未停稳承托") + "\n只用现有移动、跳蹲和抓取/松手。";
            GUI.Label(new Rect(38,134,375,82), text, style);
            GUI.Label(new Rect(38,214,375,27), "F5 停车场   F6 悬挂场   R 重置当前场", style);
            GUI.matrix = oldMatrix;
        }
        void OnDestroy() { if (font) Destroy(font); }
    }
    public sealed class M1SupportPost : MonoBehaviour { public int group, index; }
    public sealed class M1SupportContacts : MonoBehaviour
    {
        readonly Dictionary<M1SupportPost,bool> topContacts = new Dictionary<M1SupportPost,bool>();
        public int Mask(int group)
        {
            int mask=0;
            foreach (var pair in topContacts) if (pair.Key && pair.Key.gameObject.activeInHierarchy && pair.Key.group==group && pair.Value) mask |= 1<<pair.Key.index;
            return mask;
        }
        void Track(Collision collision)
        {
            var post=collision.collider.GetComponent<M1SupportPost>(); if (!post) return;
            bool top=false;
            for(int i=0;i<collision.contactCount;i++) if(collision.GetContact(i).normal.y>0.5f) top=true;
            topContacts[post]=top;
        }
        void OnCollisionEnter(Collision collision) => Track(collision);
        void OnCollisionStay(Collision collision) => Track(collision);
        void OnCollisionExit(Collision collision)
        {
            var post=collision.collider.GetComponent<M1SupportPost>(); if(post) topContacts.Remove(post);
        }
    }
}
