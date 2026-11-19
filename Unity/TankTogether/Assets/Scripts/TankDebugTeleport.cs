using System;
using UnityEngine;
namespace TankTogether.M0
{
    // Local test placement only. Does not move the tank or advance checkpoints.
    public static class TankDebugTeleport
    {
        public static bool FindFeet(M0CarryLab lab, int index, float height, out Vector3 feet)
        {
            feet=default;var actor=lab.Players[index];
            var yaw=Quaternion.Euler(0,lab.Tank.rotation.eulerAngles.y,0);
            float halfWidth=Mathf.Abs(lab.CarryFormationOffset(0).x);
            float halfDepth=Mathf.Abs(lab.CarryFormationOffset(2).z);
            float step=actor.capsule.radius;
            Vector3? safeEdge=null;
            bool TryEdgeColumn(Vector3 local,out Vector3 point)
            {
                if(!TryColumn(lab,actor,lab.Tank.position+yaw*local,height,out point))return false;
                if(!safeEdge.HasValue)safeEdge=point;
                return FlatHeadSupport.Bottom(lab.Tank,lab.tankSize,point+Vector3.up*height,out _,out float gap)&&
                    Mathf.Abs(gap)<=lab.baseline.gripVerticalReach;
            }
            // Try the assigned side, then the spare front edge, before borrowing another carrier's side.
            foreach(int edge in new[]{index,3,(index+1)%3,(index+2)%3})
            {
                Vector3 slot=edge==3?Vector3.forward*halfDepth:lab.CarryFormationOffset(edge);
                bool side=edge<2;
                float extent=side?halfDepth:halfWidth;
                float along=side?slot.z:slot.x;
                if(TryEdgeColumn(slot,out feet))return true;
                for(float distance=step;distance<=extent+Mathf.Abs(along)+step;distance+=step)
                {
                    for(int sign=-1;sign<=1;sign+=2)
                    {
                        // Sample the end of the edge once, even when it is between grid positions.
                        float limit=extent-sign*along;
                        if(distance-step>=limit)continue;
                        float delta=sign*Mathf.Min(distance,limit);
                        var offset=slot+(side?Vector3.forward:Vector3.right)*delta;
                        if(TryEdgeColumn(offset,out feet))return true;
                    }
                }
            }
            if(safeEdge.HasValue){feet=safeEdge.Value;return true;}
            for(int ring=1;ring<=8;ring++)for(int n=0;n<16;n++)
            {
                float angle=(n+index*4)*Mathf.PI/8;
                Vector3 offset=yaw*new Vector3(Mathf.Cos(angle)*ring*.5f,0,Mathf.Sin(angle)*ring*.5f);
                if(TryColumn(lab,actor,lab.Tank.position+offset,height,out feet))return true;
            }
            return false;
        }
        static bool TryColumn(M0CarryLab lab,M0CarryLab.Actor actor,Vector3 column,float height,out Vector3 feet)
        {
            feet=default;
            var hits=Physics.RaycastAll(column+Vector3.up*.2f,Vector3.down,8,~0,QueryTriggerInteraction.Ignore);
            Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            float radius=actor.capsule.radius+.015f;
            foreach(var hit in hits)
            {
                if(!lab.IsEnvironment(hit.collider)||hit.normal.y<.7f||hit.collider.GetComponent<M1SupportPost>()||
                    hit.collider.name=="Safe cotton stand"||hit.collider.name=="Authored central pedestal safe contact")continue;
                var candidate=hit.point+Vector3.up*.025f;bool blocked=false;
                foreach(var c in Physics.OverlapCapsule(candidate+Vector3.up*radius,candidate+Vector3.up*(height-radius),radius,~0,QueryTriggerInteraction.Ignore))
                {
                    if(c.attachedRigidbody==actor.body)continue;
                    // The legitimate head contact allowance is small; never place the body inside the tank.
                    if(c==lab.GlassCollider && FlatHeadSupport.Bottom(lab.Tank,lab.tankSize,candidate+Vector3.up*height,out _,out float compression) && compression<=.04f)continue;
                    blocked=true;break;
                }
                if(!blocked){feet=candidate;return true;}
            }
            return false;
        }
    }
}
