using System.Collections.Generic;
using UnityEngine;

namespace TankTogether.M0
{
    // Advances the visual gait with rendered travel, independent of input and the Animator clip phase.
    sealed class CarryFootsteps
    {
        struct Foothold
        {
            public Transform surface;
            public Vector3 point, normal;
            public Quaternion rotation;
            public Vector3 Position => surface.TransformPoint(point);
            public Quaternion Rotation => surface.rotation * rotation;
        }

        sealed class Leg
        {
            public Transform thigh, shin, foot;
            public Vector3 neutral, liftOff, target;
            public Quaternion rotation, targetRotation, thighRest, shinRest, footRest;
            public Foothold contact;
            public int cycle;
            public bool locked;
            public float swingStart;
            public float Reach => Vector3.Distance(thigh.position, shin.position) + Vector3.Distance(shin.position, foot.position);
        }

        readonly Transform root, pelvis;
        readonly Vector3 restPelvis;
        readonly Leg[] legs;
        readonly RaycastHit[] hits = new RaycastHit[16];
        Vector3 previousPosition, heading;
        float phase, speed, pelvisDrop, pelvisBlend;
        bool initialized;

        public float ContactWeight { get; private set; }

        public CarryFootsteps(Transform root, Dictionary<string, Transform> bones)
        {
            this.root = root;
            previousPosition = root.position;
            if (!bones.TryGetValue("Pelvis", out pelvis)) return;
            var left = FindLeg(bones, "L");
            var right = FindLeg(bones, "R");
            if (left == null || right == null) return;
            restPelvis = pelvis.localPosition;
            legs = new[] { left, right };
        }

        Leg FindLeg(Dictionary<string, Transform> bones, string side)
        {
            if (!bones.TryGetValue("Thigh." + side, out var thigh) ||
                !bones.TryGetValue("Shin." + side, out var shin) ||
                !bones.TryGetValue("Foot." + side, out var foot)) return null;
            return new Leg
            {
                thigh = thigh, shin = shin, foot = foot,
                neutral = root.InverseTransformPoint(foot.position),
                rotation = Quaternion.Inverse(root.rotation) * foot.rotation,
                thighRest = thigh.localRotation, shinRest = shin.localRotation, footRest = foot.localRotation
            };
        }

        public void Apply(M0CarryLab lab, bool grounded, Vector3 groundVelocity, float dt, bool teleport)
        {
            Vector3 travel = Vector3.ProjectOnPlane(root.position - previousPosition - groundVelocity * dt, Vector3.up);
            previousPosition = root.position;
            ContactWeight = 0;
            if (legs == null || dt <= 0) return;
            if (teleport)
            {
                initialized = false;
                pelvisDrop = pelvisBlend = 0;
                return;
            }

            float reach = Mathf.Min(legs[0].Reach, legs[1].Reach);
            var support = grounded ? FindGround(lab, legs[0], root.position) : default;
            pelvisBlend = Mathf.MoveTowards(pelvisBlend, grounded ? .75f : 0, 12 * dt);
            pelvis.localPosition = Vector3.Lerp(pelvis.localPosition, restPelvis, pelvisBlend);
            // Fit the standing reach to the measured floor, independently of the capsule's bottom radius.
            float hipHeight = (legs[0].thigh.position.y + legs[1].thigh.position.y) * .5f;
            float drop = support.surface
                ? Mathf.Clamp(hipHeight - support.Position.y - reach * .944f, -reach * .18f, reach * .50f)
                : 0;
            pelvisDrop = Mathf.MoveTowards(pelvisDrop, drop, reach * 2f * dt);
            pelvis.position -= Vector3.up * pelvisDrop;
            if (!grounded)
            {
                initialized = false;
                return;
            }
            if (!initialized)
            {
                phase = speed = 0;
                heading = root.forward;
                for (int i = 0; i < legs.Length; i++)
                {
                    var leg = legs[i];
                    leg.cycle = 0;
                    leg.target = root.TransformPoint(leg.neutral);
                    leg.targetRotation = root.rotation * leg.rotation;
                    Plant(leg, FindReachableGround(lab, leg, leg.target, leg.target), 0);
                    leg.liftOff = leg.target;
                }
                initialized = true;
            }

            float distance = travel.magnitude;
            float measuredSpeed = distance / dt;
            bool moving = measuredSpeed > .07f;
            float response = 1 - Mathf.Exp(-17 * dt);
            speed = Mathf.Lerp(speed, measuredSpeed, response);
            if (moving) heading = Vector3.Slerp(heading, travel / distance, response);
            float stride = reach * Mathf.Lerp(1.65f, 6.5f, Mathf.InverseLerp(.35f, 7, speed));
            float stance = Mathf.Min(.62f, reach * .62f / stride);
            if (moving) phase += distance / stride;

            for (int i = 0; i < legs.Length; i++)
            {
                var leg = legs[i];
                Vector3 neutral = root.TransformPoint(leg.neutral);
                if (moving)
                {
                    float cycle = phase + i * .5f;
                    int index = Mathf.FloorToInt(cycle);
                    float step = cycle - index;
                    if (index != leg.cycle)
                    {
                        leg.cycle = index;
                        Vector3 preferred = neutral + heading * ((stance * .5f - step) * stride);
                        Plant(leg, FindReachableGround(lab, leg, preferred, neutral), step);
                    }
                    if (leg.locked && (step >= stance || !leg.contact.surface || !CanReach(leg, leg.contact.Position)))
                    {
                        leg.liftOff = leg.target;
                        leg.swingStart = step;
                        leg.locked = false;
                    }
                    if (!leg.locked)
                    {
                        float swing = Mathf.InverseLerp(leg.swingStart, 1, step);
                        Vector3 landing = neutral + heading * ((1 - step + stance * .5f) * stride);
                        Vector3 target = Vector3.Lerp(leg.liftOff, landing, Mathf.SmoothStep(0, 1, swing));
                        // Keep the ground probe under the leg, including during sharp turns or a long recovery step.
                        target = LimitTarget(leg, target);
                        var ground = FindGround(lab, leg, target);
                        if (ground.surface) target.y = ground.Position.y;
                        target.y += Mathf.Sin(swing * Mathf.PI) * Mathf.Min(.018f, reach * .08f);
                        leg.target = target;
                        leg.targetRotation = Quaternion.Slerp(leg.targetRotation,
                            ground.surface ? ground.Rotation : root.rotation * leg.rotation, response);
                    }
                }
                else if (!leg.locked)
                {
                    var landing = FindReachableGround(lab, leg, neutral, neutral);
                    if (landing.surface)
                    {
                        leg.target = Vector3.MoveTowards(leg.target, landing.Position, 2.4f * dt);
                        if (Vector3.Distance(leg.target, landing.Position) < .005f) Plant(leg, landing, 0);
                    }
                }

                if (leg.locked && leg.contact.surface)
                {
                    leg.target = leg.contact.Position;
                    leg.targetRotation = leg.contact.Rotation;
                    ContactWeight = 1;
                }
            }

            foreach (var leg in legs)
            {
                // Applied over any clip using the same bend plane and authored bone lengths each frame.
                leg.thigh.localRotation = leg.thighRest;
                leg.shin.localRotation = leg.shinRest;
                leg.target = LimitTarget(leg, leg.target);
                CarryProceduralPose.Solve(root, leg.thigh, leg.shin, leg.foot, leg.target, 1);
                // Keep the foot aligned with the shin, allowing a small correction for the ground.
                var localContact = Quaternion.Inverse(leg.shin.rotation) * leg.targetRotation;
                leg.foot.localRotation = Quaternion.RotateTowards(leg.footRest, localContact, 10);
            }
        }

