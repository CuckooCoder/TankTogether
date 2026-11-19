using UnityEngine;

namespace TankTogether.M0
{
    static class CarryJump
    {
        public static void BrakePlantedGrips(M0CarryLab lab)
        {
            foreach (var a in lab.Players)
            {
                if (a == null || !a.joint || !a.grounded) continue;
                Vector3 grip = lab.Tank.position + lab.Tank.rotation * a.anchor;
                Vector3 slip = Vector3.ProjectOnPlane(lab.Tank.GetPointVelocity(grip) - a.groundVelocity, Vector3.up);
                Vector3 offset = Vector3.ProjectOnPlane(a.body.position - grip, Vector3.up);
                // The planted carrier resists the pivot sliding under a neutral jump, up to one leg motor's force.
                lab.Tank.AddForceAtPosition(Vector3.ClampMagnitude((offset * 6f - slip) * lab.baseline.carryMotorGain,
                    lab.baseline.carryMotorForce), grip);
            }
        }

        // Project this step's leg impulses onto the motion allowed by the remaining planted grips.
        public static void Apply(M0CarryLab lab, int jumpMask)
        {
            Rigidbody tank = lab.Tank;
            float mass = tank.mass;
            Vector3 massMoment = Vector3.zero;
            Vector3 first = Vector3.zero, second = Vector3.zero;
            int supports = 0;
            // Work relative to the tank COM to retain precision far from the world origin.
            Vector3 comOffset = tank.rotation * tank.centerOfMass;
            foreach (var a in lab.Players)
            {
                if (a == null || !a.joint) continue;
                Vector3 grip = tank.rotation * a.anchor - comOffset;
                mass += a.body.mass;
                massMoment += a.body.mass * grip;
                if (!a.grounded) continue;
                if (supports++ == 0) first = grip;
                else second = grip;
            }
            Vector3 pivot = supports == 2 ? (first + second) * .5f : supports == 1 ? first : massMoment / mass;
            Vector3 impulse = Vector3.zero, angularImpulse = Vector3.zero;
            Quaternion principal = tank.rotation * tank.inertiaTensorRotation;
            Matrix4x4 inertia = Matrix4x4.Rotate(principal) * Matrix4x4.Scale(tank.inertiaTensor) *
                Matrix4x4.Rotate(Quaternion.Inverse(principal));
            AddPointInertia(ref inertia, tank.mass, -pivot);
            for (int i = 0; i < lab.Players.Length; i++)
            {
                var a = lab.Players[i];
                if (a == null || !a.joint) continue;
                // Player rotation is locked. Each player's whole mass translates with their grip.
                Vector3 arm = tank.rotation * a.anchor - comOffset - pivot;
                AddPointInertia(ref inertia, a.body.mass, arm);
                if ((jumpMask & (1 << i)) == 0) continue;
                Vector3 push = Vector3.up * (a.body.mass * lab.baseline.carryJumpVelocity);
                impulse += push;
                angularImpulse += Vector3.Cross(arm, push);
            }

            Vector3 axis = second - first;
            Vector3 spin;
            if (supports == 2 && axis.sqrMagnitude > .000225f)
            {
                axis.Normalize();
                spin = axis * (Vector3.Dot(axis, angularImpulse) / Vector3.Dot(axis, inertia.MultiplyVector(axis)));
            }
            else
                spin = inertia.inverse.MultiplyVector(angularImpulse);
            Vector3 travel = supports == 0 ? impulse / mass : Vector3.zero;
            tank.AddForce(travel + Vector3.Cross(spin, -pivot), ForceMode.VelocityChange);
            tank.AddTorque(spin, ForceMode.VelocityChange);
            foreach (var a in lab.Players)
            {
                if (a == null || !a.joint) continue;
                Vector3 arm = tank.rotation * a.anchor - comOffset - pivot;
                a.body.AddForce(travel + Vector3.Cross(spin, arm), ForceMode.VelocityChange);
            }
        }

        static void AddPointInertia(ref Matrix4x4 inertia, float mass, Vector3 arm)
        {
            // Parallel-axis theorem, m (|r|² I - r rᵀ).
            inertia.m00 += mass * (arm.y * arm.y + arm.z * arm.z);
            inertia.m11 += mass * (arm.x * arm.x + arm.z * arm.z);
            inertia.m22 += mass * (arm.x * arm.x + arm.y * arm.y);
            inertia.m01 -= mass * arm.x * arm.y;
            inertia.m02 -= mass * arm.x * arm.z;
            inertia.m12 -= mass * arm.y * arm.z;
            inertia.m10 = inertia.m01;
            inertia.m20 = inertia.m02;
            inertia.m21 = inertia.m12;
        }
    }
}
