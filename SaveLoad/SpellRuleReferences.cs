using FDG.Rules.Definitions;
using System.Collections.Generic;

namespace FDG.SaveLoad
{
    /// <summary>
    /// Enumerates the rule names a spell effect references, split by how the engine resolves them at
    /// runtime — the split matters because the two paths have different failure gates (#377):
    /// <list type="bullet">
    ///   <item><see cref="GrantedRuleNames"/> — names granted as <c>RuleGrant</c> tokens
    ///         (<see cref="Effect.AddRule"/>, <see cref="Effect.Aura"/>, <see cref="Effect.MarkTarget"/>).
    ///         RuleEvaluator.CollectGrantedRules resolves the RAW name (no argument parsing) and screens
    ///         out any definition that reads arguments, since grants carry none.</item>
    ///   <item><see cref="WeaponRuleNames"/> — a damage effect's <see cref="Effect.DealHits.WithRules"/>
    ///         names, parsed with arguments ("Blast(3)") and resolved at Weapon scope by
    ///         <see cref="ArmyListSpellResolution.ResolveWeaponRuleNames"/>.</item>
    /// </list>
    /// Both recurse through <see cref="Effect.MoraleTestThen"/>'s on-failure arm, which applies its
    /// nested effect through the same machinery. Shared by army load's pre-flight, the #168 army audit,
    /// the #196 book coverage census, and <see cref="FDG.ArmyBuilding.BookRuleSupplement"/>'s embedding
    /// walk, so none of them can drift from the others.
    /// </summary>
    public static class SpellRuleReferences
    {
        /// <summary>Names the effect grants as RuleGrant tokens — resolved raw and argument-less at
        /// dispatch time; an unresolvable or argument-reading definition makes the grant a no-op.</summary>
        public static IEnumerable<string> GrantedRuleNames(Effect effect)
        {
            switch (effect)
            {
                case Effect.AddRule addRule:
                    yield return addRule.RuleName;
                    break;
                case Effect.Aura aura:
                    yield return aura.RuleName;
                    break;
                case Effect.MarkTarget mark:
                    yield return mark.RuleName;
                    break;
                case Effect.MoraleTestThen moraleTest:
                    foreach (string nested in GrantedRuleNames(moraleTest.OnFailure))
                    {
                        yield return nested;
                    }

                    break;
            }
        }

        /// <summary>Names the effect attaches to its synthetic damage weapon — argument-carrying
        /// references resolved at Weapon scope. Deliberately does NOT recurse through
        /// <see cref="Effect.MoraleTestThen"/>: a nested on-failure <see cref="Effect.DealHits"/> is not
        /// runtime-supported (the cast path has no child damage pipeline there), and this walker mirrors
        /// what <see cref="ArmyListSpellResolution.ResolveWeaponRules"/> actually resolves at load.</summary>
        public static IEnumerable<string> WeaponRuleNames(Effect effect)
        {
            if (effect is Effect.DealHits dealHits)
            {
                foreach (string withRule in dealHits.WithRules)
                {
                    yield return withRule;
                }
            }
        }
    }
}
