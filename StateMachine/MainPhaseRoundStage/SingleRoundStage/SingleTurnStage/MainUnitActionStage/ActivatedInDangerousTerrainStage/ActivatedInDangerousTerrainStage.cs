using FDG.Data;

namespace FDG.Stages
{
    /// <summary>
    /// #446 - Core Rules v3.6.0, Dangerous Terrain: "Units with models moving across Dangerous terrain, OR THAT
    /// ACTIVATE IN IT, must roll as many dice as the number of wounds it would take to fully destroy them, and
    /// for each roll of 1 the unit takes one wound." (The Beginner's Guide: "If any model in a unit moves into or
    /// through Dangerous terrain, or activates in it, then the entire unit must take a Dangerous Terrain Test.")
    /// The move half has always rolled in <c>ApplyNonMovementTerrainEffectsStage</c>; this stage is the other
    /// half, so a unit that Holds, shoots or casts while standing in Dangerous terrain now tests too.
    ///
    /// <para><b>Where.</b> <see cref="MainUnitActionStage"/>'s STARTING child, ahead of
    /// <see cref="ActivationStartStage"/> (WorkItems/446 D1): "activate in it" is decided by where the unit
    /// stands when the activation begins, so an activation-start reposition (Wolfborn / Rapid Blink) can
    /// neither dodge the test nor cause one; a Shaken unit, which still activates (and idles), tests; and a unit
    /// the test destroys never sees the activation-start prompts or the action menu. It runs after
    /// <c>MainUnitActionStage.Enter</c> has swept the activation's wound ledger (#419), so the test's wounds
    /// count as "taken during this activation" for a melee result later in it (a charge out of Dangerous
    /// terrain), and after the per-activation marker sweep, so the test it rolls marks the unit for the rest of
    /// this activation and its move out across the piece does not test again (D2).</para>
    ///
    /// <para><b>Rolling commits the activation</b> (#248): dice were rolled and wounds may have landed, so the
    /// activation is marked irreversible and cannot be backed out of to re-pick (which would re-roll). A unit
    /// that does not test - not in Dangerous terrain, Flying, embarked - leaves the activation pristine.</para>
    ///
    /// <para><b>A unit the test destroys</b> leaves through <see cref="OnUnitDestroyed"/>, straight to the
    /// end-of-activation reconcile (which skips a dead unit's end-of-activation abilities and still marks it
    /// activated), with the destruction seam already run by
    /// <see cref="MovementExecutor.ResolveDangerousTerrain"/>.</para>
    ///
    /// <para>Runs once per activation: it is the starting child, and every loop-back returns to
    /// <see cref="ChooseActionStage"/>, never here.</para>
    /// </summary>
    public class ActivatedInDangerousTerrainStage : StageBase<IUnitActionContext>
    {
        /// <summary> The test resolved (or none was owed) and the unit lives; on to the activation start. </summary>
        public StageBinding OnFinished;

        /// <summary> The test destroyed the unit - its activation ends here. </summary>
        public StageBinding OnUnitDestroyed;

        public ActivatedInDangerousTerrainStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent)
            : base(gameContext, parent)
        {
            OnFinished = new StageBinding(this);
            OnUnitDestroyed = new StageBinding(this);
        }

        public override async Task Enter(IUnitActionContext context)
        {
            IUnit unit = context.ActivatingUnit.GetValue();

            MovementExecutor.DangerousTerrainResult test = MovementExecutor.RollActivationDangerousTerrain(
                GameContext, unit, GameContext.TableState.Terrain.Objects);

            if (!test.AnyTested)
            {
                await OnFinished.Activate(context);
                return;
            }

            // Dice were rolled: re-picking the unit would re-roll them (#248).
            context.MarkIrreversibleAction();

            await MovementExecutor.ResolveDangerousTerrain(GameContext, test);

            if (!unit.GetIsAlive())
            {
                GameContext.Log($"{unit.Name} was destroyed by Dangerous terrain - its activation ends.");
                await OnUnitDestroyed.Activate(context);
                return;
            }

            await OnFinished.Activate(context);
        }
    }
}
