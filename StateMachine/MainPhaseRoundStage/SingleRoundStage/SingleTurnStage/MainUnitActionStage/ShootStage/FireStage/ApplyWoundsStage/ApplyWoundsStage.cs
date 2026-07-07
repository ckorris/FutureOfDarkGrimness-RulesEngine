using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Utilities;

namespace FDG.Stages
{

    public class ApplyWoundsStage<TMetadata> : CombatStage<ApplyWoundsResults, ApplyWoundsStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public ApplyWoundsStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent) 
            : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<ApplyWoundsResults, Task> onFinished)
        {
            AssignWoundsResults assignWoundsResults = QueryForResultOrThrowException<AssignWoundsResults>(metaData);

            float totalWoundsApplied = 0;
            int modelsKilled = 0;

            UnitData defendingUnit = metaData.DefendingUnit.GetValue();

            // Alive-before/dead-after guard for the destruction seam below: upstream stages already stop
            // attacking a dead defender, so this is defense-in-depth against a double-fire.
            bool defenderWasAlive = metaData.DefendingUnit.GetIsAlive();

            foreach(PendingWounds pendingWound in assignWoundsResults.PendingWounds)
            {
                ModelData model = pendingWound.Model; //Shorthand.
                float woundsToDeal = pendingWound.Wounds;

                float modelRemainingWounds = model.TotalWounds - model.WoundsDealt;

                if (woundsToDeal > modelRemainingWounds)
                {
                    throw new Exception($"Tried to deal {woundsToDeal} to a model with only {modelRemainingWounds} left.");
                }

                model.DealWounds(woundsToDeal);
                totalWoundsApplied += woundsToDeal;

                if(model.GetIsDead())
                {
                    modelsKilled++;

                    // Present the death where the model fell. State is already dead; the front-end
                    // plays the death animation over the beat's duration before dropping the model.
                    await GameContext.Presenter.Present(
                        new ModelDiedBeat(model.ID, defendingUnit.ID, defendingUnit.Name, model.Position));
                }
                else if (woundsToDeal > 0)
                {
                    // Survived a wound (e.g. Tough) — a hurt flinch, distinct from death.
                    await GameContext.Presenter.Present(new ModelWoundedBeat(model.ID, model.Position));
                }
            }

            if(metaData.DefendingUnit.GetIsAlive())
            {
                GameContext.Log($"Applying {totalWoundsApplied} wounds killed {modelsKilled} models.");
            }
            else
            {
                GameContext.Log($"Applying {totalWoundsApplied} wounds killed {modelsKilled} models, killing the unit.");

                // The one choke point every attacker-caused death passes through (shooting, melee swings,
                // impact hits, spell damage, strafing): clear the dead unit's cross-unit marks and fire
                // the unit-destroyed hook with the attacker as killer.
                if (defenderWasAlive)
                {
                    await UnitDestructionNotifier.NotifyUnitDestroyed(GameContext, defendingUnit,
                        metaData.AttackingUnit.GetValue());
                }
            }

            await onFinished(new ApplyWoundsResults(modelsKilled));
        }
    }
}