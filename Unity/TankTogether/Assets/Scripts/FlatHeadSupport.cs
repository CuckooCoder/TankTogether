using UnityEngine;
namespace TankTogether.M0
{
    // Finite, world-aligned contact footprint inside the upright locomotion capsule.
    // Applies the flat-head support forces without adding another collider.
    public static class FlatHeadSupport
    {
        public const int SampleCount = 4;
        public static Vector3 Point(Vector3 head, float diameter, float ratio, int sample)
        {
            float half = diameter * Mathf.Clamp(ratio, .3f, .7f) * .5f;
            return head + new Vector3((sample & 1) == 0 ? -half : half, 0, (sample & 2) == 0 ? -half : half);
        }
        public static bool Bottom(Rigidbody tank, Vector3 size, Vector3 headPoint, out Vector3 bottom, out float compression)
        {
            bottom = headPoint; compression = 0;
            Quaternion inverse = Quaternion.Inverse(tank.rotation);
            Vector3 local = inverse * (headPoint - tank.position), up = inverse * Vector3.up;
            if (up.y <= .3f) return false;
            float distance = (-size.y * .5f - local.y) / up.y;
            Vector3 hit = local + up * distance;
            if (Mathf.Abs(hit.x) >= size.x * .5f - .1f || Mathf.Abs(hit.z) >= size.z * .5f - .1f) return false;
            bottom = headPoint + Vector3.up * distance; compression = -distance;
            return true;
        }
        public static float Apply(M0CarryLab lab, M0CarryLab.Actor actor)
        {
            var b = lab.baseline; float total = 0;
            Vector3 head = actor.body.position + Vector3.up * actor.height;
            for (int n = 0; n < SampleCount; n++)
            {
                Vector3 p = Point(head, b.playerDiameter, b.headSupportWidthRatio, n);
                if (!Bottom(lab.Tank, lab.tankSize, p, out var bottom, out float compression) || compression < -.01f || compression >= .25f) continue;
                Vector3 relative = actor.body.GetPointVelocity(p) - lab.Tank.GetPointVelocity(bottom);
                // Each sample owns ONE QUARTER of stiffness, damping and cap. Missing contact never redistributes its force.
                float normal = Mathf.Clamp(Mathf.Max(0, compression) * b.supportStiffness + relative.y * b.supportDamping, 0, b.supportMaxForce) / SampleCount;
                Vector3 friction = actor.joint ? Vector3.zero : Vector3.ClampMagnitude(Vector3.ProjectOnPlane(relative, Vector3.up) * (20f / SampleCount), normal * b.headSupportFriction);
                Vector3 force = Vector3.up * normal + friction;
                lab.Tank.AddForceAtPosition(force, bottom);
                actor.body.AddForceAtPosition(-force, p);
                total += normal;
            }
            return total;
        }
        public static bool LowestContact(M0CarryLab lab, M0CarryLab.Actor actor, out float bottomY)
        {
            bottomY = float.PositiveInfinity; bool found = false;
            Vector3 head = actor.body.position + Vector3.up * actor.height;
            for (int n = 0; n < SampleCount; n++)
            {
                var p = Point(head, lab.baseline.playerDiameter, lab.baseline.headSupportWidthRatio, n);
                if (Bottom(lab.Tank, lab.tankSize, p, out var bottom, out float compression) && Mathf.Abs(compression) < .25f)
                { bottomY = Mathf.Min(bottomY, bottom.y); found = true; }
            }
            return found;
        }
    }
}
