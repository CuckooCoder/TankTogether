using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Generic;
using UnityEngine;
using TankTogether.Networking;
using Save=TankTogether.Networking.CoopCampaignSave;
namespace TankTogether.Editor
{
 public static class FormalSaveChecks
 {
    [Serializable] class Envelope{public int envelopeVersion=1,schemaVersion=1;public string payload,sha256;}
    public static string Result="Not run";
    public static void Run()
    {
        var rows=new List<string>();bool pass=true;
        void C(string n,bool ok){rows.Add(n+"="+ok);pass&=ok;}
        string root=Path.GetFullPath("Logs/save-v2-"+Guid.NewGuid().ToString("N"));const ulong owner=12345;
        try{
            C("slot1_preserves_original_id",Save.SlotCampaign("test",1)=="prototype-test");
            var ids=new HashSet<string>();for(int i=1;i<=5;i++)ids.Add(Save.SlotCampaign("test",i));C("five_distinct_slots",ids.Count==5);
            bool invalid=false;try{Save.SlotCampaign("test",6);}catch(ArgumentOutOfRangeException){invalid=true;}C("sixth_slot_rejected",invalid);
            var created=Save.Create(owner,Save.SlotCampaign("test",1),"test","start","v1",null,root);C("create",created.success);
            string legacy="{\"schemaVersion\":1,\"ownerSteamId\":\"12345\",\"campaignId\":\"prototype-test\",\"levelId\":\"test\",\"checkpointId\":\"cp1\",\"contentVersion\":\"v1\",\"savedAtUtc\":\"2026-10-14T10:00:00.0000000Z\",\"revision\":7}";
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(legacy))).Replace("-","").ToLowerInvariant();
            string original=JsonUtility.ToJson(new Envelope{payload=legacy,sha256=hash});File.WriteAllText(created.path,original);
            var loaded=Save.Load(owner,"prototype-test","v1",null,root);C("real_v1_without_new_fields_loads",loaded.success&&loaded.progress.checkpointId=="cp1"&&loaded.progress.run==null);
            C("read_does_not_modify_legacy",File.ReadAllText(created.path)==original);
            loaded.progress.run=new AdventureRun{id="legacy-upgrade-test",seconds=123.5,breaks=2};loaded.progress.run.members.Add(new AdventureMember{steamId="12345",name="A",jumps=3});
            var upgraded=Save.Save(loaded.progress,null,root);C("write_explicitly_upgrades",upgraded.success&&upgraded.progress.schemaVersion==2&&upgraded.progress.revision==8);
            C("original_v1_preserved",File.ReadAllText(created.path+".legacy-v1")==original);
            var reloaded=Save.Load(owner,"prototype-test","v1",null,root);C("stats_roundtrip",reloaded.success&&reloaded.progress.run.seconds==123.5&&reloaded.progress.run.members[0].jumps==3);
            var copy=Save.Copy(reloaded.progress);copy.run.members[0].jumps=99;C("no_mutable_alias",reloaded.progress.run.members[0].jumps==3);
            C("stale_delete_denied",Save.Delete(owner,"prototype-test","v1",7,null,root).status==Save.Status.Conflict&&File.Exists(created.path));
            var second=Save.Create(owner,Save.SlotCampaign("test",2),"test","start","v1",null,root);
            C("confirmed_delete",Save.Delete(owner,"prototype-test","v1",8,null,root).success);
            C("deleted_save_cannot_resurrect",Save.Load(owner,"prototype-test","v1",null,root).status==Save.Status.Missing);
            C("delete_isolated_from_other_slots",Save.Load(owner,Save.SlotCampaign("test",2),"v1",null,root).success);
            C("deleted_slot_recreatable",Save.Create(owner,"prototype-test","test","start","v1",null,root).success);
        }catch(Exception ex){pass=false;rows.Add(ex.ToString());}
        Result=string.Join("\n",rows)+"\npassed="+pass;File.WriteAllText("Logs/formal-save-v2.txt",Result);Debug.Log(Result);
    }
 }
}
