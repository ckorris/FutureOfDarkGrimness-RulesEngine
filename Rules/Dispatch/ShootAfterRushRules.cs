using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// #197 P20 Quick Shot - "this model may shoot after using Rush actions". The advance-and-shoot
    /// distance cap in <c>ChooseActionStage.GetCanShoot</c> is the gate this waives, and there are two
    /// ways a unit can hold the permission:
    /// <list type="bullet">
    /// <item><see cref="CanShootAfterRush"/> - it carries the rule itself (Quick Shot, or Quick Shot Aura
    /// conferring it on the hero's unit). Unconditional: it may then shoot anything.</item>
    /// <item><see cref="MarkGrantsShootAfterRush"/> - an ENEMY carries a Quick Shot mark, which the corpus
    /// words as "friendly units get Quick Shot AGAINST it once". The permission is target-bound, so the
    /// shoot stage narrows the target list to marked units (see <c>ChooseRangedAttackStage</c>).</item>
    /// </list>
    /// Both are detected STRUCTURALLY - by the effect a rule produces, not by its name - so an
    /// army-flavored alias or a renamed copy behaves identically, the same way <c>StrafingRules</c>
    /// recognises a strafe weapon.
    /// </summary>
    public static class ShootAfterRushRules
    {
        /// <summary>
        /// Whether <paramref name="unit"/>'s own rules (including anything conferred on it by an aura or a
        /// grant) let it shoot after a Rush. Non-logging - safe to call while building UI or an AI plan.
        /// </summary>
        public static bool CanShootAfterRush(IUnit unit, RuleEvaluator evaluator)
        {
            foreach ((RuleOperation op, string _) in evaluator.EvaluateAllNamed(
                         new ActionChoiceContext(unit), RuleParticipant.Actor(unit)))
            {
                if (op is RuleOperation.AllowShootAfterRush) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether <paramref name="target"/> carries a <see cref="TokenType.Mark"/> whose granted rule lets
        /// the attacker shoot after a Rush - the read half of Quick Shot Mark. The mark itself is claimed
        /// (and removed) later, by <c>DetermineHitRollStage</c> when the attack lands, so this is a pure
        /// look: asking the question must not spend the mark, or browsing the target list would burn it.
        /// <para>
        /// A null resolver (a bare-evaluator test, or a resume before #095 rehydration) means no marks can
        /// be read back at all, so it answers false - the same degrade granted-rule read-back takes.
        /// </para>
        /// </summary>
        public static bool MarkGrantsShootAfterRush(IUnit target, IRuleResolver? resolver)
        {
            if (resolver == null) return false;

            foreach (Token mark in target.Tokens.GetAllTokens(TokenType.Mark))
            {
                if (mark.Payload is not TokenPayload.RuleGrant grant) continue;
                if (!resolver.TryResolve(grant.RuleName, out ResolvedRule resolved)) continue;
                if (GrantsShootAfterRush(resolved.Definition)) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether <paramref name="definition"/> is a shoot-after-rush rule: it has a passive entry whose
        /// effect is <see cref="Effect.ShootAfterRush"/>. Structural, so "Quick Shot" and any army-flavored
        /// rename are recognised alike.
        /// </summary>
        public static bool GrantsShootAfterRush(SpecialRuleDefinition definition) =>
            definition.Passive.Any(entry => entry.Effect is Effect.ShootAfterRush);
    }
}
