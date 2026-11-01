
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Utilities;

namespace FDG.Stages
{
    public class DetermineMeleeWinnerStage : StageBase<ICombatActionContext>
    {
        public const string DETERMINE_MELEE_WINNER_NEEDS_ROLL_TRANSITION = "DetermineMeleeWinnerNeedsRoll";
        public const string DETERMINE_MELEE_WINNER_DOESNT_NEED_ROLL_TRANSITION = "DetermineMeleeWinnerDoesntNeedRoll";

        public StageBinding OnNeedsRollToDecide;
        public StageBinding OnDoesntNeedRollToDecide;

        public DetermineMeleeWinnerStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnNeedsRollToDecide = new StageBinding(this);
            OnDoesntNeedRollToDecide = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            IUnit attacker = context.AttackingUnit.GetValue();
            IUnit defender = context.DefendingUnit.GetValue();

            // #419 Core Rules v3.6.0, Melee Results: "compare the number of wounds each unit took during this
            // activation, and the unit with the highest total loses". Each side's total is what it had already
            // taken this activation when the melee began (the activation's wound ledger, snapshotted by the
            // context: a Dangerous Terrain test on the charge move, a Breath Attack or spell) plus what it took
            // since (the remaining-wounds snapshot: Impact hits, Counter strikes, swings, strike-backs, its own
            // Hazardous overheat). Both snapshots swap with the Counter role swap, so they follow the unit.
            float attackerTaken = context.AttackerWoundsTakenBeforeMelee
                + (context.AttackerRemainingWoundsAtStart - context.AttackingUnit.RemainingWounds());
            float defenderTaken = context.DefenderWoundsTakenBeforeMelee
                + (context.DefenderRemainingWoundsAtStart - context.DefendingUnit.RemainingWounds());
            LogEarlierWounds(attacker, context.AttackerWoundsTakenBeforeMelee);
            LogEarlierWounds(defender, context.DefenderWoundsTakenBeforeMelee);

            // #021/#419 Fear(X) v3.6.0: "When in melee with this model, enemies count as having taken +X wounds
            // when checking who won." No real wounds. Melee_OnMeleeResolution fires for each side as the Actor;
            // its own ExtraMeleeWoundCount lands on the OPPONENT's taken total, so the loser - and thus who must
            // test morale - can flip.
            MeleeResolutionContext resolution = new MeleeResolutionContext(attacker, defender);
            float attackerFear = SumExtraMeleeWounds(resolution, attacker);
            float defenderFear = SumExtraMeleeWounds(resolution, defender);
            if (attackerFear > 0f)
                GameContext.Log($"{defender.Name} counts as having taken +{Wounds(attackerFear)} ({attacker.Name}'s Fear).");
            if (defenderFear > 0f)
                GameContext.Log($"{attacker.Name} counts as having taken +{Wounds(defenderFear)} ({defender.Name}'s Fear).");
            defenderTaken += attackerFear;
            attackerTaken += defenderFear;

            // #431 Scourge: "enemies count as having taken 2 extra wounds when checking who won melee". The struck
            // unit's counted wounds (earned on its own block rolls of 1, CountedWounds) are part of ITS taken
            // total, and are used up by this check.
            float attackerCountedTaken = CountedWounds.Take(attacker);
            float defenderCountedTaken = CountedWounds.Take(defender);
            if (attackerCountedTaken > 0f)
                GameContext.Log($"{attacker.Name} counts as having taken +{Wounds(attackerCountedTaken)} in melee.");
            if (defenderCountedTaken > 0f)
                GameContext.Log($"{defender.Name} counts as having taken +{Wounds(defenderCountedTaken)} in melee.");
            attackerTaken += attackerCountedTaken;
            defenderTaken += defenderCountedTaken;

