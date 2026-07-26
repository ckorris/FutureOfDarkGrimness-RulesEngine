using System;
using System.Collections.Generic;

namespace FDG
{
    /// <summary>
    /// Per-waypoint yaw facing for a movement path (#150). Each waypoint faces its direction of travel — so a
    /// model turns through the corners of a multi-point path — rotated by <paramref name="offsetRadians"/>, the
    /// player's manual rotation (0 = face straight along travel). A zero-length segment (a hold) keeps the prior
    /// direction. Shared by <see cref="PathTemplate"/> (the committed result) and the GUI movement resolver (the
    /// live ghost), so display and result agree.
    /// </summary>
    public static class MovementFacingUtilities
    {
        public static List<Float2> WaypointFacings(Position start, IReadOnlyList<Position> waypoints,
            Float2 fallbackFacing, float offsetRadians = 0f)
        {
            float cos = MathF.Cos(offsetRadians), sin = MathF.Sin(offsetRadians);
            var facings = new List<Float2>(waypoints.Count);
            Float2 dir = fallbackFacing; // base heading (direction of travel), before the manual offset
            Position from = start;
            for (int i = 0; i < waypoints.Count; i++)
            {
                float dx = waypoints[i].x - from.x, dz = waypoints[i].z - from.z;
                float len = MathF.Sqrt(dx * dx + dz * dz);
                if (len > 1e-4f) dir = new Float2(dx / len, dz / len); // else keep the prior direction (a hold)
                facings.Add(new Float2(dir.X * cos - dir.Y * sin, dir.X * sin + dir.Y * cos));
                from = waypoints[i];
            }
            return facings;
        }

        /// <summary>
        /// Per-waypoint variant (#282): each waypoint carries the manual offset it was placed with, so a
        /// rotation made later in the plan never re-orients segments already committed. Offsets pair 1:1
        /// with waypoints.
        /// </summary>
        public static List<Float2> WaypointFacings(Position start, IReadOnlyList<Position> waypoints,
            Float2 fallbackFacing, IReadOnlyList<float> offsetsRadians)
        {
            if (offsetsRadians.Count != waypoints.Count)
                throw new ArgumentException(
                    $"Expected one offset per waypoint ({waypoints.Count}), got {offsetsRadians.Count}.",
                    nameof(offsetsRadians));

            var facings = new List<Float2>(waypoints.Count);
            Float2 dir = fallbackFacing;
            Position from = start;
            for (int i = 0; i < waypoints.Count; i++)
            {
                float dx = waypoints[i].x - from.x, dz = waypoints[i].z - from.z;
                float len = MathF.Sqrt(dx * dx + dz * dz);
                if (len > 1e-4f) dir = new Float2(dx / len, dz / len); // else keep the prior direction (a hold)
                float cos = MathF.Cos(offsetsRadians[i]), sin = MathF.Sin(offsetsRadians[i]);
                facings.Add(new Float2(dir.X * cos - dir.Y * sin, dir.X * sin + dir.Y * cos));
                from = waypoints[i];
            }
            return facings;
        }
    }
}
