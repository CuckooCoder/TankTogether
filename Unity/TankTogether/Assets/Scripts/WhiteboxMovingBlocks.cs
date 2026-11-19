using System;
using UnityEngine;
namespace TankTogether.M0
{
    // One host-owned phase drives the authored blue/up and red/down blocks.
    [DefaultExecutionOrder(-20)]
    public sealed class WhiteboxMovingBlocks : MonoBehaviour
    {
        [Serializable] public class Block { public Rigidbody body; public Vector3 origin; public bool initiallyUp; }
        public Block[] blocks = Array.Empty<Block>();
        [Min(.01f), Tooltip("第二版文档：上下移动速度，米/秒。修改后所有联机成员需使用相同配置。")]
        public float speed = .6f;
        [Min(.01f), Tooltip("第二版文档：从初始位置到另一端的单程距离，米。")]
        public float travel = 2;
        public float Phase { get; private set; }
        public float NormalizedPhase => travel>0 ? Phase/(travel*2) : 0;
        public void ApplyNetworkPhase(float phase,float blend) => ApplyProxy(phase*travel*2,blend);
        M0CarryLab lab;
        void Awake() { lab=GetComponent<M0CarryLab>(); }
        public string Signature => Hash128.Compute(JsonUtility.ToJson(new Configuration { speed=speed, travel=travel,
            entries=Array.ConvertAll(blocks,b=>new Entry { origin=b.origin, up=b.initiallyUp }) })).ToString();
        [Serializable] class Configuration { public float speed,travel; public Entry[] entries; }
        [Serializable] class Entry { public Vector3 origin; public bool up; }
        public Vector3 Position(Block b,float phase) => b.origin + Vector3.up * (b.initiallyUp?1:-1) * Mathf.PingPong(phase,travel);
        public void ResetBlocks() { Phase=0; Place(false); }
        void FixedUpdate()
        {
            if(!lab || !lab.Simulate || lab.SessionPaused || lab.Broken || travel<=0) return;
            Phase=Mathf.Repeat(Phase+speed*Time.fixedDeltaTime,travel*2); Place(true);
        }
        void Place(bool physics)
        {
            foreach(var b in blocks) if(b.body) {
                var p=Position(b,Phase);
                if(physics) b.body.MovePosition(p);
                else { b.body.position=p; b.body.transform.position=p; }
            }
        }
        public void ApplyProxy(float phase,float blend)
        {
            if(travel<=0 || float.IsNaN(phase) || float.IsInfinity(phase))return;
            float cycle=travel*2;
            // Interpolate across the wrap, never backwards through a whole cycle.
            float delta=Mathf.Repeat(phase-Phase+travel,cycle)-travel;
            Phase=Mathf.Repeat(Phase+delta*Mathf.Clamp01(blend),cycle); Place(false);
        }
    }
}
