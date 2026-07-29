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

            // #232 casualty cascade: every casualty beat except the LAST overlaps the next (the
            // presenter paces only the stagger), so a multi-casualty volley plays as a rapid-fire
            // cascade with the final death/flinch running out in full. The assignments are
            // precomputed, so the last entry that will emit a beat (Wounds > 0) is known up front.
            int lastCasualtyIndex = -1;
            for (int i = 0; i < assignWoundsResults.PendingWounds.Count; i++)
            {
                if (assignWoundsResults.PendingWounds[i].Wounds > 0)
                    lastCasualtyIndex = i;
            }

            for (int i = 0; i < assignWoundsResults.PendingWounds.Count; i++)
            {
                PendingWounds pendingWound = assignWoundsResults.PendingWounds[i];
                ModelData model = pendingWound.Model; //Shorthand.
                float woundsToDeal = pendingWound.Wounds;

                float modelRemainingWounds = model.TotalWounds - model.WoundsDealt;

                if (woundsToDeal > modelRemainingWounds)
                {
                    throw new Exception($"Tried to deal {woundsToDeal} to a model with only {modelRemainingWounds} left.");
                }

                model.DealWounds(woundsToDeal);
                totalWoundsApplied += woundsToDeal;

                bool overlap = i != lastCasualtyIndex;

                if(model.GetIsDead())
                {
                    modelsKilled++;

                    // Present the death where the model fell. State is already dead; the front-end
                    // plays the death animation over the beat's duration before dropping the model.
                    await GameContext.Presenter.Present(
                        new ModelDiedBeat(model.ID, defendingUnit.ID, defendingUnit.Name, model.Position,
                            overlap));
                }
                else if (woundsToDeal > 0)
                {
                    // Survived a wound (e.g. Tough) — a hurt flinch, distinct from death.
                    await GameContext.Presenter.Present(new ModelWoundedBeat(model.ID, model.Position, overlap));
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

            await ApplySelfWounds(metaData);

            await onFinished(new ApplyWoundsResults(modelsKilled));
        }

        /// <summary>
        /// #197 Hazardous: the ATTACKER's own overheat, counted at hit-roll-complete and carried on
        /// <see cref="RollToHitResults.SelfWounds"/>. Applied here — the last stage of every attack chain
        /// that rolls to hit — so the attack the models paid for has fully resolved first (owner-ruled
        /// 2026-07-29), and never in <c>RollToHitStage</c> itself, whose continuation after
        /// <c>onFinished</c> is unreachable.
        /// <para>
        /// Chains with no hit roll (Ravage, Impact hits, spell damage) carry no such result and skip this
        /// entirely. The wounds are unignorable by construction: <c>ApplyUnitWounds</c> bypasses the save
        /// and wound-ignore pipeline, matching dangerous terrain and No Retreat.
        /// </para>
        /// </summary>
        private async Task ApplySelfWounds(ICombatMetadata metaData)
        {
            if (!metaData.QueryForResult(out RollToHitResults hitResults) || hitResults.SelfWounds <= 0f)
            {
                return;
            }

            UnitData attacker = metaData.AttackingUnit.GetValue();
            GameContext.Log($"{attacker.Name} takes {hitResults.SelfWounds:0.##} " +
                $"wound{(hitResults.SelfWounds == 1f ? "" : "s")} from its own {metaData.WeaponType.Name}.");

            await CasualtyPresentation.ApplyUnitWounds(GameContext, attacker, hitResults.SelfWounds);
        }
    }
}