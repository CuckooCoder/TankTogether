using System;
using System.Collections.Generic;

namespace TankTogether.Formal
{
    // S0 candidate: the world owner must call Resolve after EACH authoritative physics step.
    // No persistence or network acknowledgements are implied by an activation result.
    public sealed class CheckpointStepRules
    {
        public enum Outcome { None, Checkpoint, Finish, Broken }
        public readonly struct Decision
        {
            public readonly Outcome outcome;
            public readonly int station, epoch;
            public readonly long tick;
            public Decision(Outcome outcome, int station, int epoch, long tick)
            { this.outcome = outcome; this.station = station; this.epoch = epoch; this.tick = tick; }
        }
        readonly HashSet<int> activated = new HashSet<int>();
        int epoch;
        long lastTick = -1;
        bool armed, broken, finished;
        public int CurrentCheckpoint { get; private set; }

        public CheckpointStepRules(int currentCheckpoint = 0)
        { CurrentCheckpoint = currentCheckpoint; activated.Add(currentCheckpoint); BeginEpoch(); }

        // Explicit world reset, not a replay/new Run. Completed state is deliberately retained.
        public void BeginEpoch()
        { epoch++; lastTick = -1; broken = false; armed = false; }

        public Decision Resolve(long tick, IReadOnlyDictionary<int, int> contacts, bool danger, int finishStation)
        {
            if (tick <= lastTick) throw new ArgumentOutOfRangeException(nameof(tick), "Resolve each physics tick once, in order");
            lastTick = tick;
            Decision Emit(Outcome kind, int station = -1) => new Decision(kind, station, epoch, tick);
            if (finished || broken) return Emit(Outcome.None);
            // Damage is never suppressed by spawn activation suppression.
            if (danger) { broken = true; return Emit(Outcome.Broken); }
            if (!armed)
            {
                bool touching = false;
                foreach (var pair in contacts) if ((pair.Value & 15) != 0) touching = true;
                if (!touching) armed = true;
                return Emit(Outcome.None);
            }
            // Conflicting authored stations must not resolve by Dictionary insertion order.
            int candidate = int.MaxValue;
            foreach (var pair in contacts)
                if ((pair.Value & 15) == 15 && pair.Key > CurrentCheckpoint && !activated.Contains(pair.Key))
                    candidate = Math.Min(candidate, pair.Key);
            if (candidate == int.MaxValue) return Emit(Outcome.None);
            activated.Add(candidate);
            if (candidate == finishStation) { finished = true; return Emit(Outcome.Finish, candidate); }
            CurrentCheckpoint = candidate;
            return Emit(Outcome.Checkpoint, candidate);
        }
    }
}