            (string attackerLabel, string defenderLabel) = ResultLabels(attacker, defender);
            string tally = $"{attackerLabel} took {Wounds(attackerTaken)}, {defenderLabel} took {Wounds(defenderTaken)}";
            if (attackerTaken == defenderTaken)
            {
                GameContext.Log($"Melee result: {tally} - a tie, nobody tests morale.");
                context.AddResult(new DetermineMeleeWinnerResults(DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie));
                await OnDoesntNeedRollToDecide.Activate(context);
            }
            else if (defenderTaken > attackerTaken)
            {
                GameContext.Log($"Melee result: {tally} - {defenderLabel} took more and loses the melee.");
                context.AddResult(new DetermineMeleeWinnerResults(DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon));
                await OnNeedsRollToDecide.Activate(context);
            }
            else
            {
                GameContext.Log($"Melee result: {tally} - {attackerLabel} took more and loses the melee.");
                context.AddResult(new DetermineMeleeWinnerResults(DetermineMeleeWinnerResults.EMeleeWinnerResult.DefenderWon));
                await OnNeedsRollToDecide.Activate(context);
            }
        }

        // Says why a total is higher than the melee's own exchange: wounds the unit took earlier in the activation.
        private void LogEarlierWounds(IUnit unit, float wounds)
        {
            if (wounds > 0f)
                GameContext.Log($"{unit.Name} already took {Wounds(wounds)} earlier this activation - they count.");
        }

        // The two names the result line uses. A mirror match puts two "Warriors" in one melee, so identical unit
        // names gain their owners' names - and the seats, should those match too.
        private (string Attacker, string Defender) ResultLabels(IUnit attacker, IUnit defender)
        {
            if (attacker.Name != defender.Name) return (attacker.Name, defender.Name);

            string attackerOwner = GameContext.GetPlayerName(attacker.PlayerID);
            string defenderOwner = GameContext.GetPlayerName(defender.PlayerID);
            return attackerOwner != defenderOwner
                ? ($"{attacker.Name} ({attackerOwner})", $"{defender.Name} ({defenderOwner})")
                : ($"{attacker.Name} (attacker)", $"{defender.Name} (defender)");
        }

        // "1 wound", "2 wounds", "0.5 wounds" - fractional under the probabilistic roller, so the number agrees
        // with what is printed.
        private static string Wounds(float count)
        {
            string shown = count.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            return shown == "1" ? "1 wound" : $"{shown} wounds";
        }

        // Sum the extra melee wounds (Fear) the actor's rules add to its OPPONENT's who-won total (3.6: "enemies
        // count as having taken +X wounds"). The actor sits in the Actor seat (Fear's default), so only its rules
        // fire; the opponent's Fear is summed by the symmetric call. ExtraMeleeWoundCount arrives already resolved
        // to an int.
        //
        // #175: the unit's LIVING models ride along (AnyOwner), the same shape every other model-aware
        // dispatch site uses. Fear(X)'s rulebook text is per-MODEL ("When in melee with this model, enemies
        // count as having taken +X wounds"), and a joined hero's rules live on the hero MODEL (#006 slice F) — without the
        // models list this site collected unit-static rules only, so a hero-only Fear contributed
        // nothing at all. (X) rules are exempt from the no-stack dedup, so a host unit's Fear and a
        // joined hero's Fear are two sources and sum; the unit-level rule still counts once, not once
        // per model carrying it. A hero killed during this melee is not among the living and adds
        // nothing, matching every other living-models site.
        private float SumExtraMeleeWounds(MeleeResolutionContext resolution, IUnit actor)
        {
            IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                resolution, RuleParticipant.Actor(actor, models: HeroStatRules.LivingModels(actor)));

            float extra = 0f;
            foreach (RuleOperation operation in operations)
            {
                if (operation is RuleOperation.ExtraMeleeWoundCount fear) extra += fear.Amount;
            }
            return extra;
        }
    }

    public readonly struct DetermineMeleeWinnerResults
    {
        public readonly EMeleeWinnerResult Winner;

        public DetermineMeleeWinnerResults(EMeleeWinnerResult result)
        {
            Winner = result;
        }

        public enum EMeleeWinnerResult
        {
            AttackerWon,
            DefenderWon,
            Tie
        }
    }
}
