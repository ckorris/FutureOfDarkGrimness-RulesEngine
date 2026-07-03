using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Foundation;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #100 #2 — resolves an ability's <see cref="TargetSelector"/> into the units the acting unit may
    /// pick: applies the affinity, range, line-of-sight, and required-token filters. This is the first code
    /// to actually READ a TargetSelector (the record was inert before #2). <see cref="PreAttackStage"/>
    /// uses it both to decide whether an ability has enough valid targets to offer and to populate the
    /// target-selection request.
    /// </summary>
    public static class PreAttackTargeting
    {
        /// <summary>
        /// The units <paramref name="actingUnit"/> may pick for an ability with <paramref name="selector"/>.
        /// Self short-circuits to the bearer. Friend/Foe/Any scan armies by allegiance, then filter by
        /// closest-model range, an optional line-of-sight requirement, and an optional required token.
        /// Off-battlefield units (reserves / embarked / wiped out) are never candidates.
        /// </summary>
        public static List<DataBinding<UnitData>> EligibleTargets(
            DataBinding<UnitData> actingUnit, TargetSelector selector, IGameContext gameContext)
        {
            if (selector.TargetAffinity == ETargetAffinity.Self)
            {
                return new List<DataBinding<UnitData>> { actingUnit };
            }

            PlayerID actingPlayer = actingUnit.PlayerID();
            IReadOnlyList<PlayerID> allied = AlliedPlayers(actingPlayer, gameContext);
            IReadOnlyList<ITerrain>? terrain = selector.RequireLineOfSight
                ? gameContext.TableState.Terrain.Objects.ToList()
                : null;

            List<DataBinding<UnitData>> eligible = new List<DataBinding<UnitData>>();

            foreach (ArmyData army in gameContext.GameDataStore.GetAllValues<ArmyData>())
            {
                if (!AffinityMatches(selector.TargetAffinity, allied.Contains(army.PlayerID)))
                {
                    continue;
                }

                foreach (DataBinding<UnitData> candidateBinding in army.UnitBindings)
                {
                    UnitData candidate = candidateBinding.GetValue();

                    if (!candidate.GetIsOnBattlefield())
                    {
                        continue;
                    }

                    float distance = UnitCompareUtilities.MinDistanceBetweenUnits(actingUnit.GetValue(),
                        candidate, out IModel? nearActing, out IModel? nearCandidate, includeVertical: false);
                    if (distance > selector.RangeInches)
                    {
                        continue;
                    }

                    if (selector.RequiredToken != null
                        && !candidate.Tokens.HasToken(selector.RequiredToken.Value))
                    {
                        continue;
                    }

                    if (selector.RequiredRule != null
                        && !candidate.RuleDefinitions.Any(r =>
                            string.Equals(r.Definition.Name, selector.RequiredRule, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(r.RequestedName, selector.RequiredRule, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (selector.RequireLineOfSight && nearActing != null && nearCandidate != null
                        && !LineOfSightUtilities.HasLineOfSight(nearActing.Position, nearCandidate.Position, terrain))
                    {
                        continue;
                    }

                    eligible.Add(candidateBinding);
                }
            }

            return eligible;
        }

        private static bool AffinityMatches(ETargetAffinity affinity, bool isAllied) => affinity switch
        {
            ETargetAffinity.Friend => isAllied,
            ETargetAffinity.Foe => !isAllied,
            ETargetAffinity.Any => true,
            _ => false, // Self is handled by the caller before we get here.
        };

        private static IReadOnlyList<PlayerID> AlliedPlayers(PlayerID player, IGameContext gameContext)
        {
            TeamData? team = gameContext.GameDataStore.GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(player));
            return team?.Players ?? new List<PlayerID> { player };
        }
    }
}
