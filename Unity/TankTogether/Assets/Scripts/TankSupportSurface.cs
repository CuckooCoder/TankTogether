using UnityEngine;

namespace TankTogether.M0
{
    // Only the upward bearing face of an explicitly designated tank stand permits glass contact.
    [DisallowMultipleComponent]
    public sealed class TankSupportSurface : MonoBehaviour
    {
        public static bool BreaksGlass(Collider surface, Vector3 normal) =>
            normal.y <= .5f || surface.GetComponent<M0Danger>() ||
            (!surface.GetComponent<TankSupportSurface>() && !surface.GetComponent<M1SupportPost>());
    }
}
