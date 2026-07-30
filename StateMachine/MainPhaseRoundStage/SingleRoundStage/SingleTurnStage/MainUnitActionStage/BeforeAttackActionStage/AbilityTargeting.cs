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
    /// to actually READ a TargetSelector (the record was inert before #2). <see cref="BeforeAttackActionStage"/>
    /// uses it both to decide whether an ability has enough valid targets to offer and to populate the
    /// target-selection request.
    /// </summary>
    public static class AbilityTargeting
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

            IReadOnlyList<IUnit> relays = BuffRelaysInReach(actingUnit, selector, allied, gameContext);

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

                    // #197 Extended Buff Range: a candidate out of the ability's own reach may still be
                    // picked "as if the user were in the relay's position" - the relay relaxes RANGE only;
                    // every other filter below applies to the candidate unchanged.
                    float distance = UnitCompareUtilities.MinDistanceBetweenUnits(actingUnit.GetValue(),
                        candidate, out IModel? nearActing, out IModel? nearCandidate, includeVertical: false);
                    if (distance > selector.RangeInches
                        && !ReachableViaRelay(relays, candidate, selector.RangeInches))
                    {
                        continue;
                    }

                    if (selector.RequiredToken != null
                        && !candidate.Tokens.HasToken(selector.RequiredToken.Value))
                    {
                        continue;
                    }

                    // The candidate qualifies if IT or any of its MODELS carries the rule. A joined hero
                    // keeps its own rules on its model (#006/#093) - Caster is the common case, and the
                    // round-start token grant already scans models the same way - so a unit-only scan would
                    // make "pick an enemy with Caster" (#197 P6's Casting Debuff) blind to every hero caster.
                    if (selector.RequiredRule != null && !HasRule(candidate, selector.RequiredRule))
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

        /// <summary>
        /// #197 Extended Buff Range: the OTHER friendly units whose <see cref="Rules.Definitions.RuleOperation.EnableBuffRelay"/>
        /// offer reaches the acting unit right now - each is a position the pick may be measured from.
        /// Only a FRIENDLY pick can be relayed (the rule relays "buffs"), and only a sight-free one: a
        /// relay lends position, not eyes, and no corpus Friend-pick requires line of sight, so the
        /// combination is gated out rather than guessed at (grow on demand).
        /// </summary>
        private static IReadOnlyList<IUnit> BuffRelaysInReach(DataBinding<UnitData> actingUnit,
            TargetSelector selector, IReadOnlyList<PlayerID> allied, IGameContext gameContext)
        {
            if (selector.TargetAffinity != ETargetAffinity.Friend || selector.RequireLineOfSight)
            {
                return Array.Empty<IUnit>();
            }

            List<IUnit>? relays = null;
            UnitData actor = actingUnit.GetValue();

            foreach (ArmyData army in gameContext.GameDataStore.GetAllValues<ArmyData>())
            {
                if (!allied.Contains(army.PlayerID))
                {
                    continue;
                }

                foreach (DataBinding<UnitData> binding in army.UnitBindings)
                {
                    UnitData other = binding.GetValue();

                    // "Another friendly unit": never the acting unit itself, and never one off the table.
                    if (ReferenceEquals(other, actor) || other.ID.Equals(actor.ID)) continue;
                    if (!other.GetIsAlive() || !other.GetIsOnBattlefield()) continue;

                    float distance = UnitCompareUtilities.MinDistanceBetweenUnits(actor, other,
                        out _, out _, includeVertical: false);

                    foreach (Rules.Definitions.RuleOperation.EnableBuffRelay offer in
                             Rules.Dispatch.CapabilityRuleQueries.BuffRelayOffers(other, gameContext.RuleEvaluator))
                    {
                        if (distance <= offer.RangeInches)
                        {
                            (relays ??= new List<IUnit>()).Add(other);
                            break;
                        }
                    }
                }
            }

            return (IReadOnlyList<IUnit>?)relays ?? Array.Empty<IUnit>();
        }

        private static bool ReachableViaRelay(IReadOnlyList<IUnit> relays, UnitData candidate,
            float rangeInches)
        {
            foreach (IUnit relay in relays)
            {
                if (UnitCompareUtilities.MinDistanceBetweenUnits(relay, candidate,
                        out _, out _, includeVertical: false) <= rangeInches)
                {
                    return true;
                }
            }

            return false;
        }

        // Matches on the canonical name OR the name the book asked for, so a book alias ("Wizard" for
        // Caster) still satisfies the filter - the same pair the rule-resolution path compares.
        private static bool HasRule(IUnit unit, string ruleName)
        {
            bool Matches(IEnumerable<Rules.Dispatch.ResolvedRule> rules) => rules.Any(r =>
                string.Equals(r.Definition.Name, ruleName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.RequestedName, ruleName, StringComparison.OrdinalIgnoreCase));

            return Matches(unit.RuleDefinitions) || unit.Models.Any(m => Matches(m.RuleDefinitions));
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
