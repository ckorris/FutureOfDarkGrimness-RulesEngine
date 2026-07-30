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
    /// Detection spans native rules (unit + per-model), aura-granted rules (RuleGrant tokens), and - #197
    /// Unpredictable Marks - a Mark token on the DEFENDER whose payload grants an applicable Unpredictable
    /// rule. The mark is claimed (converted into an attacker-side grant) only at the hit stage's
    /// ClaimTargetMarks, AFTER this action-level roll, so without the defender scan a mark-granted
    /// Unpredictable could never see a branch and both of its arms would gate themselves out.
    /// </summary>
    public static class UnpredictableBranchResolver
    {
        // 4-6 -> +1 to hit; 1-3 -> AP(+1).
        private const int HIT_BONUS_MIN_FACE = 4;

        public static EUnpredictableBranch Resolve(IUnit attacker, IUnit defender, bool isMelee, IDiceRoller diceRoller)
        {
            if (!AttackerHasApplicableRule(attacker, isMelee) && !DefenderMarkGrantsApplicableRule(defender, isMelee))
            {
                return EUnpredictableBranch.None;
            }

            int face = diceRoller.RollDecisiveFace();
            return face >= HIT_BONUS_MIN_FACE ? EUnpredictableBranch.HitBonus : EUnpredictableBranch.ApBonus;
        }

        // "Unpredictable" fires on any attack; the Fighter/Shooter variants fire only on their combat kind.
        private static bool Applies(string name, bool isMelee)
        {
            string kindName = isMelee
                ? CoreRuleCatalog.UnpredictableFighterRuleName
                : CoreRuleCatalog.UnpredictableShooterRuleName;

            return name == CoreRuleCatalog.UnpredictableRuleName || name == kindName;
        }

        private static bool AttackerHasApplicableRule(IUnit attacker, bool isMelee)
        {
            if (attacker.RuleDefinitions.Any(r => Applies(r.Definition.Name, isMelee)))
            {
                return true;
            }

            if (attacker.Models.Any(m => m.RuleDefinitions.Any(r => Applies(r.Definition.Name, isMelee))))
            {
                return true;
            }

            foreach (Token token in attacker.Tokens.GetAllTokens(TokenType.RuleGrant))
            {
                if (token.Payload is TokenPayload.RuleGrant grant && Applies(grant.RuleName, isMelee))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// #197 Unpredictable Marks: a mark on the target ("the first friendly attack against it counts as
        /// having Unpredictable Fighter/Shooter") grants the rule to the ATTACKER, but only when the hit
        /// stage claims it - after this action-level roll. Scanning the defender's marks here is what lets
        /// the branch exist by the time the claimed grant's arms read it.
        /// </summary>
        private static bool DefenderMarkGrantsApplicableRule(IUnit defender, bool isMelee)
        {
            foreach (Token token in defender.Tokens.GetAllTokens(TokenType.Mark))
            {
                if (token.Payload is TokenPayload.RuleGrant grant && Applies(grant.RuleName, isMelee))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
