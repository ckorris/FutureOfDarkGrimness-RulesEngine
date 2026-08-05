using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;

namespace FDG.Utilities
{
    /// <summary>
    /// The #206 forced-charge proximity obligation, in ONE place (#334).
    ///
    /// A non-charge move may legally END inside the standoff band — the movement validator deliberately does
    /// not reject it (see <c>MovementUtilities.ValidateMovingThroughEnemyUnits</c>). The consequence lands a
    /// stage later: a unit with a living model within
    /// <see cref="GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES"/> (base-to-base) of a living enemy model
    /// cannot Pass, so it must Charge (or reposition) rather than stand idle in an enemy's face.
    ///
    /// Two forms of the same predicate live here so they cannot drift apart:
    /// <list type="bullet">
    ///   <item><see cref="AnyEnemyWithinStandoff"/> — live table positions. This IS the gate;
    ///   <c>ChooseActionStage.GetCanPass</c> delegates to it.</item>
    ///   <item><see cref="FindContacts"/> — hypothetical poses, so a front end can show the player the
    ///   obligation WHILE the move is being aimed instead of after it is committed. It measures with the same
    ///   shape-aware 3D base-to-base call the live path uses, so a preview built from the positions a move
    ///   would produce predicts the gate exactly.</item>
    /// </list>
    /// </summary>
    public static class ForcedChargeUtilities
    {
        /// <summary>
        /// One model's base as it would sit on the table: where its centre is, its base shape, and the facing
        /// that shape is oriented by. The hypothetical counterpart to an <see cref="IModel"/>, so a resolver
        /// can measure a ghost that does not exist yet (#150 — an oriented rectangular base measures by its
        /// true footprint, so the facing is part of the pose, not decoration).
        /// </summary>
        public readonly record struct StandoffPose(Position Centre, IBaseShape BaseShape, Float2 Facing);

        /// <summary>
        /// One mover model that would end inside the band and the enemy model that puts it there, as indices
        /// into the two lists handed to <see cref="FindContacts"/>. Indices rather than models keep this
        /// engine-side type ignorant of whatever the caller is really holding (live models, ghosts, or both).
        /// </summary>
        public readonly record struct StandoffContact(int MoverIndex, int EnemyIndex, float GapInches);

        /// <summary>
        /// The single comparison behind the whole rule: a base-to-base gap strictly under the standoff
        /// distance is inside the band. Strict, matching the gate — a model at exactly 1.0" is clear.
        /// </summary>
        public static bool IsInsideStandoff(float baseToBaseGapInches) =>
            baseToBaseGapInches < GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES;

        /// <summary>
        /// Base-to-base gap between two poses, measured exactly as the live-position path measures it
        /// (<see cref="IModelExtensions.BaseDistanceToOtherModel_3D"/>): shape- and facing-aware in the
        /// horizontal plane, then combined with the vertical separation.
        /// </summary>
        public static float Gap(StandoffPose a, StandoffPose b) =>
            DistanceUtilities.GetBaseToBaseDistanceInches_3D(a.Centre, b.Centre,
                a.BaseShape, a.Facing, b.BaseShape, b.Facing);

        /// <summary>
        /// Every (mover, enemy) pair whose bases would sit inside the standoff band. All pairs, not just the
        /// closest one per mover: a front end wants to light up each enemy that is being crowded, and the
        /// panel wants to name every enemy unit involved.
        /// </summary>
        public static List<StandoffContact> FindContacts(IReadOnlyList<StandoffPose> movers,
            IReadOnlyList<StandoffPose> enemies)
        {
            var contacts = new List<StandoffContact>();
            if (movers == null || enemies == null) return contacts;

            for (int m = 0; m < movers.Count; m++)
            {
                for (int e = 0; e < enemies.Count; e++)
                {
                    float gap = Gap(movers[m], enemies[e]);
                    if (IsInsideStandoff(gap)) contacts.Add(new StandoffContact(m, e, gap));
                }
            }
            return contacts;
        }

        /// <summary>
        /// True when any non-allied unit has a living model within the standoff distance of one of this unit's
        /// living models. Allies are excluded via the activating player's team, mirroring GetCanCharge's target
        /// screening.
        ///
        /// #263: only units actually IN PLAY can force a charge — a reserve unit's models sit at the origin,
        /// and without this filter an enemy standing near the table corner was FORCED to charge a unit that
        /// wasn't on the board.
        /// </summary>
        public static bool AnyEnemyWithinStandoff(IGameContext gameContext, PlayerID activatingPlayer, IUnit unit)
        {
            TeamData? playerTeam = gameContext.GameDataStore().GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(activatingPlayer));
            IReadOnlyList<PlayerID> alliedPlayers = playerTeam != null
                ? playerTeam.Players
                : new List<PlayerID> { activatingPlayer };

            return gameContext.GameDataStore().GetAllValues<ArmyData>()
                .Where(a => !alliedPlayers.Contains(a.PlayerID))
                .SelectMany(a => a.UnitBindings)
                .Where(enemyUnit => enemyUnit.GetValue().GetIsOnBattlefield())
                .Any(enemyUnit => IsInsideStandoff(UnitCompareUtilities.MinDistanceBetweenUnits(
                    unit, enemyUnit.GetValue(), out _, out _, includeVertical: true)));
        }
    }
}
