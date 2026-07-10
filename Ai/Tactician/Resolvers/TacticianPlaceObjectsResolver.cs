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
        /// <summary>PlaceDeferredUnitsStage's literal - Scout placement into the forward-extended
        /// zone works exactly like deployment (#191 A5-2).</summary>
        public const string ScoutTaskName = "Place Scout Unit";
        /// <summary>StartOfRoundExtraActionStage's literal - Ambush arrival over the whole table.</summary>
        public const string AmbushTaskName = "Ambush Deploy";

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
            bool deploymentShaped = request.TaskName == DeploymentTaskName
                || request.TaskName == ScoutTaskName;
            if (!deploymentShaped && request.TaskName != AmbushTaskName)
                return base.PreferredBlockCenter(request, bounds, deployIndex, maxRadius,
                    gridWidth, gridHeight, spacingX);

            List<IObjective> objectives = _tableState.Objectives.Objects.ToList();
            if (objectives.Count == 0)
                return base.PreferredBlockCenter(request, bounds, deployIndex, maxRadius,
                    gridWidth, gridHeight, spacingX);

            // Ambush arrival (#191 A5-2): the whole table is the zone and the engine enforces the
            // rule's enemy clearance (the caller spiral-searches off the aim), so aim straight at
            // the most WINNABLE objective - not ours, fewest enemies nearby, central as the tie
            // break. The unit cannot score the round it arrives; the payoff is holding the marker
            // from the next round on. Dropping beside enemy units to set up charges is a recorded
            // deferral (search-level judgment).
            if (request.TaskName == AmbushTaskName)
                return BestAmbushObjective(objectives, request).Position;

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

        // Not-ours first, then fewest living enemy models within the contest radius, then nearest
        // the table centre - all deterministic comparisons on live state.
        private IObjective BestAmbushObjective(List<IObjective> objectives, PlaceObjectsRequest<T> request)
        {
            var tableCentre = new Position(GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES / 2f,
                GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES / 2f);
            IObjective best = objectives[0];
            (int Ours, int Enemies, float CentreDistSq) bestKey = (int.MaxValue, int.MaxValue, float.MaxValue);
            foreach (IObjective objective in objectives)
            {
                (int, int, float) key = (
                    objective.OwnerID.HasValue && objective.OwnerID.Value == request.TargetPlayerID ? 1 : 0,
                    EnemiesNear(objective.Position, request.TargetPlayerID),
                    DistSq(objective.Position, tableCentre));
                if (key.CompareTo(bestKey) < 0)
                {
                    bestKey = key;
                    best = objective;
                }
            }
            return best;
        }

        private int EnemiesNear(Position point, PlayerID us)
        {
            const float contestRadiusInches = 9f;
            int count = 0;
            foreach (IUnit unit in _tableState.Units.Objects)
            {
                if (unit.PlayerID == us || !unit.GetIsOnBattlefield()) continue;
                foreach (IModel model in unit.Models)
                {
                    if (!model.GetIsAlive()) continue;
                    if (DistSq(model.Position, point) <= contestRadiusInches * contestRadiusInches)
                        count++;
                }
            }
            return count;
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