        Vector3 LimitTarget(Leg leg, Vector3 target)
        {
            Vector3 offset = root.InverseTransformDirection(target - leg.thigh.position);
            float thigh = Vector3.Distance(leg.thigh.position, leg.shin.position);
            float shin = Vector3.Distance(leg.shin.position, leg.foot.position);
            float reach = thigh + shin;
            offset.x = Mathf.Clamp(offset.x, -reach * .16f, reach * .16f);
            offset.y = Mathf.Min(offset.y, -reach * .68f);
            offset.z = Mathf.Clamp(offset.z, -reach * .41f, reach * .41f);
            // Limit flexion so the broad mesh around the knee and ankle retains its silhouette.
            const float MaxKneeFlexion = 44;
            float minimum = Mathf.Sqrt(thigh * thigh + shin * shin +
                2 * thigh * shin * Mathf.Cos(MaxKneeFlexion * Mathf.Deg2Rad));
            return leg.thigh.position + root.TransformDirection(offset.normalized * Mathf.Clamp(offset.magnitude, minimum, reach * .996f));
        }

        bool CanReach(Leg leg, Vector3 position) => (LimitTarget(leg, position) - position).sqrMagnitude <= .000025f;

        Foothold FindReachableGround(M0CarryLab lab, Leg leg, Vector3 preferred, Vector3 neutral)
        {
            var hit = FindGround(lab, leg, preferred);
            if (hit.surface && CanReach(leg, hit.Position)) return hit;
            hit = FindGround(lab, leg, neutral);
            if (!hit.surface || CanReach(leg, hit.Position)) return hit;
            // Probe close points on the ramp when the nominal foothold is outside the leg's range.
            Vector3 downhill = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
            if (downhill.sqrMagnitude < .04f) return default;
            Vector3 step = downhill.normalized * (leg.Reach * .12f);
            for (int i = 1; i <= 2; i++)
            {
                hit = FindGround(lab, leg, neutral - i * step);
                if (hit.surface && CanReach(leg, hit.Position)) return hit;
                hit = FindGround(lab, leg, neutral + i * step);
                if (hit.surface && CanReach(leg, hit.Position)) return hit;
            }
            return default;
        }

        static void Plant(Leg leg, Foothold ground, float step)
        {
            leg.contact = ground;
            leg.locked = ground.surface;
            leg.liftOff = leg.target;
            leg.swingStart = step;
        }

        Foothold FindGround(M0CarryLab lab, Leg leg, Vector3 near)
        {
            int count = Physics.RaycastNonAlloc(near + Vector3.up * .55f, Vector3.down, hits, 1.25f, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit best = default;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.normal.y >= .55f && lab.IsEnvironment(hit.collider) && (!best.collider || hit.distance < best.distance))
                    best = hit;
            }
            if (!best.collider) return default;
            var surface = best.collider.transform;
            float sole = Mathf.Max(.02f, leg.neutral.y * root.lossyScale.y);
            var rotation = Quaternion.FromToRotation(Vector3.up, best.normal) * root.rotation * leg.rotation;
            return new Foothold
            {
                surface = surface, point = surface.InverseTransformPoint(best.point + Vector3.up * sole), normal = best.normal,
                rotation = Quaternion.Inverse(surface.rotation) * rotation
            };
        }
    }
}
