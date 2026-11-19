using System;
using System.IO;
using System.Text;
using TankTogether.M0;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TankTogether.Networking
{
    public sealed partial class SteamCarrySession
    {
        const string ConfigMessage="tt.config.1",ConfigAckMessage="tt.config-ack.1",GMPauseMessage="tt.gm-pause.1";
        [Serializable] public sealed class ConfigEnvelope { public int revision,round; public string hash; public CarryTuningProfile profile; }
        [Serializable] sealed class ConfigAck { public int revision; public string hash; }
        readonly bool[] configReady=new bool[4];
        int configRevision;
        string configHash="",packageCompatibility;
        float nextConfigResend;
        public bool InRoom=>Running;
        public int ConfigRevision=>configRevision;
        public string ConfigHash=>configHash;
        public string ConfigurationStatus=> !Running?"本地配置 · "+CarryTuning.Hash(CarryTuning.Capture(lab)).Substring(0,8):
            "配置 r"+configRevision+" · "+(configHash.Length>=8?configHash.Substring(0,8):"等待房主")+(Host?" · 已确认 "+ConfigReadyCount+"/"+PlayerCount:" · 房主统一参数（只读）");
        int ConfigReadyCount {get{int n=0;for(int i=0;i<4;i++)if(connected[i]&&configReady[i])n++;return n;}}
        void RegisterTuningMessages()
        {
            net.CustomMessagingManager.RegisterNamedMessageHandler(ConfigMessage,ReceiveConfiguration);
            net.CustomMessagingManager.RegisterNamedMessageHandler(ConfigAckMessage,ReceiveConfigurationAck);
            net.CustomMessagingManager.RegisterNamedMessageHandler(GMPauseMessage,ReceiveGMPause);
        }
        void InitializeHostConfiguration()
        {
            configRevision=1; configHash=CarryTuning.Hash(CarryTuning.Capture(lab));Array.Clear(configReady,0,4);configReady[0]=true;nextConfigResend=0;
        }
        public bool OpenGM()
        {
            if(!Running)return false;
            if(Host)PauseSession("房主打开 GM，调参后从检查点继续。");
            else Send(GMPauseMessage,NetworkManager.ServerClientId,new byte[]{1},NetworkDelivery.ReliableSequenced);
            Capture(false);return true;
        }
        void ReceiveGMPause(ulong sender,FastBufferReader reader)
        {
            if(!Host||sender==NetworkManager.ServerClientId||!slots.ContainsKey(sender)||reader.Length-reader.Position!=1)return;
            reader.ReadValueSafe(out byte value);if(value!=1)return;
            if(phase==SteamCarryProtocol.Phase.Playing||phase==SteamCarryProtocol.Phase.Preparing)PauseSession("队友正在查看 GM，关闭后由房主从检查点继续。");
        }
        public bool ApplyRoomConfiguration(CarryTuningProfile profile,out string error)
        {
            error=null;if(!Host||!Running){error="只有房主可以应用联机参数。";return false;}
            var b=Instantiate(lab.baseline);var e=CarryTuning.Clone(lab.Tuning);
            try{CarryTuning.Read(profile,b,e,LevelId);}catch(Exception ex){error=ex.Message;return false;}finally{Destroy(b);}
            PauseSession("正在同步房主 GM 参数…");
            CarryTuning.Apply(lab,profile);playerReady.ResetForConfiguration();configRevision++;configHash=CarryTuning.Hash(CarryTuning.Capture(lab));
            Array.Clear(configReady,0,4);configReady[0]=true;PrepareRound();phase=SteamCarryProtocol.Phase.Paused;
            foreach(var pair in slots)if(pair.Key!=NetworkManager.ServerClientId)SendConfiguration(pair.Key);
            nextConfigResend=Time.unscaledTime+2;status="参数已应用，等待全员确认；关闭 GM 后由房主开始本段。";
            Log("gm_applied revision="+configRevision+" hash="+configHash);return true;
        }
        void SendConfiguration(ulong id)
        {
            if(!Host||configRevision<1)return;
            var packet=new ConfigEnvelope{revision=configRevision,round=round,hash=configHash,profile=CarryTuning.Capture(lab)};
            var bytes=Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet));
            if(bytes.Length>CarryTuning.MaxBytes)throw new InvalidDataException("GM payload too large");
            Send(ConfigMessage,id,bytes,NetworkDelivery.ReliableFragmentedSequenced);
        }
        void RetryConfiguration()
        {
            if(!Host||Time.unscaledTime<nextConfigResend)return;nextConfigResend=Time.unscaledTime+2;
            foreach(var pair in slots)if(pair.Key!=NetworkManager.ServerClientId&&!configReady[pair.Value])SendConfiguration(pair.Key);
        }
        static string ReadConfigText(FastBufferReader reader)
        {
            int n=reader.Length-reader.Position;if(n<1||n>CarryTuning.MaxBytes)throw new InvalidDataException("GM message size");
            var bytes=new byte[n];reader.ReadBytesSafe(ref bytes,n);return Encoding.UTF8.GetString(bytes);
        }
        public static void ValidateConfiguration(ConfigEnvelope packet,M0CarryBaseline b,CarryTuningExtras e,string level)
        {
            if(packet==null||packet.revision<1||packet.round<1||packet.hash==null||packet.hash.Length!=32)throw new InvalidDataException("Invalid GM envelope");
            CarryTuning.Read(packet.profile,b,e,level);
            if(CarryTuning.Hash(packet.profile)!=packet.hash)throw new InvalidDataException("GM hash mismatch");
        }
        void ReceiveConfiguration(ulong sender,FastBufferReader reader)
        {
            if(Host||sender!=NetworkManager.ServerClientId||net==null||!net.IsClient)return;
            try
            {
                var packet=JsonUtility.FromJson<ConfigEnvelope>(ReadConfigText(reader));
                if(!AcceptHostConfiguration(packet))return;
                Send(ConfigAckMessage,NetworkManager.ServerClientId,Encoding.UTF8.GetBytes(JsonUtility.ToJson(new ConfigAck{revision=configRevision,hash=configHash})),NetworkDelivery.ReliableSequenced);
            }
            catch(Exception e){Log("invalid_gm_config "+e.Message);Leave("参数同步失败，请确认使用同一版本测试包。");}
        }
        // Shared receive-state transition; sender authorization remains at the network boundary.
        bool AcceptHostConfiguration(ConfigEnvelope packet)
        {
                if(packet==null)throw new InvalidDataException("Empty GM config");
                if(packet.revision<configRevision)return false;
                var b=Instantiate(lab.baseline);var e=CarryTuning.Clone(lab.Tuning);
                try{ValidateConfiguration(packet,b,e,LevelId);}finally{Destroy(b);}
                if(packet.revision==configRevision){if(packet.hash!=configHash)throw new InvalidDataException("GM revision collision");}
                else
                {
                    Capture(false);CarryTuning.Apply(lab,packet.profile);configRevision=packet.revision;configHash=packet.hash;
                    round=Mathf.Max(round,packet.round);snapshotTick=-1;target=null;phase=SteamCarryProtocol.Phase.Paused;
                    lab.ResetLab();lab.SetSessionPaused(true);lab.Simulate=false;blockHeld=true;
                    if(lab.GM)lab.GM.RefreshFromCurrent();
                    status="已收到房主参数 r"+configRevision+"，等待房主从检查点开始。";
                    Log("gm_received revision="+configRevision+" hash="+configHash);
                }
            return true;
        }
        void ReceiveConfigurationAck(ulong sender,FastBufferReader reader)
        {
            if(!Host||sender==NetworkManager.ServerClientId||!slots.TryGetValue(sender,out int slot))return;
            try{var ack=JsonUtility.FromJson<ConfigAck>(ReadConfigText(reader));if(ack!=null&&ack.revision==configRevision&&ack.hash==configHash)configReady[slot]=true;}
            catch(Exception){Log("invalid_gm_ack");}
        }
    }
}
