using System.Linq;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// #197 (P15): resolves the once-per-attack-action Unpredictable die into an <see cref="EUnpredictableBranch"/>.
    /// The rule reads "when attacking, roll one die: 1-3 -> AP(+1), 4-6 -> +1 to hit." Called once per attack
    /// ACTION from <see cref="Stages.CombatActionContext"/> (not per weapon), so a multi-weapon unit shares one
    /// branch across all its weapons - matching "roll one die ... apply to all models."
    ///
    /// The die is DECISIVE (<see cref="IDiceRollerExtensions.RollDecisiveFace"/>): even under the probabilistic
    /// roller it commits to one concrete face, exactly as morale / dangerous-terrain rolls do. Averaging a
    /// branch selector would be meaningless - the AP and +hit sub-distributions can't be blended into "half a
    /// modifier" on a threshold roll. A die is consumed ONLY when an applicable rule is present, so the seeded
    /// dice stream (#193) is untouched for the vast majority of attacks that have no Unpredictable rule.
    ///
    /// Detection spans native rules (unit + per-model) AND aura-granted rules (RuleGrant tokens), so
    /// "Unpredictable Fighter Aura" (which confers "Unpredictable Fighter") triggers the roll too. The Mark
    /// variants are out of scope: a mark grants Unpredictable at the hit-roll hook, after this action-level
    /// roll has already happened.
    /// </summary>
    public static class UnpredictableBranchResolver
    {
        // 4-6 -> +1 to hit; 1-3 -> AP(+1).
        private const int HIT_BONUS_MIN_FACE = 4;

        public static EUnpredictableBranch Resolve(IUnit attacker, bool isMelee, IDiceRoller diceRoller)
        {
            if (!AttackerHasApplicableRule(attacker, isMelee))
            {
                return EUnpredictableBranch.None;
            }

            int face = diceRoller.RollDecisiveFace();
            return face >= HIT_BONUS_MIN_FACE ? EUnpredictableBranch.HitBonus : EUnpredictableBranch.ApBonus;
        }

        private static bool AttackerHasApplicableRule(IUnit attacker, bool isMelee)
        {
            // "Unpredictable" fires on any attack; the Fighter/Shooter variants fire only on their combat kind.
            string kindName = isMelee
                ? CoreRuleCatalog.UnpredictableFighterRuleName
                : CoreRuleCatalog.UnpredictableShooterRuleName;

            bool Applies(string name) =>
                name == CoreRuleCatalog.UnpredictableRuleName || name == kindName;

            if (attacker.RuleDefinitions.Any(r => Applies(r.Definition.Name)))
            {
                return true;
            }

            if (attacker.Models.Any(m => m.RuleDefinitions.Any(r => Applies(r.Definition.Name))))
            {
                return true;
            }

            foreach (Token token in attacker.Tokens.GetAllTokens(TokenType.RuleGrant))
            {
                if (token.Payload is TokenPayload.RuleGrant grant && Applies(grant.RuleName))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
