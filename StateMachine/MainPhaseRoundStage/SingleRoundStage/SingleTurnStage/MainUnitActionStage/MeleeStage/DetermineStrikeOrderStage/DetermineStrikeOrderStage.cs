using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;

namespace FDG.Stages
{
    /// <summary>
    /// #042 Counter: a charged unit with Counter strikes FIRST, before the charging unit's strikes.
    /// Fires the OnCounterTrigger "when" for the defender (Subject seat); if a StrikeFirst op is queued,
    /// swaps the attacker/defender roles for the rest of the melee. The existing flow then runs
    /// unchanged with roles reversed — the Counter unit takes the first swing, and the charger is offered
    /// the strike-back. Runs after charge-contact (Impact) so the charger still deals its impact hits.
    ///
    /// Counter is weapon-scoped (#027: "strikes first with this weapon"), so the defender's melee
    /// weapons join the evaluation as carriers; the evaluator dedupes duplicate instances.
    /// </summary>
    public class DetermineStrikeOrderStage : StageBase<ICombatActionContext>
    {
        private static readonly TextColor CounterBannerColor = new TextColor(80, 220, 200, 255);

        public StageBinding OnStrikeOrderDetermined;

        public DetermineStrikeOrderStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent)
            : base(gameContext, parent)
        {
            OnStrikeOrderDetermined = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            IUnit attacker = context.AttackingUnit.GetValue();
            IUnit defender = context.DefendingUnit.GetValue();

            // #197 P20: the CHARGER joins as an Actor so Unwieldy ("strikes last when charging") can queue
            // the mirror op. Deliberately a plain unit participant, not the weapon-carrier fan-out the
            // defender gets: Counter is weapon-scoped ("strikes first with this weapon"), Unwieldy is a
            // unit/model rule about the whole charge. This evaluation also SPENDS one-shot grants, which is
            // how Unwieldy Debuff's "once (next time the effect would apply)" is consumed.
            IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                new CounterTriggerContext(attacker, defender),
                Prepend(RuleParticipant.Actor(attacker), SubjectWithMeleeWeapons(defender)));

            // A charger that strikes last and a defender that strikes first are the same swapped melee seen
            // from either side, so both ops route here and the swap happens exactly once even if both fire.
            bool defenderGoesFirst = operations.OfType<RuleOperation.StrikeFirst>().Any()
                || operations.OfType<RuleOperation.StrikeLast>().Any();

            // Only swap if the unit taking the first swing can actually fight — it must have a living model
            // with a melee weapon. Weapons are distributed across models (round-robin), so a unit can lose
            // its only melee-armed model yet keep others; swapping such a unit in as the attacker would
            // enter ChooseMeleeWeaponStage with an empty weapon pool and throw. (The normal strike-back path
            // guards the same way in OfferStrikeBackStage.) An Unwieldy charger facing a defender that
            // cannot swing therefore just strikes normally - there is no one to put ahead of it.
            if (defenderGoesFirst && context.DefendingUnit.GetValue().GetMeleeWeapons().Count > 0)
            {
                // Announce (banner + log) who gets the first swing and why, before the role swap makes it
                // so. Counter is named for the DEFENDER's rule; Unwieldy is the charger's own failing.
                string announcement = operations.OfType<RuleOperation.StrikeFirst>().Any()
                    ? $"{defender.Name} counters the charge - strikes first!"
                    : $"{attacker.Name} is unwieldy - {defender.Name} strikes first!";
                await GameContext.Announce(announcement, CounterBannerColor);
                context.SwapCombatRoles();
            }

            await OnStrikeOrderDetermined.Activate(context);
        }

        /// <summary>
        /// The defender as a Subject participant once per living-model melee weapon (so
        /// weapon-scoped Counter fires), plus once weaponless (so unit-scoped defensive
        /// rules fire even with no melee weapons). #183: the weaponless participant carries the defender's
        /// living models under AnyOwner, so a joined hero's relocated unit-scoped Counter-Attack becomes
        /// visible (gated by AllModelsHaveThisRule); the weapon carriers stay model-less (weapon-scoped
        /// Counter rides the weapon, not a model).
        /// </summary>
        internal static RuleParticipant[] SubjectWithMeleeWeapons(IUnit defender)
        {
            var participants = new List<RuleParticipant>
            {
                RuleParticipant.Subject(defender, models: HeroStatRules.LivingModels(defender)),
            };
            foreach (Weapon meleeWeapon in defender.GetMeleeWeapons())
            {
                participants.Add(RuleParticipant.Subject(defender, meleeWeapon));
            }
            return participants.ToArray();
        }

        // #197 P20: the charger's Actor seat ahead of the defender's participants. Its own method only
        // because SubjectWithMeleeWeapons is shared with the tests and must keep returning just the
        // defender's side.
        private static RuleParticipant[] Prepend(RuleParticipant first, RuleParticipant[] rest)
        {
            var all = new List<RuleParticipant> { first };
            all.AddRange(rest);
            return all.ToArray();
        }
    }
}
