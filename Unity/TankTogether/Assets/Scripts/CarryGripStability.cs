using UnityEngine;

namespace TankTogether.M0
{
    // Point and line support allow tipping about the existing grips.
    sealed class CarryGripStability
    {
        const float LineTolerance = .015f;
        readonly Vector3[] points = new Vector3[3];

        public bool Evaluate(M0CarryLab lab, out Vector3 torque, out Vector3 downhill)
        {
            torque = Vector3.zero;
            downhill = Vector3.zero;
            var tank = lab.Tank;
            Quaternion worldToTank = Quaternion.Inverse(tank.rotation);
            int count = 0;
            foreach (var a in lab.Players)
            {
                if (a == null || !a.active || !a.body || (!a.joint && !(a.grounded && a.support))) continue;
                Vector3 point = a.joint ? a.anchor : worldToTank * (a.body.position + Vector3.up * a.height - tank.position);
                point.y = -lab.tankSize.y * .5f;
                points[count++] = point;
            }
            if (count == 0) return false;

            // Work on the tank's underside so yaw, pitch and slot ordering cannot change collinearity.
            Vector3 line = Vector3.zero;
            for (int i = 1; i < count; i++)
                for (int j = 0; j < i; j++)
                {
                    Vector3 edge = points[i] - points[j];
                    if (edge.sqrMagnitude > line.sqrMagnitude) line = edge;
                }
            if (count == 3 && Vector3.Cross(points[1] - points[0], points[2] - points[0]).sqrMagnitude
                > line.sqrMagnitude * LineTolerance * LineTolerance) return false;

            Vector3 support = tank.position + tank.rotation * points[0];
            Vector3 gravityArm = Vector3.Cross(tank.worldCenterOfMass - support, Physics.gravity.normalized);
            Vector3 axis;
            if (line.sqrMagnitude > LineTolerance * LineTolerance)
            {
                // Choose the same direction along the line when two players exchange slots.
                if ((Mathf.Abs(line.x) >= Mathf.Abs(line.z) ? line.x : line.z) < 0) line = -line;
                axis = tank.rotation * line.normalized;
            }
            else
                axis = gravityArm.sqrMagnitude > .000001f ? gravityArm.normalized : tank.rotation * Vector3.forward;

            // Gravity already supplies most of the torque. Seed only the missing lever near perfect balance.
            float gravityLever = Vector3.Dot(gravityArm, axis);
            float sign = Mathf.Abs(gravityLever) > .001f ? Mathf.Sign(gravityLever) : 1f;
            downhill = Vector3.Cross(worldToTank * (axis * sign), Vector3.up).normalized;
            float assistLever = Mathf.Max(0, lab.baseline.playerDiameter * .3f - Mathf.Abs(gravityLever));
            torque = axis * (sign * assistLever * tank.mass * Physics.gravity.magnitude);
            return true;
        }

        internal static float MaxSupportHeight(M0CarryLab lab, M0CarryLab.Actor actor, Vector3 downhill)
        {
            Vector3 grip = actor.joint ? actor.anchor : lab.Tank.transform.InverseTransformPoint(actor.body.position + Vector3.up * actor.height);
            Vector3 half = lab.tankSize * .5f;
            float edge = Mathf.Abs(downhill.x) * half.x + Mathf.Abs(downhill.z) * half.z;
            float reach = edge - Vector3.Dot(grip, downhill);
            return Mathf.Max(lab.baseline.playerDiameter, reach * Mathf.Sin(80f * Mathf.Deg2Rad) - lab.baseline.gripLimit - .02f);
        }
    }
}
