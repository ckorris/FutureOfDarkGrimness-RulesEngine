using System;
using System.Collections.Generic;

namespace FDG
{
    /// <summary>
    /// Per-waypoint yaw facing for a movement path (#150). By default each waypoint faces its direction of
    /// travel, so a model turns through the corners of a multi-point path. A non-null manual override locks
    /// every waypoint of that model to a chosen facing instead. Shared by <see cref="PathTemplate"/> (the
    /// committed result) and the GUI movement resolver (the live ghost), so display and result agree.
    /// </summary>
    public static class MovementFacingUtilities
    {
        public static List<Float2> WaypointFacings(Position start, IReadOnlyList<Position> waypoints,
            Float2 fallbackFacing, Float2? manualOverride)
        {
            var facings = new List<Float2>(waypoints.Count);
            Float2 prev = fallbackFacing;
            Position from = start;
            for (int i = 0; i < waypoints.Count; i++)
            {
                Float2 f;
                if (manualOverride is Float2 m)
                {
                    f = m;
                }
                else
                {
                    float dx = waypoints[i].x - from.x, dz = waypoints[i].z - from.z;
                    float len = MathF.Sqrt(dx * dx + dz * dz);
                    // A zero-length segment (a hold) can't define a direction — keep the prior facing.
                    f = len > 1e-4f ? new Float2(dx / len, dz / len) : prev;
                }
                facings.Add(f);
                prev = f;
                from = waypoints[i];
            }
            return facings;
        }
    }
}
