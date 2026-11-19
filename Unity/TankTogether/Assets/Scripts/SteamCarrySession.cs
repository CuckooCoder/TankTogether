using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Netcode.Transports.Facepunch;
using Steamworks;
using Steamworks.Data;
using TankTogether.M0;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Color = UnityEngine.Color;
using InputCommand = TankTogether.Networking.SteamCarryProtocol.Input;
using Snapshot = TankTogether.Networking.SteamCarryProtocol.Snapshot;
using static TankTogether.Networking.SessionRoster;

namespace TankTogether.Networking
{
    [DefaultExecutionOrder(-100)]
    public sealed partial class SteamCarrySession : MonoBehaviour
    {
        public enum EntryMode { Home, LocalTest, Steam }
        public EntryMode Mode { get; private set; } = EntryMode.Home;
        public bool PersonalDialogVisible => showSettings || showSaveMenu || showCosmetic || exitConfirmation || AtFinish || Mode==EntryMode.Home;
        public bool MenuOverlayVisible => PersonalDialogVisible || Mode==EntryMode.Steam&&!captured;
        public bool CanChooseMode => !busy && !leaving && net == null && !lobby.HasValue;
        public M0CarryLab lab;
        public uint appId = 480;
        const string InputMessage = "tt.input.1", StateMessage = "tt.state.1", WelcomeMessage = "tt.welcome.1", AckMessage = "tt.ack.1";
        NetworkManager net;
        TankSteamTransport transport;
        Lobby? lobby;
        readonly SessionRoster roster = new SessionRoster();
        readonly Dictionary<ulong, int> slots = new Dictionary<ulong, int>();
        readonly Queue<(int slot, InputCommand input)> pending = new Queue<(int, InputCommand)>();
        readonly int[] lastSequence = new int[4];
        readonly float[] lastInputTime = new float[4];
        readonly bool[] rearm = new bool[4];
        readonly bool[] connected = new bool[4];
        readonly bool[] peerReady = new bool[4];
        SteamCarryProtocol.Phase phase;
        CoopCampaignSave.Progress progress;
        bool saveReady;
        string saveStatus = "由房主保存共同进度", queuedInvite = "";
        float prepareAt;
        public int PlayerCount => Array.FindAll(connected, v => v).Length;
        public bool IsPlaying => Running && phase == SteamCarryProtocol.Phase.Playing && !lab.Broken;
        public string LevelId => lab.GetComponent<WhiteboxCarryLevel>() ? lab.GetComponent<WhiteboxCarryLevel>().levelId : "steam-lab";
        public string CampaignId => SlotId(selectedSaveSlot);
        const string ContentVersion = "coop-prototype-20261013-1"; // Checkpoint schema unchanged; baseline signature gates gameplay compatibility.
        bool EveryoneReady {
            get {
                if (PlayerCount != RequiredPlayers || lab.GMOpen) return false;
                for (int i = 1; i < RequiredPlayers; i++)
                    if (connected[i] && (!configReady[i] || !peerReady[i] || Time.unscaledTime - lastInputTime[i] > 2)) return false;
                return true;
            }
        }
        Snapshot target;
        int round = 1, tick, sequence, localSlot = -1, snapshotTick = -1, operation;
        float yaw, pitch = 24, nextSend, nextSnapshot, lastSnapshotAt, connectAt, rtt, maxGap;
        bool initialized, busy, leaving, captured, blockHeld, quitting;
        string roomInput = "", status = "选择本地模拟或 Steam 好友联机。", roomToken = "", logPath;
        Font font;
        [NonSerialized] GUIStyle heading, label, small, button, field;
        int snapshotsReceived, inputsReceived;
        public bool Host => net != null && net.IsServer;
        bool Running => net != null && net.IsListening && localSlot >= 0;
        bool stretchNetwork = true;
        string Compatibility => packageCompatibility ?? (packageCompatibility = SteamCarryProtocol.Version + ":" + lab.baseline.Signature + ":" + LevelId + ":" + ContentVersion + ":" + AssistSignature + ":" + RouteSignature + (stretchNetwork?":stretch-b1":":original-a"));

