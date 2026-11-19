namespace TankTogether.Networking
{
    public sealed class CheckpointVote
    {
        public int Revision { get; private set; }
        public byte Voters { get; private set; }
        public byte Yes { get; private set; }
        public float Deadline { get; private set; }
        public bool Active { get; private set; }
        public bool Passed => Active && Yes == Voters;
        public bool Begin(byte voters, float now)
        {
            if (Active || voters == 0 || voters > 15 || (voters & 1) == 0) return false;
            Revision++; Voters = voters; Yes = 1; Deadline = now + 20; Active = true; return true;
        }
        public bool Agree(int slot, int revision, float now)
        {
            if (!Active || revision != Revision || now >= Deadline || slot < 1 || slot > 3 || (Voters & (1 << slot)) == 0) return false;
            Yes |= (byte)(1 << slot); return true;
        }
        public void Disconnected(int slot) { if (Active && slot > 0 && slot < 4) Yes &= (byte)~(1 << slot); }
        public void Cancel() { Active = false; Voters = Yes = 0; }
    }
}
