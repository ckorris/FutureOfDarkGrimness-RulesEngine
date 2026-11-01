using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// The passive modifier a caster's own rules give its casting roll (#427, Core Rules v3.6.0 Spell Amplifier:
    /// "Gets +1 to casting rolls"). Rules author an <see cref="Effect.RollModifier"/> of
    /// <see cref="ERollKind.Cast"/> at <see cref="EHookID.Casting_OnSpellCastAttempt"/>; this folds them the way
    /// the hit-roll stage folds its own, so the cast stage, the spell picker's odds preview and any AI estimate
    /// read ONE number.
    ///
    /// <para>The caster is evaluated as a unit, with its living models and EVERY living model's weapons as
    /// participants: Spell Amplifier is printed on a weapon (a Caster Staff, "Psy-Hacker Gear"), and the engine
    /// does not track which model of the unit is the one casting, so any weapon in the unit counts. The
    /// evaluator's dedup keeps an argument-less rule at one firing per bearer however many weapons carry it
    /// ("multiple instances of the same rule do not stack"), so two staffs are still +1. A unit-level grant
    /// (a weapon-scoped rule granted to the whole unit) is re-homed onto every weapon at army load and a
    /// token-granted copy is read off the unit, so both reach the same fold.</para>
    /// </summary>
    public static class CastRollRules
    {
        /// <summary>
        /// The net passive modifier to <paramref name="caster"/>'s casting roll (positive = easier). Pass
        /// <paramref name="live"/> true from the cast stage so the rules narrate themselves in the game log and spend
        /// one-shot grants; false from a read-only preview (the spell picker), which must do neither.
        /// </summary>
        public static int Net(IUnit caster, RuleEvaluator evaluator, bool live)
        {
            var context = new SpellCastAttemptContext(caster);
            IReadOnlyList<IModel> living = HeroStatRules.LivingModels(caster);

            var participants = new List<RuleParticipant> { RuleParticipant.Actor(caster, weapon: null, models: living) };
            foreach (IWeapon weapon in caster.AllWeapons())
            {
                participants.Add(RuleParticipant.Actor(caster, weapon));
            }

            IReadOnlyList<RuleOperation> ops = live
                ? evaluator.EvaluateAll(context, participants.ToArray())
                : evaluator.EvaluateAllNamed(context, participants.ToArray()).Select(n => n.Op).ToList();

            var sink = new RollModifierSink();
            sink.ApplyFrom(ops);
            return sink.Net(ERollKind.Cast);
        }
    }
}