        string RouteSignature => lab.GetComponent<WhiteboxCarryLevel>() ? lab.GetComponent<WhiteboxCarryLevel>().CompatibilitySignature : "flat";
        string AssistSignature => lab.twoPlayerGripSpring.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + lab.twoPlayerGripDamping.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + lab.twoPlayerGripMaxTorque.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        void Awake()
        {
            var route = lab.GetComponent<WhiteboxCarryLevel>();
            if (route) {route.PersistCheckpoint = SaveCheckpoint;route.Finished += OnRouteFinished;}
            lab.PlayerAction += RecordPlayerEvent;
            lab.TankFailed += OnTankFailed;
            lab.ReturnToLobbyRequested += ReturnToLobby;
            lab.DynamicRoster = true;
            lab.ExternalControl = true;
            lab.Testing = true;
            lab.Simulate = false;
            Application.runInBackground = true;
            PlayerPreferences.Load();
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
            logPath = Path.Combine(Application.persistentDataPath, "steam-session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
        }
        IEnumerator Start()
        {
            // Lab builds runtime objects in its Awake; wait until every Awake completed.
            yield return null;
            InitializeSteam();
#if !UNITY_EDITOR
            var display=PlayerPreferences.Data;bool safe=Array.IndexOf(Environment.GetCommandLineArgs(),"-safe-display")>=0;
            Screen.SetResolution(safe?1280:Mathf.Clamp(display.width,640,7680),safe?720:Mathf.Clamp(display.height,360,4320),safe?FullScreenMode.Windowed:display.windowMode==0?FullScreenMode.ExclusiveFullScreen:display.windowMode==1?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed);
#endif
            _ = Compatibility; // Cache shipped compatibility before any runtime GM edits.
            FreezeProxy();
            var launchArgs = Environment.GetCommandLineArgs();
            int lobbyArg = Array.IndexOf(launchArgs, "+connect_lobby");
            if (lobbyArg >= 0 && lobbyArg + 1 < launchArgs.Length) {
                OpenSteamMenu();
                roomInput = launchArgs[lobbyArg + 1]; JoinRoom();
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-steam-smoke") >= 0)
            {
                OpenSteamMenu();
                StartCoroutine(Smoke());
            }
        }
        public bool StartLocalTest()
        {
            if (!Application.isEditor || !CanChooseMode || Mode == EntryMode.LocalTest || !lab.Tank) return false;
            Mode = EntryMode.LocalTest;
            Capture(false);
            lab.BeginLocalSimulation();
            status = "本地模拟测试，不写入联机进度。";
            return true;
        }
        public bool StartLocalStretchLevel(bool inLevel = true)
        {
            if (!StartLocalTest()) return false;
            var experiment = lab.GetComponent<HeightExperiment>();
            if (!experiment) experiment = lab.gameObject.AddComponent<HeightExperiment>();
            experiment.Begin(lab, inLevel);
            return true;
        }
        public bool OpenSteamMenu()
        {
            if (!CanChooseMode || Mode == EntryMode.LocalTest) return false;
            Mode = EntryMode.Steam;
            packageCompatibility=null;
            if(stretchNetwork){var exp=lab.GetComponent<HeightExperiment>();if(!exp)exp=lab.gameObject.AddComponent<HeightExperiment>();exp.BeginNetwork(lab);}
            InitializeSteam();
            return true;
        }
        void ClosePersonalMenus()
        {
            if(displayPending)RestoreDisplay();showSettings=showCosmetic=showSaveMenu=exitConfirmation=exitSaveFailed=discardExitConfirmed=false;
            bindingIndex=bindingCandidate=-1;restoreKeysConfirm=false;DestroyPreview();
        }
        public void ReturnToLobby()
        {
            ClosePersonalMenus();frontIntent=0;ClearLobbyPortraits();
            if (!Host && Running && !exitPending) { reconnectRoom=0; StartCoroutine(DepartRoom()); return; }
            if (exitPending) return;
            if (lab.Stretch) lab.Stretch.End();
            if(lab.GM)lab.GM.Close();
            Mode = EntryMode.Home;
            lab.ReleaseLocalControl();
            lab.RestoreShippedBaseline();
            lab.DynamicRoster = true;
            lab.ExternalControl = true;
            lab.Testing = true;
            if (net != null || lobby.HasValue || busy) Leave("已返回大厅。");
            else {
                Capture(false);
                FreezeProxy();
                status = "选择本地模拟或 Steam 好友联机。";
            }
        }
        void InitializeSteam()
        {
            try
            {
                if (!SteamClient.IsValid) SteamClient.Init(appId, false);
                initialized = SteamClient.IsValid;
                PlayerPreferences.Load();
                if (!initialized) throw new InvalidOperationException("Steam client is not valid");
                SteamNetworkingUtils.InitRelayNetworkAccess();
                SteamFriends.OnGameLobbyJoinRequested -= OnInviteAccepted;
                SteamFriends.OnGameLobbyJoinRequested += OnInviteAccepted;
                SteamFriends.OnGameOverlayActivated -= OnOverlay;
                SteamFriends.OnGameOverlayActivated += OnOverlay;
                status = "Steam 已连接。可用房间码或 Steam 好友邀请加入。";
                Log("steam_initialized app=" + appId + " compatibility=" + Compatibility);
            }
            catch (Exception e) { initialized = false; status = "Steam 初始化失败：请登录桌面 Steam 后点击重新连接。"; Log("steam_init_error " + e.Message); }
        }
        void Log(string message)
        {
            Debug.Log("[SteamCarry] " + message);
            try { File.AppendAllText(logPath, DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine); } catch (IOException) { }
        }
        void ConfigureNetwork()
        {
            var go = new GameObject("Steam Carry Network");
            transport = go.AddComponent<TankSteamTransport>();
            net = go.AddComponent<NetworkManager>();
            net.NetworkConfig = new NetworkConfig {
                NetworkTransport = transport, EnableSceneManagement = false, ConnectionApproval = true,
                TickRate = 30, ClientConnectionBufferTimeout = 20,
                ConnectionData = Encoding.UTF8.GetBytes(Compatibility + ":" + roomToken)
            };
            net.ConnectionApprovalCallback += Approve;
            net.OnClientConnectedCallback += OnConnected;
            net.OnClientDisconnectCallback += OnDisconnected;
        }
        void RegisterMessages()
        {
            net.CustomMessagingManager.RegisterNamedMessageHandler(InputMessage, ReceiveInput);
            net.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, ReceiveState);
            net.CustomMessagingManager.RegisterNamedMessageHandler(WelcomeMessage, ReceiveWelcome);
            net.CustomMessagingManager.RegisterNamedMessageHandler(AckMessage, ReceiveAck);
            RegisterTuningMessages();
            RegisterRoomActions();
            RegisterAdventureMessages();
            RegisterCosmetics();
            net.CustomMessagingManager.RegisterNamedMessageHandler(PlayerReadyMessage, ReceivePlayerReady);
        }
        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            bool valid = request.Payload != null && request.Payload.Length <= 256 &&
                Encoding.UTF8.GetString(request.Payload) == Compatibility + ":" + roomToken;
            string denial = valid ? "房间已满" : "房间或测试版本不一致";
            bool identityAccepted = request.ClientNetworkId == NetworkManager.ServerClientId;
            if (valid && !leaving && !identityAccepted)
            {
                ulong transportId = net.GetTransportIdFromClientId(request.ClientNetworkId);
                if (transport.TryGetPeerSteamId(transportId, out ulong steamId))
                    identityAccepted = roster.Admit(steamId, request.ClientNetworkId, out _, out denial);
                else denial = "无法确认 Steam 连接身份";
            }
            response.Approved = valid && !leaving && identityAccepted;
            response.CreatePlayerObject = false;
            response.Pending = false;
            response.Reason = response.Approved ? "" : denial;
        }
        public async void CreateRoom()
        {
            if (Mode != EntryMode.Steam || busy || leaving || net != null || !initialized) return;
            if (!LoadCampaign()) return;
            busy = true; connectAt = Time.unscaledTime; status = "正在创建 Steam 好友房间…";
            int op = ++operation;
            try
            {
                var result = await SteamMatchmaking.CreateLobbyAsync(RequiredPlayers);
                if (quitting || op != operation) { result?.Leave(); return; }
                if (!result.HasValue) throw new InvalidOperationException("Steam 未能创建房间");
                lobby = result;
                roomToken = Guid.NewGuid().ToString("N");
                var room = result.Value;
                if (!room.SetPublic() || !room.SetJoinable(true) ||
                    !room.SetData("game", Compatibility) || !room.SetData("token", roomToken) || !room.SetData("host", SteamClient.SteamId.ToString()))
                    throw new InvalidOperationException("房间属性设置失败");
                ConfigureNetwork();
                roster.Begin(SteamClient.SteamId.Value);
                if (!net.StartHost()) throw new InvalidOperationException("Steam 主机启动失败");
                RegisterMessages();
                // NGO may invoke local connected synchronously while StartHost is running.
                localSlot = 0; slots[NetworkManager.ServerClientId] = 0; connected[0] = true; playerReady.SetConnected(0, true);
                InitializeHostConfiguration();
                SendCosmetic();
                phase = SteamCarryProtocol.Phase.Waiting; PrepareRound();
                reconnectRoom=0;
                roomInput = room.Id.ToString();
                status = "等待队友加入，三人到齐并准备后可开始。";
                Log("host_started protocol=" + SteamCarryProtocol.Version);
            }
            catch (Exception e) { status = "创建失败：" + e.Message; Log("create_error " + e.Message); Leave(status); }
            finally { if (op == operation) busy = false; }
        }
        public async void JoinRoom()
        {
            if (Mode != EntryMode.Steam || busy || leaving || net != null || !initialized) return;
            if (!ulong.TryParse(roomInput.Trim(), out ulong id)) { status = "请粘贴完整的数字房间号。"; return; }
            busy = true; connectAt = Time.unscaledTime; status = "正在加入 Steam 房间…";
            int op = ++operation;
            try
            {
                var result = await SteamMatchmaking.JoinLobbyAsync(id);
                if (quitting || op != operation) { result?.Leave(); return; }
                if (!result.HasValue) throw new InvalidOperationException("无法加入，请检查房间号、空位和 Steam 网络");
                lobby = result;
                if (result.Value.GetData("game") != Compatibility) throw new InvalidOperationException("房间的程序或物理参数版本不同，请双方使用同一测试包");
                roomToken = result.Value.GetData("token");
                if (roomToken.Length != 32) throw new InvalidOperationException("房间数据不完整，请重新加入");
                if (result.Value.Owner.Id == SteamClient.SteamId || result.Value.Owner.Id.ToString() != result.Value.GetData("host")) throw new InvalidOperationException("原房主已离开，请重新建房");
                ConfigureNetwork();
                transport.targetSteamId = result.Value.Owner.Id;
                if (!net.StartClient()) throw new InvalidOperationException("Steam 客户端启动失败");
                RegisterMessages();
                connectAt = Time.unscaledTime;
                status = "已进入房间，正在建立 Steam 游戏连接…";
                Log("client_connecting");
            }
            catch (Exception e) { status = "加入失败：" + e.Message; Log("join_error " + e.Message); Leave(status); }
            finally { if (op == operation) busy = false; }
        }
        void OnConnected(ulong id)
        {
            if (!Host) return;
            if (id == NetworkManager.ServerClientId) return;
            int slot;
            if (!roster.TryGetSlot(id, out slot) || slot < 1) { net.DisconnectClient(id); return; }

            connected[slot] = true; playerReady.SetConnected(slot, true); slots[id] = slot; lastSequence[slot] = 0;
            lastInputTime[slot] = Time.unscaledTime; rearm[slot] = true; peerReady[slot] = false; configReady[slot] = false;
            // A reconnecting member has no body until configuration + first valid input arrive.
            lab.SetActorActive(slot, !roster.Locked);
            if (!roster.Locked) lab.SetSessionPaused(true);
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe(slot); w.WriteValueSafe(round);
            net.CustomMessagingManager.SendNamedMessage(WelcomeMessage, id, w, NetworkDelivery.ReliableSequenced);
            SendConfiguration(id);
            status = "P" + (slot + 1) + (roster.Locked ? " 正在重连，其他成员继续。" : " 已加入，请准备。");
            Log("peer_connected slot=" + slot);
        }
        void OnDisconnected(ulong id)
        {
            if (leaving || quitting) return;
            if (Host)
            {
                bool voluntary=slots.TryGetValue(id,out int departing)&&!roster.CanReconnect(departing);
                roster.Disconnect(id);
                if (slots.TryGetValue(id, out int slot))
                {

                    resetVote.Disconnected(slot);
                    if(AtFinish){finishVote.Depart(slot,Time.unscaledTime);returnedToRoom|=(byte)(1<<slot);PersistPendingVote();}
                    int count = pending.Count;
                    for (int i = 0; i < count; i++) { var item = pending.Dequeue(); if (item.slot != slot) pending.Enqueue(item); }
                    connected[slot] = false; playerReady.SetConnected(slot, false); peerReady[slot] = false; lab.SetActorActive(slot, false); slots.Remove(id);
                    status = "P" + (slot + 1) + (voluntary ? " 已主动离队，本局不能重连。" : " 已断线，其他成员继续；原成员可重连。");
                    Log("peer_disconnected slot=" + slot);
                }
            }
            else
            {
                string reason = net != null ? net.DisconnectReason : "";
                ConnectionLost(reason);
            }
        }
        void ReceiveWelcome(ulong sender, FastBufferReader reader)
        {
            if (Host || sender != NetworkManager.ServerClientId || reader.Length - reader.Position != 8) return;
            reader.ReadValueSafe(out int slot); reader.ReadValueSafe(out int epoch);
            if (slot < 1 || slot >= RequiredPlayers || epoch < 1) return;
            localSlot = slot; round = epoch; lastSnapshotAt = Time.unscaledTime;
            if(lobby.HasValue)reconnectRoom=lobby.Value.Id.Value;
            status = "已连接，你是 P" + (slot + 1) + "。等待房主开始。";
            SendCosmetic();
            Log("client_welcome slot=" + slot);
        }
        public static byte[] ReadPayload(FastBufferReader reader)
        {
            int remaining = reader.Length - reader.Position;
            if (remaining < 1 || remaining > SteamCarryProtocol.MaxPacket) throw new InvalidDataException("Message size");
            var bytes = new byte[remaining]; reader.ReadBytesSafe(ref bytes, bytes.Length); return bytes;
        }
        void Send(string name, ulong client, byte[] bytes, NetworkDelivery delivery)
        {
            using var w = new FastBufferWriter(bytes.Length, Allocator.Temp);
            w.WriteBytesSafe(bytes);
            net.CustomMessagingManager.SendNamedMessage(name, client, w, delivery);
        }
        void ReceiveInput(ulong sender, FastBufferReader reader)
        {
            if (!Host || sender == NetworkManager.ServerClientId || !slots.TryGetValue(sender, out int slot)) return;
            try
            {
                var input = SteamCarryProtocol.DecodeInput(ReadPayload(reader));
                if (input.configRevision != configRevision || !configReady[slot] || !SteamCarryProtocol.AcceptInput(input, round, lastSequence[slot])) return;
                if (pending.Count >= 128) { net.DisconnectClient(sender); return; }
                lastSequence[slot] = input.sequence;
                peerReady[slot] = true; lastInputTime[slot] = Time.unscaledTime;
                if (input.pauseRequested) {
                    input.move = Vector3.zero; input.interact=false; input.held = input.jump = input.crouch = input.sprint = false; input.stretch = 0;
                    rearm[slot] = true;
                }
                if (phase == SteamCarryProtocol.Phase.Playing)
                {
                    if (!lab.Players[slot].active)
                    {
                        lab.SetActorActive(slot, true);
                        var route = lab.GetComponent<WhiteboxCarryLevel>();
                        lab.PlacePlayer(slot, route ? route.RespawnPoint(slot) : new Vector3(-3 + slot * .7f, .05f, -3));
                        rearm[slot] = true;
                        // Reconnect never restores an old held button or moves the shared tank.
                        // Keep the raw held bit for rearm: only a real release permits a new grab.
                        input.interact=false; input.jump = false; input.stretch = 0;
                    }
                    pending.Enqueue((slot, input));
                }
                inputsReceived++;
                using var w = new FastBufferWriter(4, Allocator.Temp); w.WriteValueSafe(input.sentAt);
                net.CustomMessagingManager.SendNamedMessage(AckMessage, sender, w, NetworkDelivery.UnreliableSequenced);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is OverflowException) { Log("invalid_input"); net.DisconnectClient(sender); }
        }
        void ReceiveAck(ulong sender, FastBufferReader reader)
        {
            if (Host || sender != NetworkManager.ServerClientId || reader.Length - reader.Position != 4) return;
            reader.ReadValueSafe(out float sent);
            float sample = (Time.unscaledTime - sent) * 1000;
            if (!float.IsNaN(sample) && sample >= 0 && sample < 30000) rtt = Mathf.Lerp(rtt, sample, rtt == 0 ? 1 : 0.2f);
        }
        void ReceiveState(ulong sender, FastBufferReader reader)
        {
            if (Host || sender != NetworkManager.ServerClientId || localSlot < 0) return;
            try
            {
                var state = SteamCarryProtocol.DecodeSnapshot(ReadPayload(reader));
                if (state.configRevision != configRevision || configRevision < 1) return;
                if (!SteamCarryProtocol.IsNewer(state.round, state.tick, round, snapshotTick)) return;
                bool reset = state.round != round || target == null;
                if (reset) {
                    round = state.round; blockHeld = true;
                    if (target == null || state.phase == SteamCarryProtocol.Phase.Preparing) { var route = lab.GetComponent<WhiteboxCarryLevel>(); yaw = route ? route.startYaw : 0; }
                }
                if (phase != state.phase && state.phase != SteamCarryProtocol.Phase.Playing) Capture(false);
                phase = state.phase; lab.SessionTeamSize = state.teamSize;
                status = phase == SteamCarryProtocol.Phase.Playing ? "共同进度由房主保存。" : phase == SteamCarryProtocol.Phase.Preparing ? "正在同步检查点，请稍候…" : "游戏已暂停，等待房主从最近检查点开始。";
                float gap = Time.unscaledTime - lastSnapshotAt;
                if (snapshotsReceived > 0) maxGap = Mathf.Max(maxGap, gap);
                lastSnapshotAt = Time.unscaledTime; snapshotTick = state.tick; target = state; snapshotsReceived++;
                if (reset) ApplySnapshot(1);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is OverflowException) { Log("invalid_snapshot"); Leave("收到无效同步数据，请确认两端版本相同。"); }
        }
        bool KnownCheckpoint(CoopCampaignSave.Progress value) => value.levelId == LevelId && (lab.GetComponent<WhiteboxCarryLevel>() ? lab.GetComponent<WhiteboxCarryLevel>().HasCheckpoint(value.checkpointId) : value.checkpointId == "start");
        bool LoadCampaign()
        {
            var result = CoopCampaignSave.Load(SteamClient.SteamId.Value, CampaignId, ContentVersion, KnownCheckpoint,SaveRoot);
            if (result.status == CoopCampaignSave.Status.Missing) result = CoopCampaignSave.Create(SteamClient.SteamId.Value, CampaignId, LevelId, "start", ContentVersion, KnownCheckpoint,SaveRoot);
            saveReady = result.success; progress = result.progress;
            saveStatus = result.message;
            if(saveReady && progress.run?.finished==true && progress.pendingReport!=null){
                // Host crash ends the vote; previously persisted votes remain in the pending report.
                reportSaved=false;if(!SaveFinalReport())saveReady=false;
            }
            if (!saveReady) status = "无法读取房主进度：" + result.message;
            return saveReady;
        }
        // Only authored checkpoint IDs may be persisted, after host physical parking confirmation.
        public bool SaveCheckpoint(string checkpointId)
        {
            if (!Host || !IsPlaying || lab.Broken || !saveReady || progress == null) return false;
            var parkingRoute = lab.GetComponent<WhiteboxCarryLevel>();
            if (parkingRoute && parkingRoute.checkpoints.Length > 0 && !parkingRoute.IsParkingCandidate(checkpointId)) return false;
            var next = CoopCampaignSave.Copy(progress); next.checkpointId = checkpointId;
            if(next.run!=null)next.run.checkpoints++;
            if(IsFinalCheckpoint(checkpointId))FreezeCompletion(next);
            if (!KnownCheckpoint(next)) return false;
            var result = CoopCampaignSave.Save(next, KnownCheckpoint,SaveRoot);
            saveStatus = result.message;
            if (result.success) progress = result.progress;
            else { saveReady = false; PauseSession("存档失败，请先退出房间并检查存档状态。"); }
            return result.success;
        }
        void PrepareRound()
        {
            var checkpointRoute = lab.GetComponent<WhiteboxCarryLevel>();
            if (checkpointRoute && progress != null) checkpointRoute.SelectCheckpoint(pendingReplay ? "start" : progress.checkpointId);
            lab.SessionTeamSize = Mathf.Clamp(PlayerCount, 2, RequiredPlayers);
            lab.ConfigureSessionRoster(connected, true);
            lab.SetSessionPaused(true);
            target = null; pending.Clear();
            for (int i = 0; i < 4; i++) { rearm[i] = true; peerReady[i] = i == 0; lastInputTime[i] = Time.unscaledTime; }
            var route = lab.GetComponent<WhiteboxCarryLevel>(); yaw = route ? route.startYaw : 0;
            blockHeld = true; tick = 0; nextSnapshot = 0;
        }
        public bool StartOrResume()
        {
            if(Host && roster.Locked && phase==SteamCarryProtocol.Phase.Failed && saveReady && lab.FailureReady){
                round++;PrepareRound();phase=SteamCarryProtocol.Phase.Playing;lab.SetSessionPaused(false);status="已从最近检查点重试。";return true;
            }
            if (!Host || !saveReady || !EveryoneReady || !PlayersConsented || phase == SteamCarryProtocol.Phase.Playing || phase == SteamCarryProtocol.Phase.Preparing) return false;
            if (progress?.run?.finished==true && !pendingReplay)return false;
            if (lab.Broken && !lab.FailureReady) return false;
            // Retry restores the last valid progress; failure/retry never advances or rewrites it.
            if (!roster.Lock()) return false;
            round++; PrepareRound(); phase = SteamCarryProtocol.Phase.Preparing; prepareAt = Time.unscaledTime;
            status = "正在同步检查点，等待所有队友准备完成…";
            Log("checkpoint_preparing round=" + round + " players=" + PlayerCount);
            return true;
        }
        public void RestartCheckpoint()
        {
            if (!Host || phase == SteamCarryProtocol.Phase.Preparing) return;
            if (lab.Broken) { StartOrResume(); return; }
            if (!IsPlaying) return;
            byte mask=0; for(int i=0;i<4;i++)if(connected[i] && lab.Players[i].active)mask|=(byte)(1<<i);
            if(resetVote.Begin(mask,Time.unscaledTime)) status="房主发起返回检查点投票，20 秒内全员按 P 同意。";
        }
        void OnTankFailed()
        {
            if (!Host || phase != SteamCarryProtocol.Phase.Playing) return;
            if(progress?.run!=null){if(lab.LastFailureWasDanger)progress.run.breaks++;else progress.run.techResets++;PersistRun();}
            PauseSession(lab.LastFailureWasDanger?"鱼缸碎了，本次搬运失败。由房主从最近检查点重试。":"测试区域异常，恢复最近检查点；不计碎缸。");
            Log("round_failed round=" + round);
        }
        void PauseSession(string reason)
        {
            if (!Host) return;
            phase = lab.Broken ? SteamCarryProtocol.Phase.Failed : SteamCarryProtocol.Phase.Paused; round++; tick = 0;
            pending.Clear(); blockHeld = true; Capture(false);
            lab.SetSessionPaused(true);
            for (int i = 0; i < 4; i++) { rearm[i] = true; peerReady[i] = i == 0; lastInputTime[i] = Time.unscaledTime; }
            nextSnapshot = 0; status = reason;
            Log("session_paused " + reason);
        }
        void AbortLoading(string reason="加载超时，已回房间；原存档保留，请重新准备。")
        {
            if(!Host)return;roster.ReturnToRoom();playerReady.ResetForConfiguration();
            phase=SteamCarryProtocol.Phase.Waiting;round++;pending.Clear();Capture(false);
            lab.SetSessionPaused(true);nextSnapshot=nextAdventureInfo=0;status=reason;Log("loading_aborted");
        }
        void FreezeProxy()
        {
            lab.ConfigureSessionRoster(new bool[4], false);
            lab.SetSessionPaused(true);
        }
        public void InviteFriends()
        {
            if (!Running || !lobby.HasValue || !SteamClient.IsValid) return;
            Capture(false); SteamFriends.OpenGameInviteOverlay(lobby.Value.Id);
            status = "在 Steam 好友列表中发送邀请；也可复制房间号。";
        }
        bool steamOverlayActive;
        void OnOverlay(bool active) {
            steamOverlayActive=active;
            if (!active) return;
            if (Mode == EntryMode.LocalTest) lab.ReleaseLocalControl();
            else Capture(false);
        }
        void OnInviteAccepted(Lobby invited, SteamId friend)
        {
            if (lobby.HasValue && lobby.Value.Id == invited.Id) return;
            if (Mode == EntryMode.LocalTest || Running || busy || leaving || net != null) {
                queuedInvite = invited.Id.ToString();
                status = "收到好友邀请；返回大厅后可在 Steam 联机中加入。";
                if (Mode == EntryMode.LocalTest) lab.ReleaseLocalControl();
                else Capture(false);
                return;
            }
            OpenSteamMenu();
            roomInput = invited.Id.ToString(); JoinRoom();
        }
        void Update()
        {
            if((Mode!=EntryMode.Steam||!Running||IsPlaying||AtFinish)&&Array.Exists(lobbyPortraits,p=>p!=null))ClearLobbyPortraits();
            UpdatePersonalSettings();
            RefreshCosmetics();
            if (initialized && SteamClient.IsValid) SteamClient.RunCallbacks();
            CheckConnectionHealth();
            HandleMenuEscape();
            if (!Application.isFocused || steamOverlayActive) Capture(false);
            if (!Running) return;
            RetryConfiguration();
            UpdateRoomActions();
            UpdateAdventure();
            var k = Keyboard.current; var m = Mouse.current;
            if (k == null || m == null) {
                if (Host) {
                    Capture(false);
                    if (IsPlaying) lab.SetInput(0, Vector3.zero, false, false, false, false, 0);
                } else if (Time.unscaledTime >= nextSend) {
                    var inactive = new InputCommand { configRevision=configRevision, round = round, sequence = ++sequence, pauseRequested = true, sentAt = Time.unscaledTime };
                    Send(InputMessage, NetworkManager.ServerClientId, SteamCarryProtocol.Encode(inactive), NetworkDelivery.ReliableSequenced);
                    nextSend = Time.unscaledTime + 1f/lab.Tuning.inputHeartbeat;
                }
                return;
            }

            if (Application.isFocused && !steamOverlayActive && !lab.GMOpen && !showSettings && !showSaveMenu && !exitConfirmation && !showCosmetic && k.enterKey.wasPressedThisFrame) Capture(true);
            if (!lab.GMOpen && !showSettings && !showSaveMenu && !exitConfirmation && !showCosmetic && Host && k.rKey.wasPressedThisFrame) RestartCheckpoint();
            if (captured)
            {
                Vector2 delta = m.delta.ReadValue(); yaw += delta.x * lab.Tuning.mouseYaw * PlayerPreferences.Data.sensitivity; pitch = Mathf.Clamp(pitch - delta.y * lab.Tuning.mousePitch * PlayerPreferences.Data.sensitivity * (PlayerPreferences.Data.invertY?-1:1), M0CarryLab.MinPitch, M0CarryLab.MaxPitch);
            }
            lab.SetExternalView(localSlot, yaw, pitch);
            if (!PlayerPreferences.Held(PlayerAction.Grab)) blockHeld = false;
            Vector3 raw = captured && IsPlaying ? PlayerPreferences.Move : Vector3.zero;
            var input = new InputCommand {
                configRevision=configRevision, round = round, sequence = ++sequence, move = Quaternion.Euler(0, yaw, 0) * Vector3.ClampMagnitude(raw, 1),
                interact = IsPlaying && captured && PlayerPreferences.Pressed(PlayerAction.Interact),
                held = IsPlaying && captured && !blockHeld && PlayerPreferences.Held(PlayerAction.Grab),
                jump = IsPlaying && captured && PlayerPreferences.Pressed(PlayerAction.Jump), crouch = IsPlaying && captured && PlayerPreferences.Held(PlayerAction.Crouch),
                stretch = stretchNetwork && IsPlaying && captured ? HeightExperiment.Combine(PlayerPreferences.Held(PlayerAction.Grow),PlayerPreferences.Held(PlayerAction.Shrink)):0,
                sprint = IsPlaying && captured && PlayerPreferences.Held(PlayerAction.Run), pauseRequested = !Host && (lab.GMOpen || Time.unscaledTime - lastSnapshotAt > 2), sentAt = Time.unscaledTime
            };
            // Send every edge immediately, plus movement changes and a 10 Hz heartbeat.
            bool changed = input.interact || input.stretch != previous.stretch || input.move != previous.move || input.held != previous.held || input.jump || input.crouch != previous.crouch || input.sprint != previous.sprint || input.pauseRequested != previous.pauseRequested;
            if (input.pauseRequested) { input.stretch=0; input.move = Vector3.zero; input.interact=false; input.held = input.jump = input.crouch = input.sprint = false; }
            if (Host) { if (IsPlaying) pending.Enqueue((0, input)); }
            else if (changed || Time.unscaledTime >= nextSend)
            {
                Send(InputMessage, NetworkManager.ServerClientId, SteamCarryProtocol.Encode(input), NetworkDelivery.ReliableSequenced);
                nextSend = Time.unscaledTime + 1f/lab.Tuning.inputHeartbeat;
            }
            previous = input;
        }
        InputCommand previous;
        void FixedUpdate()
        {
            if (!Host) return;
            tick++;
            if (phase == SteamCarryProtocol.Phase.Preparing) {
                int loaded = 1;
                for (int i = 1; i < RequiredPlayers; i++) if (connected[i] && configReady[i] && peerReady[i] && Time.unscaledTime-lastInputTime[i] <= 2) loaded++;
                if (loaded == RequiredPlayers) {
                    if(!CommitAdventureStart()){AbortLoading("开局存档提交失败，原进度保留；请检查存档后重试。");return;}
                    phase = SteamCarryProtocol.Phase.Playing; lab.SetSessionPaused(false); status = "已从检查点开始，按 Enter 操作。"; Log("checkpoint_started players=" + loaded);
                }
                else if (Time.unscaledTime - prepareAt > 15) AbortLoading();
            }
            if (phase != SteamCarryProtocol.Phase.Playing) { pending.Clear(); return; }
            for (int i = 1; i < 4; i++) if (connected[i] && Time.unscaledTime - lastInputTime[i] > 2) {
                // A transient stall releases controls but preserves passive support; only the
                // confirmed network disconnect destroys the body and retains a reconnect seat.
                lab.SetInput(i, Vector3.zero, false, false, false, false, 0); rearm[i] = true;
            }
            while (pending.Count > 0)
            {
                var p = pending.Dequeue();
                if (!connected[p.slot] || p.input.round != round) continue;
                if (!p.input.held) rearm[p.slot] = false;
                lab.SetInput(p.slot, p.input.move, p.input.held && !rearm[p.slot], p.input.jump, p.input.crouch, p.input.sprint, stretchNetwork?p.input.stretch:0);
                if(p.input.interact && !p.input.pauseRequested) {
                    var lift=lab.GetComponent<WhiteboxLift>();
                    if(lift)lift.TryInteract(p.slot,out _,p.input.held);
                }
                lastInputTime[p.slot] = Time.unscaledTime;
            }
        }
        void LateUpdate()
        {
            if (!Running) return;
            if (!Host) { if (target != null) ApplySnapshot(1 - Mathf.Exp(-Time.unscaledDeltaTime * lab.Tuning.snapshotSmoothing)); return; }
            if (Time.unscaledTime < nextSnapshot) return;
            nextSnapshot = Mathf.Max(nextSnapshot + 1f / lab.Tuning.snapshotRate, Time.unscaledTime); // Keep cadence across variable frame times; never send a backlog.
            var s = new Snapshot { voteRevision=resetVote.Revision, voteMask=resetVote.Voters, voteYes=resetVote.Yes, voteSeconds=resetVote.Active?Mathf.Max(0,resetVote.Deadline-Time.unscaledTime):0, readyRevision=playerReady.Revision, readyMask=playerReady.ReadyMask, configRevision=configRevision, round = round, tick = tick, phase = phase, teamSize = lab.SessionTeamSize, broken = lab.Broken, position = lab.Tank.position, rotation = lab.Tank.rotation, velocity = lab.Tank.linearVelocity };
            for (int i = 0; i < 4; i++)
            {
                var a = lab.Players[i];
                s.actors[i] = new SteamCarryProtocol.Actor { active = connected[i] && a.active, grip = lab.IsGripping(i), support = a.support, rejected = Time.time < a.failedUntil, position = a.body ? a.body.position : Vector3.zero, height = a.height };
            }
            var routeState = lab.GetComponent<WhiteboxCarryLevel>();
            s.checkpoint = routeState ? routeState.CurrentCheckpoint : 0;
            var lift = lab.GetComponent<WhiteboxLift>();
            if (lift) { s.liftDistance = lift.Distance; s.liftMoving = lift.Moving; s.liftGoingUp=lift.GoingUp; }
            var movingBlocks=lab.GetComponent<WhiteboxMovingBlocks>();
            if(movingBlocks)s.movingBlockPhase=movingBlocks.NormalizedPhase;
            var bytes = SteamCarryProtocol.Encode(s);
            foreach (var id in net.ConnectedClientsIds) if (id != NetworkManager.ServerClientId) Send(StateMessage, id, bytes, NetworkDelivery.UnreliableSequenced);
        }
        void ApplySnapshot(float blend)
        {
            var routeState = lab.GetComponent<WhiteboxCarryLevel>();
            if (routeState && routeState.checkpoints.Length > 0 && routeState.CurrentCheckpoint != target.checkpoint)
            {
                if (target.checkpoint >= routeState.checkpoints.Length) { Leave("检查点数据不匹配。"); return; }
                routeState.SelectCheckpoint(routeState.checkpoints[target.checkpoint].id);
                lab.ResetLab();
            }
            var lift = lab.GetComponent<WhiteboxLift>();
            if (lift) lift.ApplyProxy(target.liftDistance, target.liftMoving, target.liftGoingUp);
            lab.GetComponent<WhiteboxMovingBlocks>()?.ApplyNetworkPhase(target.movingBlockPhase, blend);
            lab.Tank.transform.SetPositionAndRotation(Vector3.Lerp(lab.Tank.transform.position, target.position, blend), Quaternion.Slerp(lab.Tank.transform.rotation, target.rotation, blend));
            lab.Tank.linearVelocity = target.velocity;
            lab.SetProxyState(target.broken, target.broken ? "鱼缸破碎，请房主按 R 重置。" : "状态来自房主物理模拟");
            for (int i = 0; i < RequiredPlayers; i++)
            {
                var s = target.actors[i]; var a = lab.Players[i];
                connected[i] = adventureInfo!=null?(adventureInfo.online&(1<<i))!=0:s.active;
                bool spawn = !a.active && s.active;
                lab.SetActorActive(i, s.active);
                if (!s.active) continue;
                a = lab.Players[i];
                a.body.transform.position = Vector3.Lerp(a.body.transform.position, s.position, spawn ? 1 : blend);
                a.height = Mathf.Min(s.height,M0CarryLab.MaximumPlayerHeight); a.remoteGrip = s.grip; a.support = s.support; a.failedUntil = s.rejected ? Time.time + 0.1f : 0;
                lab.UpdateActorVisual(a);
                a.capsule.height = a.height; a.capsule.center = Vector3.up * a.height / 2;
                a.indicator.localPosition = Vector3.up * (a.height + 0.06f);
            }
        }
        void Capture(bool value)
        {
            value = value && IsPlaying && !lab.GMOpen;
            captured = value; blockHeld = true;
            if (!value && Host && IsPlaying && localSlot >= 0) {
                lab.SetInput(localSlot, Vector3.zero, false, false, false, false, 0);
                rearm[localSlot] = true;
            }
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !value;
        }
        void OnApplicationFocus(bool focus) { if (!focus) Capture(false); }
        public void Leave(string message = "已离开房间。")
        {
            if (leaving) return;
            ClosePersonalMenus();
            if(Host){if(AtFinish){finishVote.Close();PersistPendingVote();SaveFinalReport();}else PersistRun();}
            if (lab.Stretch) lab.Stretch.End();
            if(lab.GM)lab.GM.Close(); lab.RestoreShippedBaseline();
            configRevision=0;configHash="";Array.Clear(configReady,0,4);
            leaving = true; operation++; busy = false; Capture(false);
            if (SteamClient.IsValid) lobby?.Leave(); lobby = null;
            if (net != null)
            {
                net.OnClientConnectedCallback -= OnConnected; net.OnClientDisconnectCallback -= OnDisconnected;
                net.Shutdown(); Destroy(net.gameObject); net = null; transport = null;
            }
            resetVote.Cancel(); roster.Clear(); playerReady.Clear(); slots.Clear(); pending.Clear(); Array.Clear(connected, 0, 4); Array.Clear(peerReady, 0, 4);
            phase = SteamCarryProtocol.Phase.Waiting; saveReady = false; progress = null; pendingReplay=false;reportSaved=false;adventureInfo=null;returnedToRoom=0;exitConfirmation=false;showSaveMenu=false;steamOverlayActive=false;appliedCosmetics.Clear();
            localSlot = -1; snapshotTick = -1; target = null; sequence = 0; round = 1; rtt = 0; maxGap = 0; snapshotsReceived = 0; lastSnapshotAt = 0;
            if (lab != null && lab.Tank != null) { FreezeProxy(); }
            status = message; Log("session_left");
            if (!quitting) StartCoroutine(AfterLeave());
        }
        IEnumerator AfterLeave()
        {
            yield return null; yield return null;
            // The transport owns Steam shutdown during NGO teardown; reinitialize afterwards.
            string message = status;
            initialized = SteamClient.IsValid;
            if (Mode == EntryMode.Steam) InitializeSteam();
            status = message; leaving = false;
        }
        void OnDestroy()
        {
            PlayerPreferences.RestoreQuality();
            if (lab) { lab.TankFailed -= OnTankFailed; lab.ReturnToLobbyRequested -= ReturnToLobby; }
            DestroyPreview();DestroyAvatars();ClearLobbyPortraits();
            quitting = true; operation++;
            SteamFriends.OnGameLobbyJoinRequested -= OnInviteAccepted; SteamFriends.OnGameOverlayActivated -= OnOverlay;
            if (SteamClient.IsValid) lobby?.Leave(); lobby = null;
            if (net != null) { net.OnClientDisconnectCallback -= OnDisconnected; net.Shutdown(); Destroy(net.gameObject); }
            if (SteamClient.IsValid) SteamClient.Shutdown();
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            if (font) Destroy(font);
        }
        void OnApplicationQuit()
        {
            if(Host)PersistRun();
            quitting = true; operation++;
            if (SteamClient.IsValid) lobby?.Leave(); lobby = null;
            if (net != null) { net.OnClientConnectedCallback -= OnConnected; net.OnClientDisconnectCallback -= OnDisconnected; }
        }
        void Styles()
        {
            if (heading != null && heading.font && heading.fontSize == 25) return;
            heading = new GUIStyle(GUI.skin.label) { font = font, fontSize = 25, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 18, wordWrap = true, normal = { textColor = Color.white } };
            small = new GUIStyle(label) { fontSize = 15 };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 18 };
            frontSmallButton = null;
            field = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 19 };
        }
        static void Panel(Rect r) { var c = GUI.color; GUI.color = new Color(0.025f, 0.05f, 0.07f, 0.96f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = c; }
        public string NetworkDiagnostics => !Running ? "未在 Steam 房间中" : Host ? "房主 · 目标状态发送 "+lab.Tuning.snapshotRate+" Hz · 当前 " + PlayerCount + " 人" : "加入方 · 应用层往返 " + rtt.ToString("F0") + " ms · 距上个状态 " + ((Time.unscaledTime-lastSnapshotAt)*1000).ToString("F0") + " ms · 最大间隔 " + (maxGap*1000).ToString("F0") + " ms";
        void OnGUI()
        {
            if (lab.GMOpen) return;
            Styles(); var old = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            bool fullMenu=Mode==EntryMode.Home||Mode==EntryMode.Steam&&(!Running||!IsPlaying)||PersonalDialogVisible;
            Vector3 offset=fullMenu?new Vector3((Screen.width-1280*scale)/2,(Screen.height-720*scale)/2,0):Vector3.zero;
            GUI.matrix = Matrix4x4.TRS(offset, Quaternion.identity, Vector3.one * scale);
            if(MenuOverlayVisible||Mode==EntryMode.Home){var tint=GUI.color;GUI.color=new Color(.015f,.025f,.035f,.92f);GUI.DrawTexture(new Rect(-offset.x/scale,-offset.y/scale,Screen.width/scale,Screen.height/scale),Texture2D.whiteTexture);GUI.color=tint;}
            textInputFocused=GUI.GetNameOfFocusedControl()=="save-name" || GUI.GetNameOfFocusedControl()=="room-code";
            if(showSettings){DrawSettings();GUI.matrix=old;return;}
            if(showCosmetic){DrawCosmetic();GUI.matrix=old;return;}
            if(exitConfirmation){DrawExitConfirmation();GUI.matrix=old;return;}
            if(showSaveMenu){DrawSaveMenu();GUI.matrix=old;return;}
            if(AtFinish){DrawFinish();GUI.matrix=old;return;}

            if (Mode == EntryMode.LocalTest)
            {
                if (!lab.LocalInputCaptured)
                {
                    if (GameText.Button(new Rect(24, 122, 260, 40), "返回大厅（结束本次模拟）", button)) ReturnToLobby();
                    if(GameText.Button(new Rect(295,122,160,40),"个人设置",button))OpenSettings();
                    GameText.Label(new Rect(24, 168, 300, 44), "本地模拟不写入房主存档", small);
                    if (!string.IsNullOrEmpty(queuedInvite))
                        GameText.Label(new Rect(24, 218, 350, 65), "收到 Steam 好友邀请。\n返回大厅后选择 Steam 好友联机。", small);
                }
                GUI.matrix = old;
                return;
            }
            if (Mode == EntryMode.Home)
            {
                DrawHome();
                GUI.matrix = old;
                return;
            }
            if (Running && lab.Broken)
            {
                DrawFailure();
                GUI.matrix = old;
                return;
            }
            if(exitConfirmation){DrawExitConfirmation();GUI.matrix=old;return;}
            if(showSaveMenu){DrawSaveMenu();GUI.matrix=old;return;}
            if(AtFinish){DrawFinish();GUI.matrix=old;return;}
            if (!Running){DrawSteamEntry();GUI.matrix=old;return;}
            if (!IsPlaying){DrawRoomLobby();GUI.matrix=old;return;}
            Panel(new Rect(20, 20, 780, 125));
            GameText.Label(new Rect(38, 30, 740, 36), "搬鱼缸 · 好友合作", heading);
            GameText.Label(new Rect(38, 72, 740, 60), status, small);
            if (!Running)
            {
                Panel(new Rect(300, 167, 680, 450));
                GameText.Label(new Rect(330, 183, 620, 38), "邀请好友，共同继续这一段", heading);
                GameText.Label(new Rect(330, 230, 620, 55), "三人合作。各自登录 Steam，凭房间码即可加入。\n房主保存共同进度，队友加入房主的旅程。", label);
                GUI.enabled = initialized && !busy && !leaving && net == null;
                if (GameText.Button(new Rect(330, 300, 620, 44), "创建房间 · 选择存档", button)) { OpenSaveMenu();createAfterSelecting=true; }
                if(GameText.Button(new Rect(330,535,620,40),"管理 / 选择五个存档",button))OpenSaveMenu();
                GUI.SetNextControlName("room-code");roomInput = GUI.TextField(new Rect(330, 365, 435, 42), roomInput, 24, field);
                if (GameText.Button(new Rect(780, 365, 170, 42), "加入房间", button)) JoinRoom();
                if (!string.IsNullOrEmpty(queuedInvite) && GameText.Button(new Rect(330, 421, 620, 38), "加入刚才邀请的房间", button)) { roomInput = queuedInvite; queuedInvite = ""; JoinRoom(); }
                GUI.enabled = true;
                if (!initialized && !busy && !leaving && GameText.Button(new Rect(330, 421, 620, 38), "重新连接 Steam", button)) InitializeSteam();
                GameText.Label(new Rect(330, 471, 620, 56), "可接受 Steam 好友邀请，也可粘贴房间号。\n进度保存在房主本机；进入房间后可查看当前检查点。", small);
                if ((net != null || busy) && GameText.Button(new Rect(330, 585, 620, 38), "取消连接", button)) Leave();
            }
            else
            {
                Panel(new Rect(825, 20, 435, 300));
                GameText.Label(new Rect(845, 32, 390, 30), (Host ? "房主" : "队友") + " · " + PlayerCount + "/" + RequiredPlayers + " 人 · 你是 P" + (localSlot + 1), heading);
                GameText.Label(new Rect(845, 76, 390, 26), "房间：" + (lobby.HasValue ? lobby.Value.Id.ToString() : "—"), small);
                for (int i = 0; i < RequiredPlayers; i++)
                {
                    var a = lab.Players[i];
                    bool reserved=Host?roster.Identity(i)!=0:!string.IsNullOrEmpty(adventureInfo?.identities?[i])&&adventureInfo.identities[i]!="0";
                    string state = !connected[i] ? (reserved?"离线":"空位") : !IsPlaying ? (i == 0 ? "房主" : (DisplayReadyMask & (1 << i)) != 0 ? "已准备" : "未准备") : (a.joint || a.remoteGrip) ? "已抓取" : Time.time < a.failedUntil ? "松开后重按抓取" : a.support ? "仅支撑" : "自由移动";
                    if(Host&&!roster.Locked&&i>0&&connected[i]&&GameText.Button(new Rect(1200,112+i*29,50,26),"踢出",small))KickLobbyMember(i);
                    DrawAvatar(i,new Rect(841,112+i*29,24,24));
                    GameText.RawLabel(new Rect(872, 112 + i * 29, 325, 28), "P" + (i + 1) + " · " + (Host?PeerName(i):adventureInfo?.names?[i]??"") + "  " + GameText.Translate(state), label);
                }
                GameText.Label(new Rect(845, 237, 390, 55), "检查点：" + (lab.GetComponent<WhiteboxCarryLevel>() ? lab.GetComponent<WhiteboxCarryLevel>().CheckpointLabel : "起点") + " · 房主本地存档\n" + (Host ? saveStatus : "队友原有存档不会被覆盖"), small);
                GameText.Label(new Rect(845,286,390,28),"累计 "+Duration(Host?progress?.run?.seconds??0:adventureInfo?.seconds??0)+" · 存档 "+(Host?selectedSaveSlot:adventureInfo?.slot??1),small);
                Panel(new Rect(20, 603, 1240, 97));
                string info = !IsPlaying ? "三人到齐，两位队友准备后可开局。" : lab.Broken ? "鱼缸破碎：房主按 R，再从检查点重开。" : ControlHint();
                if (!Host && Time.unscaledTime - lastSnapshotAt > 1) info = "同步中断，等待房主连接恢复；目前显示最后收到的状态。";
                GameText.RawLabel(new Rect(38, 614, 1200, 32), GameText.Translate(info), label);
                GameText.Label(new Rect(38, 657, 1200, 28), GameText.Translate("Enter 操作 · Esc 打开房间菜单 · 房主 R 发起复位投票")+" · "+GameText.Translate("同意复位投票")+": "+PlayerPreferences.KeyName(PlayerPreferences.Data.keys[(int)PlayerAction.AgreeReset]), small);
                DrawResetVote();
                if (!captured)
                {
                    if(GameText.Button(new Rect(20,535,270,42),"个人设置",button))OpenSettings();
                    if((Host?!roster.Locked:phase==SteamCarryProtocol.Phase.Waiting)&&GameText.Button(new Rect(20,483,270,42),"角色装扮",button))OpenCosmetic();
                    if(Host&&!roster.Locked){
                        if(GameText.Button(new Rect(350,265,280,40),"选择房主存档",button))OpenSaveMenu();
                        if(progress?.run?.finished==true && GameText.Button(new Rect(650,265,280,40),confirmReplay?"确认从起点重新挑战":"重新挑战…",button)){
                            if(!confirmReplay)confirmReplay=true;
                            else {pendingReplay=true;confirmReplay=false;playerReady.ResetForConfiguration();status="本场将从起点挑战；新局成功进入后才重置进度。";}
                        }
                    }
                    Panel(new Rect(330, 325, 620, 257));
                    if (!IsPlaying)
                    {
                        GUI.enabled = Host && saveReady && EveryoneReady && PlayersConsented && phase != SteamCarryProtocol.Phase.Preparing;
                        string action = phase == SteamCarryProtocol.Phase.Preparing ? "正在同步检查点…" : PlayerCount < RequiredPlayers ? "等待三人到齐" : Host ? (!PlayersConsented ? "等待所有队友点击准备" : "从最近检查点开始 / 继续") : "等待房主开始 / 继续";
                        if (Host) { if (GameText.Button(new Rect(350, 343, 580, 44), action, button)) StartOrResume(); }
                        else { GUI.enabled = ReadyMutable && target != null; bool consent = (DisplayReadyMask & (1 << localSlot)) != 0; if (GameText.Button(new Rect(350, 343, 580, 44), consent ? "取消准备" : "准备", button)) SetLocalPlayerReady(!consent); }
                        GUI.enabled = true;
                    }
                    else if (GameText.Button(new Rect(350, 343, 580, 44), "继续操作 / Enter", button)) Capture(true);
                    if (GameText.Button(new Rect(350, 404, 280, 40), "邀请 Steam 好友", button)) InviteFriends();
                    if (GameText.Button(new Rect(650, 404, 280, 40), "复制房间号", button)) GUIUtility.systemCopyBuffer = lobby.Value.Id.ToString();
                    if (GameText.Button(new Rect(350, 458, 280, 40), "离开房间…", button)) exitConfirmation=true;
                    GUI.enabled = Host && IsPlaying;
                    if (GameText.Button(new Rect(650, 458, 280, 40), "发起返回检查点投票", button)) RestartCheckpoint();
                    GUI.enabled = true;
                    GameText.Label(new Rect(350, 516, 580, 50), "普通掉线其他人继续；原成员可重连。房主离开会结束房间。", small);
                }
            }
            GUI.matrix = old;
        }
        void DrawFailure()
        {
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            float x = 340, y = 220;
            Panel(new Rect(x,y,600,285));
            GameText.Label(new Rect(x+28,y+20,544,40), (Host&&!lab.LastFailureWasDanger?"技术复位":"鱼缸碎了"), heading);
            GameText.Label(new Rect(x+28,y+70,544,50), "本次搬运失败。最近检查点：" + (lab.GetComponent<WhiteboxCarryLevel>() ? lab.GetComponent<WhiteboxCarryLevel>().CheckpointLabel : "起点") + "。\n已保存的进度保留，重试无需重新邀请好友。", label);
            GUI.enabled = Host && saveReady && lab.FailureReady;
            string action = !Host ? "等待房主重试" : !lab.FailureReady ? "本次搬运结束…" : "从检查点重试 / R";
            if (Host) { if (GameText.Button(new Rect(x+28,y+143,544,44), action, button)) StartOrResume(); }
            else { GUI.enabled = ReadyMutable && target != null; bool consent = (DisplayReadyMask & (1 << localSlot)) != 0; if (GameText.Button(new Rect(x+28,y+143,544,44), consent ? "已准备 · 点击取消" : "准备重试", button)) SetLocalPlayerReady(!consent); }
            GUI.enabled = true;
            if (GameText.Button(new Rect(x+28,y+202,544,38), Host ? "返回大厅（结束房间）" : "离开房间并返回大厅", button)) exitConfirmation=true;
            GameText.Label(new Rect(x+28,y+250,544,28), PlayerCount + "/" + RequiredPlayers + " 人 · " + (Host ? "你是房主" : "队友") + " · 房主保存共同进度", small);
        }
        [Serializable] class SmokeReport { public string utc, protocol, baseline, error, checkpoint, runId; public double savedSeconds; public int savedMembers; public bool steam, lobbyCreated, hostListening, offlineActorsDisabled, restarted, gmAvailable, glassAvailable, gmOnlineApplied, gmClipboardRoundtrip, gmDefaultsRestored, displayApplied, displayRestored; public int connectedClients; public uint app; }
        IEnumerator Smoke()
        {
            string path = Path.Combine(Application.persistentDataPath, "steam-smoke.json");
            var args = Environment.GetCommandLineArgs(); int arg = Array.IndexOf(args, "-steam-report"); if (arg >= 0 && arg + 1 < args.Length) path = args[arg + 1];
            string folder = Path.GetDirectoryName(path);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "steam-menu.png"));
            var report = new SmokeReport { utc = DateTime.UtcNow.ToString("O"), app = appId, protocol = SteamCarryProtocol.Version, baseline = lab.baseline.Signature, steam = initialized };
            var glass = Resources.Load<Material>("TankGlass");
            report.gmAvailable = lab.GM != null;
            report.glassAvailable = glass && glass.shader && glass.shader.isSupported && glass.shader.name == "Tank Together/Clear Glass";
            if (initialized)
            {
                CreateRoom(); float deadline = Time.unscaledTime + 30;
                while (busy && Time.unscaledTime < deadline) yield return null;
                yield return new WaitForSecondsRealtime(2);
                report.lobbyCreated = lobby.HasValue; report.hostListening = Host;
                report.connectedClients = net != null ? net.ConnectedClientsIds.Count : 0;
                report.checkpoint=progress?.checkpointId;report.runId=progress?.run?.id;report.savedSeconds=progress?.run?.seconds??0;report.savedMembers=progress?.run?.members?.Count??0;
                if (Array.IndexOf(args,"-formal-display-smoke")>=0) {
                    OpenSettings();yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(folder,"settings-standalone.png"));
                    oldWidth=Screen.width;oldHeight=Screen.height;oldMode=Screen.fullScreenMode;
                    int expectedWidth=oldWidth,expectedHeight=oldHeight;
                    Screen.SetResolution(1600,900,FullScreenMode.Windowed);displayPending=true;displayDeadline=Time.realtimeSinceStartup+15;
                    yield return new WaitForSecondsRealtime(2);report.displayApplied=Screen.width==1600&&Screen.height==900;
                    yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(folder,"display-confirm-standalone.png"));
                    while(displayPending)yield return null;
                    yield return new WaitForSecondsRealtime(2);report.displayRestored=Screen.width==expectedWidth&&Screen.height==expectedHeight;
                    ClosePersonalMenus();
                }
                report.offlineActorsDisabled = !lab.Players[1].body && !lab.Players[2].body && !lab.Players[3].body;
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "steam-host.png"));
                yield return null;
                if (report.hostListening && report.connectedClients == 1)
                {
                    string defaults=CarryTuning.Hash(CarryTuning.Capture(lab));
                    var trial=CarryTuning.Capture(lab);
                    foreach(var value in trial.values)if(value.key=="b.walkSpeed")value.value=5.25f;
                    string clipboard=GUIUtility.systemCopyBuffer;
                    lab.GM.Open();
                    report.gmOnlineApplied=ApplyRoomConfiguration(trial,out string gmError)&&ConfigRevision==2&&lab.baseline.walkSpeed==5.25f&&Time.timeScale==1&&lab.SessionPaused;
                    try{string copied=lab.GM.CopyCurrent();report.gmClipboardRoundtrip=GUIUtility.systemCopyBuffer==copied&&CarryTuning.Hash(CarryTuning.Parse(copied))==ConfigHash;}finally{GUIUtility.systemCopyBuffer=clipboard;}
                    lab.GM.Close();
                    Leave("正在检查退出后重新建房…");
                    yield return new WaitForSecondsRealtime(2);
                    CreateRoom(); deadline = Time.unscaledTime + 30;
                    while (busy && Time.unscaledTime < deadline) yield return null;
                    yield return new WaitForSecondsRealtime(1);
                    report.restarted = Host && lobby.HasValue;
                    report.gmDefaultsRestored=ConfigRevision==1&&ConfigHash==defaults;
                }
            }
            report.error = report.hostListening ? "" : status;
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Log("smoke_complete host=" + report.hostListening + " remote_peers=0");
            Application.Quit(report.steam && report.lobbyCreated && report.hostListening && report.offlineActorsDisabled && report.restarted && report.gmAvailable && report.glassAvailable && report.gmOnlineApplied && report.gmClipboardRoundtrip && report.gmDefaultsRestored && (Array.IndexOf(args,"-formal-display-smoke")<0 || report.displayApplied&&report.displayRestored) ? 0 : 1);
        }
    }
}
