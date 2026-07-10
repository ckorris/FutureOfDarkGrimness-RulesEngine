using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Objective-aware deployment (#191 A4b): the solo resolver's placement machinery already
    /// guarantees legality (cohesive block, in-zone, terrain/overlap/enemy clearance) and exposes
    /// exactly one strategy knob - the preferred block centre. This subclass re-aims that knob for
    /// DEPLOYMENT requests only: units spread across the objectives (nearest to the zone first)
    /// instead of marching blind lanes, melee units crowd the zone's forward edge, shooters hang
    /// back but stay in reach. Every other PlaceObjectsRequest (disembark, spillout, ambush
    /// arrival, reposition) and every fallback path keeps solo behavior via the base class.
    /// Cover-aware centre choice is deferred to a later A4b sub-slice (recorded in the ledger).
    /// </summary>
    public class TacticianPlaceObjectsResolver<T> : AiPlaceObjectsResolver<T>
    {
        /// <summary>DeployUnitStage's literal TaskName - the deployment discriminator.</summary>
        public const string DeploymentTaskName = "Place Unit Models";

        // How far behind the zone's forward edge a unit prefers to stand, by weapon reach.
        private const float LongRangeDepthInches = 6f;
        private const float MidRangeDepthInches = 3f;

        private readonly ITableState _tableState;

        public TacticianPlaceObjectsResolver(ITableState tableState) : base(tableState)
        {
            _tableState = tableState;
        }

        protected override Position PreferredBlockCenter(PlaceObjectsRequest<T> request, ZoneBounds bounds,
            int deployIndex, float maxRadius, float gridWidth, float gridHeight, float spacingX)
        {
            if (request.TaskName != DeploymentTaskName)
                return base.PreferredBlockCenter(request, bounds, deployIndex, maxRadius,
                    gridWidth, gridHeight, spacingX);

            List<IObjective> objectives = _tableState.Objectives.Objects.ToList();
            if (objectives.Count == 0)
                return base.PreferredBlockCenter(request, bounds, deployIndex, maxRadius,
                    gridWidth, gridHeight, spacingX);

            // Spread successive units across the objectives, closest to our zone first, so the army
            // fans out over what decides the game instead of over empty table.
            var zoneCenter = new Position(bounds.CenterX, bounds.CenterZ);
            objectives.Sort((a, b) =>
                DistSq(a.Position, zoneCenter).CompareTo(DistSq(b.Position, zoneCenter)));
            IObjective aim = objectives[deployIndex % objectives.Count];

            // Depth: the forward edge is the one facing the table centre (DefaultDeployFacing.Y is
            // the +-1 Z direction the zone looks toward). Melee crowds the line; shooters step back
            // without leaving reach of the mid-table.
            Float2 facing = PlacementUtilities.DefaultDeployFacing(bounds,
                GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
            float forwardZ = facing.Y >= 0f ? bounds.Top : bounds.Bottom;
            float z = forwardZ - facing.Y * DepthFor(MaxWeaponRange(request));

            // The caller clamps into the zone and spiral-searches for a clear block, so a raw aim
            // (the objective's X, our chosen depth) is safe even when the objective is off-zone.
            return new Position(aim.Position.x, z);
        }

        private static float DepthFor(float maxWeaponRange) => maxWeaponRange switch
        {
            >= 18f => LongRangeDepthInches,
            >= 12f => MidRangeDepthInches,
            _ => 0f,
        };

        private static float MaxWeaponRange(PlaceObjectsRequest<T> request)
        {
            float max = 0f;
            foreach (DataBinding<T> binding in request.ModelsToPlace)
            {
                if (binding.GetValue() is not ModelData model) continue;
                foreach (Weapon weapon in model.Weapons)
                    if (weapon.RangeInches > max) max = weapon.RangeInches;
            }
            return max;
        }

        private static float DistSq(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
