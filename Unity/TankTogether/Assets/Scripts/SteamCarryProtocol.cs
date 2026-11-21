using System;
using System.IO;
using UnityEngine;

namespace TankTogether.Networking
{
    // Versioned application messages. Steam/NGO owns delivery; only the host owns simulation.
    public static class SteamCarryProtocol
    {
        public const string Version = "tank-together-steam-21";
        public const int MaxPacket = 1024;
        public enum Phase : byte { Waiting, Playing, Paused, Preparing, Failed, Finished }
        public struct Input
        {
            public int configRevision, round, sequence;
            public Vector3 move;
            public bool held, jump, crouch, sprint, pauseRequested, interact;
            public float sentAt;
            public int stretch;
        }
        public struct Actor
        {
            public bool active, grip, support, rejected;
            public Vector3 position;
            public float height;
        }
        public sealed class Snapshot
        {
            public int configRevision, round, tick;
            public int checkpoint, tankTeleportRevision;
            public int readyRevision = 1;
            public byte readyMask;
            public int voteRevision;
            public byte voteMask, voteYes;
            public float voteSeconds;
            public Phase phase;
            public int teamSize = SessionRoster.RequiredPlayers;
            public bool broken;
            public float liftDistance;
            public float movingBlockPhase; // Normalized host cycle [0,1), independent of stroke.
            public bool liftMoving, liftGoingUp;
            public Vector3 position, velocity;
            public Quaternion rotation;
            public Actor[] actors = new Actor[4];
        }
        static void V(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        static Vector3 V(BinaryReader r) => new Vector3(F(r), F(r), F(r));
        static float F(BinaryReader r) { float f = r.ReadSingle(); if (float.IsNaN(f) || float.IsInfinity(f)) throw new InvalidDataException("Non-finite value"); return f; }
        static byte[] Write(Action<BinaryWriter> action)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            action(writer);
            return stream.ToArray();
        }
        static BinaryReader Reader(byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > MaxPacket) throw new InvalidDataException("Packet size");
            return new BinaryReader(new MemoryStream(data));
        }
        static void End(BinaryReader r) { if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("Trailing bytes"); }
        public static byte[] Encode(Input i) => Write(w => {
            w.Write(i.configRevision); w.Write(i.round); w.Write(i.sequence); V(w, i.move);
            w.Write(i.held); w.Write(i.jump); w.Write(i.crouch); w.Write(i.sprint); w.Write(i.pauseRequested); w.Write(i.sentAt); w.Write((sbyte)i.stretch); w.Write(i.interact);
        });
        public static Input DecodeInput(byte[] data)
        {
            using var r = Reader(data);
            var i = new Input { configRevision = r.ReadInt32(), round = r.ReadInt32(), sequence = r.ReadInt32(), move = V(r), held = r.ReadBoolean(), jump = r.ReadBoolean(), crouch = r.ReadBoolean(), sprint = r.ReadBoolean(), pauseRequested = r.ReadBoolean(), sentAt = F(r), stretch=r.ReadSByte(), interact=r.ReadBoolean() };
            End(r);
            if (i.stretch < -1 || i.stretch > 1 || i.sequence < 1 || i.round < 1 || Mathf.Abs(i.move.y) > 0.001f || i.move.sqrMagnitude > 1.01f) throw new InvalidDataException("Invalid input");
            return i;
        }
        public static byte[] Encode(Snapshot s) => Write(w => {
            w.Write(s.configRevision); w.Write(s.round); w.Write(s.tick); w.Write((byte)s.phase); w.Write((byte)s.teamSize); w.Write(s.broken); V(w, s.position); V(w, s.velocity);
            w.Write(s.rotation.x); w.Write(s.rotation.y); w.Write(s.rotation.z); w.Write(s.rotation.w); w.Write(s.tankTeleportRevision);
            w.Write(s.movingBlockPhase); w.Write(s.liftDistance); w.Write(s.liftMoving); w.Write(s.liftGoingUp); w.Write((byte)s.checkpoint); w.Write(s.readyRevision); w.Write(s.readyMask); w.Write(s.voteRevision); w.Write(s.voteMask); w.Write(s.voteYes); w.Write(s.voteSeconds);
            foreach (var a in s.actors) { w.Write(a.active); w.Write(a.grip); w.Write(a.support); w.Write(a.rejected); V(w, a.position); w.Write(a.height); }
        });
        public static Snapshot DecodeSnapshot(byte[] data)
        {
            using var r = Reader(data);
            var s = new Snapshot { configRevision = r.ReadInt32(), round = r.ReadInt32(), tick = r.ReadInt32(), phase = (Phase)r.ReadByte(), teamSize = r.ReadByte(), broken = r.ReadBoolean(), position = V(r), velocity = V(r), rotation = new Quaternion(F(r), F(r), F(r), F(r)) };
            s.tankTeleportRevision = r.ReadInt32();
            s.movingBlockPhase = F(r); s.liftDistance = F(r); s.liftMoving = r.ReadBoolean(); s.liftGoingUp = r.ReadBoolean(); s.checkpoint = r.ReadByte(); s.readyRevision = r.ReadInt32(); s.readyMask = r.ReadByte(); s.voteRevision=r.ReadInt32();s.voteMask=r.ReadByte();s.voteYes=r.ReadByte();s.voteSeconds=F(r);
            if(s.voteRevision<0||s.voteMask>=(1<<SessionRoster.RequiredPlayers)||(s.voteYes & ~s.voteMask)!=0||s.voteSeconds<0||s.voteSeconds>20)throw new InvalidDataException("Invalid vote state");
            if (s.readyRevision < 1 || s.readyMask >= (1 << SessionRoster.RequiredPlayers)) throw new InvalidDataException("Invalid ready state");
            if (s.movingBlockPhase < 0 || s.movingBlockPhase >= 1f) throw new InvalidDataException("Invalid moving block phase");
            if (s.checkpoint > 5) throw new InvalidDataException("Unknown checkpoint index");
            if (s.liftDistance < 0 || s.liftDistance > 101.01f) throw new InvalidDataException("Invalid lift state");
            for (int j = 0; j < 4; j++)
            {
                s.actors[j] = new Actor { active = r.ReadBoolean(), grip = r.ReadBoolean(), support = r.ReadBoolean(), rejected = r.ReadBoolean(), position = V(r), height = F(r) };
                if (j >= SessionRoster.RequiredPlayers && s.actors[j].active) throw new InvalidDataException("Reserved player slot is active");
                if (s.actors[j].height < .5f || s.actors[j].height > 5) throw new InvalidDataException("Invalid height");
            }
            End(r);
            float q = Quaternion.Dot(s.rotation, s.rotation);
            if (s.round < 1 || s.tick < 0 || s.tankTeleportRevision < 0 || s.phase > Phase.Finished || s.teamSize < 2 || s.teamSize > SessionRoster.RequiredPlayers || q < 0.9f || q > 1.1f) throw new InvalidDataException("Invalid snapshot");
            return s;
        }
        public static bool IsNewer(int round, int tick, int previousRound, int previousTick) => round > previousRound || (round == previousRound && tick > previousTick);
        public static bool AcceptInput(Input input, int round, int lastSequence) => input.round == round && input.sequence > lastSequence;
    }
}
