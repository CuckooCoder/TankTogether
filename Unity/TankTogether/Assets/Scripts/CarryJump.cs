using UnityEngine;

namespace TankTogether.M0
{
    sealed class CarryJump
    {
        readonly float[] apex = new float[3], deadline = new float[3], correction = new float[3];
        readonly Vector3[] arms = new Vector3[3], response = new Vector3[3];
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
        public void Apply(M0CarryLab lab, int jumpMask)
        {
            Rigidbody tank = lab.Tank;
            float gravity = -Physics.gravity.y;
            float jumpHeight = lab.baseline.freeJumpHeight;
            float launchSpeed = Mathf.Sqrt(2 * gravity * jumpHeight);
            int assist = 0;
            for (int i = 0; i < lab.LocalPlayerCount; i++)
            {
                if (!lab.IsGripping(i) || !lab.Players[i].carryJumpAirborne) continue;
                Vector3 grip = tank.position + tank.rotation * lab.Players[i].anchor;
                bool takeoff = (jumpMask & (1 << i)) != 0;
                if (takeoff)
                {
                    apex[i] = grip.y + jumpHeight;
                    deadline[i] = Time.fixedTime + launchSpeed / gravity + Time.fixedDeltaTime;
                }
                float speed = tank.GetPointVelocity(grip).y;
                if (Time.fixedTime > deadline[i] || (!takeoff && speed <= 0)) continue;
                float target = Mathf.Sqrt(2 * gravity * Mathf.Max(0, apex[i] - grip.y)) + gravity * Time.fixedDeltaTime * .5f;
                correction[i] = Mathf.Clamp(target - speed, takeoff ? -launchSpeed : -.30f, takeoff ? 2 * launchSpeed : .40f);
                assist |= 1 << i;
            }
            if (assist == 0) return;
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
                arms[i] = arm;
            }

            Vector3 axis = (second - first).normalized;
            float axisInertia = Vector3.Dot(axis, inertia.MultiplyVector(axis));
            float liftPerImpulse = supports == 0 ? 1 / mass : 0;
            Matrix4x4 inverse = inertia.inverse;
            for (int i = 0; i < lab.LocalPlayerCount; i++)
            {
                if ((assist & (1 << i)) == 0) continue;
                Vector3 lever = Vector3.Cross(arms[i], Vector3.up);
                response[i] = supports < 2 ? inverse.MultiplyVector(lever) : axisInertia > .001f
                    ? axis * (Vector3.Dot(lever, axis) / axisInertia) : Vector3.zero;
            }

            // Solve all active grip speeds together while retaining the planted pivot.
            float lift = 0;
            Vector3 spin = Vector3.zero;
            for (int pass = 0; pass < 8; pass++)
            for (int i = 0; i < lab.LocalPlayerCount; i++)
            {
                if ((assist & (1 << i)) == 0) continue;
                float gain = liftPerImpulse + Vector3.Cross(response[i], arms[i]).y;
                if (gain < .0001f) continue;
                float residual = correction[i] - lift - Vector3.Cross(spin, arms[i]).y;
                float impulse = Mathf.Clamp(residual / gain, -mass * launchSpeed, mass * launchSpeed);
                lift += impulse * liftPerImpulse;
                spin += response[i] * impulse;
            }
            spin = Vector3.ClampMagnitude(spin, jumpMask == 0 ? .45f : tank.maxAngularVelocity);
            Vector3 travel = Vector3.up * Mathf.Clamp(lift, -launchSpeed, 2 * launchSpeed);
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
