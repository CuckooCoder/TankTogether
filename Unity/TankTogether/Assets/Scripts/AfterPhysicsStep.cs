using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
namespace TankTogether.Formal
{
    public static class AfterPhysicsStep
    {
        public static event Action Completed;
        public static long Tick { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() {Completed=null;Tick=0;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var loop=PlayerLoop.GetCurrentPlayerLoop(); Insert(ref loop); PlayerLoop.SetPlayerLoop(loop);
        }
        static bool Insert(ref PlayerLoopSystem system)
        {
            if(system.subSystemList==null)return false;
            var list=new List<PlayerLoopSystem>(system.subSystemList);
            list.RemoveAll(s=>s.type==typeof(AfterPhysicsStep));
            for(int i=0;i<list.Count;i++) {
                if(list[i].type==typeof(UnityEngine.PlayerLoop.FixedUpdate.PhysicsFixedUpdate)) {
                    list.Insert(i+1,new PlayerLoopSystem{type=typeof(AfterPhysicsStep),updateDelegate=Dispatch});
                    system.subSystemList=list.ToArray();return true;
                }
                var child=list[i];if(Insert(ref child)){list[i]=child;system.subSystemList=list.ToArray();return true;}
            }
            return false;
        }
        static void Dispatch(){Tick++;Completed?.Invoke();}
    }
}
