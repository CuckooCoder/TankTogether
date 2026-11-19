using System.Collections.Generic;
using UnityEngine;

namespace TankTogether.Formal
{
    // Isolated S0 collector. Not attached to the shipping route yet.
    // Registers authored physical colliders, never trigger volumes or historical touch counts.
    public sealed class CheckpointContactProbe : MonoBehaviour
    {
        readonly Dictionary<Collider, (int station, int post)> posts = new Dictionary<Collider, (int, int)>();
        readonly HashSet<Collider> dangerous = new HashSet<Collider>();
        readonly HashSet<Collider> touching = new HashSet<Collider>();
        readonly Dictionary<int, int> mask = new Dictionary<int, int>();
        public bool Danger { get; private set; }
        public void RegisterPost(Collider collider, int station, int post)
        {
            if (!collider || collider.isTrigger || post < 0 || post > 3) throw new System.ArgumentException("Physical post required");
            posts.Add(collider, (station, post));
        }
        public void RegisterDanger(Collider collider) => dangerous.Add(collider);
        public IReadOnlyDictionary<int, int> ReadContacts()
        {
            mask.Clear(); Danger = false;
            // Sleeping contacts persist. Disabled/destroyed colliders cannot supply ghost support.
            touching.RemoveWhere(c => !c || !c.enabled || !c.gameObject.activeInHierarchy);
            foreach (var collider in touching)
            {
                if (dangerous.Contains(collider)) Danger = true;
                if (posts.TryGetValue(collider, out var p))
                { mask.TryGetValue(p.station, out int value); mask[p.station] = value | (1 << p.post); }
            }
            return mask;
        }
        void OnCollisionEnter(Collision c) { if (c.contactCount > 0) touching.Add(c.collider); }
        void OnCollisionStay(Collision c) { if (c.contactCount > 0) touching.Add(c.collider); else touching.Remove(c.collider); }
        void OnCollisionExit(Collision c) => touching.Remove(c.collider);
        void OnDisable() { touching.Clear(); mask.Clear(); Danger = false; }
    }
}
