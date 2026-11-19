using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using TankTogether.Networking;
namespace TankTogether.Editor
{
    public static class CheckpointVoteChecks
    {
        public static string Result="Not run";
        [MenuItem("Tank Together/Formal/Run Vote Checks")]
        public static void Run()
        {
            var lines=new List<string>();bool passed=true;
            void C(string n,bool ok){lines.Add(n+"="+ok);passed&=ok;}
            var v=new CheckpointVote();C("host_required",!v.Begin(2,0));
            C("host_automatically_agrees",v.Begin(7,10)&&v.Yes==1&&!v.Passed);
            C("duplicate_start_rejected",!v.Begin(3,11));
            C("outsider_rejected",!v.Agree(3,v.Revision,12));
            C("stale_vote_rejected",!v.Agree(1,v.Revision-1,12));
            C("partial_not_passed",v.Agree(1,v.Revision,12)&&!v.Passed);
            v.Disconnected(1);C("disconnect_keeps_voter_removes_consent",v.Voters==7&&v.Yes==1);
            C("exact_deadline_rejected",!v.Agree(2,v.Revision,30));
            v.Agree(1,v.Revision,13);v.Agree(2,v.Revision,13);C("unanimous_passes",v.Passed);
            v.Cancel();C("cancel_is_not_pass",!v.Passed);
            C("host_alone_reset",v.Begin(1,40)&&v.Passed);
            Result=string.Join("\n",lines)+"\npassed="+passed;File.WriteAllText("Logs/formal-vote.txt",Result);Debug.Log(Result);
        }
    }
}
