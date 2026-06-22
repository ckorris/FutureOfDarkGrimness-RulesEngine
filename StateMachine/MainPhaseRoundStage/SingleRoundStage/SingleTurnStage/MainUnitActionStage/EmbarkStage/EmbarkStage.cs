using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #035 slice D — the mid-game counterpart of <see cref="DisembarkStage"/>. Resolves the Embark action
    /// a unit chose in <see cref="ChooseActionStage"/>: it asks which eligible friendly transport to board
    /// (when more than one), sets the unit aside off-table into it (an <c>EmbarkedIn</c> token + its models
    /// moved to origin), and ends the activation — boarding uses the unit's move action and puts it inside,
    /// from where it can't act this turn.
    ///
    /// The "within move-range of a friendly transport with room" eligibility is spatial, so it lives here in
    /// engine code (<see cref="GetEmbarkableTransports"/>) — shared with <see cref="ChooseActionStage"/>,
    /// which calls it to gate whether the Embark action surfaces at all (the same way Charge is gated by
    /// enemy-in-range). The simplification (set-aside if a transport is within Advance distance, no real path
    /// drawn) mirrors <see cref="DisembarkStage"/>'s Advance-equivalent placement; a real move-into-contact
    /// (and embark-via-Rush/Charge) is deferred.
    /// </summary>
    public class EmbarkStage : StageBase<IUnitActionContext>
    {
        public StageBinding OnEmbarked;            // the unit boarded — its activation is over
        public StageBinding OnBackToChooseAction;  // the player cancelled — re-offer the action menu

        public EmbarkStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent)
            : base(gameContext, parent)
        {
            OnEmbarked = new StageBinding(this);
            OnBackToChooseAction = new StageBinding(this);
        }

        public override async Task Enter(IUnitActionContext context)
        {
            UnitData unit = context.ActivatingUnit.GetValue();

            List<DataBinding<UnitData>> transports = GetEmbarkableTransports(GameContext, unit);
            if (transports.Count == 0)
            {
                // Defensive: ChooseActionStage only offers Embark when this is non-empty, but if state moved
                // on, just go back to the action menu rather than throwing.
                await OnBackToChooseAction.Activate(context);
                return;
            }

            DataBinding<UnitData>? chosen = await PromptEmbarkChoice(unit, transports);
            if (chosen == null)
            {
                await OnBackToChooseAction.Activate(context);
                return;
            }

            UnitData transport = chosen.GetValue();
            TransportUtilities.Embark(unit, transport);

            // Set the unit aside: move its models off-table (origin). With the EmbarkedIn token this makes it
            // embarked / off-battlefield — excluded from targeting, objectives, and melee for free.
            foreach (IModel model in unit.Models)
            {
                model.SetPosition(new Position(0f, 0f));
            }

            context.Log($"{unit.Name} embarked {transport.Name}.");
            await context.Announce($"{unit.Name} embarked {transport.Name}.", new TextColor(120, 200, 255, 255));

            // Boarding consumes the unit's move action and puts it inside — the activation is over.
            await OnEmbarked.Activate(context);
        }

        /// <summary>
        /// Friendly transports on the table that <paramref name="candidate"/> could board this activation:
        /// full embark eligibility (transport identity, same player, ride caps, remaining capacity) via
        /// <see cref="TransportUtilities.CanUnitEmbark"/>, restricted to deployed transports within the
        /// unit's move-shoot (Advance) distance. Shared with <see cref="ChooseActionStage"/>'s Embark gate.
        /// </summary>
        public static List<DataBinding<UnitData>> GetEmbarkableTransports(IGameContext gameContext, UnitData candidate)
        {
            List<DataBinding<UnitData>> allBindings = new List<DataBinding<UnitData>>();
            foreach (ArmyData army in gameContext.GameDataStore.GetAllValues<ArmyData>())
            {
                allBindings.AddRange(army.UnitBindings);
            }

            List<IUnit> allUnits = allBindings.Select(binding => (IUnit)binding.GetValue()).ToList();

            candidate.GetMobility(out float moveShootDistanceInches, out _);

            List<DataBinding<UnitData>> eligible = new List<DataBinding<UnitData>>();
            foreach (DataBinding<UnitData> binding in allBindings)
            {
                UnitData transport = binding.GetValue();
                if (transport.ID == candidate.ID) continue;
                if (!TransportUtilities.IsTransport(transport)) continue;
                if (!transport.GetIsOnBattlefield()) continue;
                if (!TransportUtilities.CanUnitEmbark(candidate, transport, allUnits, out _)) continue;

                float distance = UnitCompareUtilities.MinDistanceBetweenUnits(
                    candidate, transport, out _, out _, includeVertical: false);
                if (distance > moveShootDistanceInches) continue;

                eligible.Add(binding);
            }

            return eligible;
        }

        private async Task<DataBinding<UnitData>?> PromptEmbarkChoice(UnitData unit,
            List<DataBinding<UnitData>> transports)
        {
            List<SelectionRequest<UnitData>.ValidOption> options = new List<SelectionRequest<UnitData>.ValidOption>();
            foreach (DataBinding<UnitData> transport in transports)
            {
                options.Add(new SelectionRequest<UnitData>.ValidOption(
                    transport, $"Embark into {transport.GetValue().Name}"));
            }

            SelectionRequest<UnitData> request = new SelectionRequest<UnitData>(unit.PlayerID,
                $"Embark {unit.Name} into which transport? (Cancel to go back.)",
                options, new List<SelectionRequest<UnitData>.InvalidOption>(), allowCancel: true);

            return await GameContext.PlayerRequester
                .RequestDecision<SelectionRequest<UnitData>, DataBinding<UnitData>>(request);
        }
    }
}
