using FDG.Data;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// The one cover-majority computation: strictly more than half of the defending unit's LIVING
    /// models seen through Cover (by any living attacker model) earns the +1 defense bonus.
    ///
    /// <para>Shared by <see cref="CoverCheckStage"/> (the roll) and the targeting stage's cover
    /// flag (<c>ChooseRangedAttackStage</c>, the preview) so the two can never disagree (#385) -
    /// a preview that re-derives the test drifts from the rule that enforces it. Before this
    /// helper the two had drifted exactly once: the preview filtered dead models and the roll did
    /// not, so after mid-activation casualties the panel and the dice disagreed about cover.</para>
    ///
    /// <para>Dead models are excluded on BOTH sides (#158): only living defenders can benefit
    /// from cover, and only living attackers have sight lines - a squad whose casualties happened
    /// to die behind a wall must not grant the survivors standing in the open a cover bonus.</para>
    /// </summary>
    public static class CoverMajority
    {
        public readonly record struct Result(int ModelsInCover, int LivingDefenders)
        {
            /// <summary>Majority rule: strictly more than half of the living defenders in cover.</summary>
            public bool HasCover => ModelsInCover * 2 > LivingDefenders;
        }

        /// <param name="terrain">The full sight-blocker list: table terrain concatenated with
        /// <see cref="LineOfSightUtilities.BuildModelBlockers"/> for this (attacker, defender) pair.</param>
        /// <param name="applyProximityExceptions">The #201 house rule
        /// (<see cref="GameSettings.CoverProximityExceptionsEnabled"/>) - see <see cref="CoverProximityRules"/>.</param>
        /// <param name="sightBlockers">#425: what Sight Blockers do to THIS shot - <see cref="ESightBlockerCover"/>,
        /// derived per weapon by <c>SightRuleQueries.SightBlockerCoverFor</c>. The default is the ordinary shot.</param>
        public static Result Evaluate(DataBinding<UnitData> attackingUnit,
            DataBinding<UnitData> defendingUnit, IReadOnlyList<ITerrain> terrain,
            bool applyProximityExceptions, ESightBlockerCover sightBlockers = ESightBlockerCover.BlocksSight)
        {
            List<DataBinding<ModelData>> attackers = attackingUnit.ModelBindings()
                .Where(model => model.GetIsAlive()).ToList();
            List<DataBinding<ModelData>> defenders = defendingUnit.ModelBindings()
                .Where(model => model.GetIsAlive()).ToList();

            int modelsInCover = 0;
            foreach (DataBinding<ModelData> defender in defenders)
            {
                ModelData defModel = defender.GetValue();
                Position defPos = defModel.PositionBinding.GetValue();
                foreach (DataBinding<ModelData> attacker in attackers)
                {
                    ModelData atkModel = attacker.GetValue();
                    bool inCover = sightBlockers == ESightBlockerCover.BlocksSight
                        ? LineOfSightUtilities.EvaluateSightLine(
                            atkModel.PositionBinding.GetValue(), defPos, terrain,
                            CoverContext.ForModels(atkModel, defModel), applyProximityExceptions)
                            == ESightLineEffect.Cover
                        : CoveredPastSightBlockers(atkModel.PositionBinding.GetValue(), defPos, terrain,
                            CoverContext.ForModels(atkModel, defModel), applyProximityExceptions,
                            blockersGrantCover: sightBlockers == ESightBlockerCover.GrantsCover);
                    if (inCover)
                    {
                        modelsInCover++;
                        break;
                    }
                }
            }

            return new Result(modelsInCover, defenders.Count);
        }

        /// <summary>
        /// #425: one sight line's cover verdict for a shot that ignores line of sight. A Blocking piece does not
        /// stop the line (the shot is "as if in line of sight"), so it can no longer MASK Cover terrain behind or
        /// in front of it - every piece on the line is read: Cover terrain counts (#201 proximity voiding
        /// included; a piece flagged both Blocking and Cover counts as Cover terrain), and a Sight Blocker counts
        /// only when <paramref name="blockersGrantCover"/> (the shot keeps "Cover from Sight Blockers"). The
        /// proximity exceptions are a Cover-terrain house rule and are not applied to Sight Blockers.
        /// </summary>
        private static bool CoveredPastSightBlockers(Position attacker, Position target, IReadOnlyList<ITerrain> terrain,
            in CoverContext coverContext, bool applyProximityExceptions, bool blockersGrantCover)
        {
            bool behindBlocker = false;
            foreach (ITerrain piece in terrain)
            {
                ESightLineEffect effect = piece.EvaluateSightLine(attacker, target);
                if (effect == ESightLineEffect.Clear) continue;

                bool coverTerrain = effect == ESightLineEffect.Cover
                    || piece.TerrainType.HasFlag(ETerrainType.Cover);
                if (coverTerrain && !(applyProximityExceptions && CoverProximityRules.VoidsCover(piece, coverContext)))
                {
                    return true;
                }
                if (effect == ESightLineEffect.Blocking) behindBlocker = true;
            }
            return blockersGrantCover && behindBlocker;
        }
    }
}
