using System;
using System.Collections.Generic;
namespace TankTogether.Networking
{
    [Serializable] public sealed class AdventureRun
    {
        public string id;
        public static AdventureRun Create()=>new AdventureRun{id=Guid.NewGuid().ToString("N")};
        public double seconds;
        public int breaks, checkpoints, resets, techResets;
        public bool finished;
        public List<AdventureMember> members = new List<AdventureMember>();
    }
    [Serializable] public sealed class AdventureMember
    {
        public string steamId, name;
        public int jumps, grabs, falls;
    }
    [Serializable] public sealed class AdventureReport
    {
        public string runId, completedUtc;
        public double seconds;
        public int breaks, checkpoints;
        public List<AdventureMember> members = new List<AdventureMember>();
        public List<string> finalists = new List<string>(), mvp = new List<string>(), blame = new List<string>();
    }
    public sealed class FinishVote
    {
        readonly int[] mvp = {-1,-1,-1,-1}, blame = {-1,-1,-1,-1};
        public byte Candidates { get; private set; }
        public byte Submitted { get; private set; }
        public float Deadline { get; private set; }
        public bool Closed { get; private set; }
        public void Begin(byte members,float now)
        {
            Candidates=members;Submitted=0;Deadline=now+30;Closed=false;
            for(int i=0;i<4;i++)mvp[i]=blame[i]=-1;
            if((members&(members-1))==0){Submitted=members;Closed=true;}
        }
        bool Legal(int voter,int choice)=>choice==-1 || (choice>=0&&choice<4&&choice!=voter&&(Candidates&(1<<choice))!=0);
        public bool Submit(int voter,int m,int b,float now)
        {
            if(Closed || now>=Deadline || voter<0 || voter>3 || (Candidates&(1<<voter))==0 || (Submitted&(1<<voter))!=0 || !Legal(voter,m) || !Legal(voter,b))return false;
            mvp[voter]=m;blame[voter]=b;Submitted|=(byte)(1<<voter);if(Submitted==Candidates)Closed=true;return true;
        }
        public void Depart(int slot,float now) { Submit(slot,-1,-1,now); }
        public void Close() {Closed=true;}
        public byte Winners(bool forMvp)
        {
            var votes=forMvp?mvp:blame;var count=new int[4];int max=0;byte mask=0;
            foreach(var v in votes)if(v>=0){count[v]++;max=Math.Max(max,count[v]);}
            if(max==0)return 0;
            for(int i=0;i<4;i++)if(count[i]==max)mask|=(byte)(1<<i);return mask;
        }
    }
}
