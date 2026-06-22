using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary> Result of the spillout check — how many embarked units spilled out (0 when the defender
    /// wasn't a just-destroyed transport carrying anyone). </summary>
    public record SpilloutResults(int UnitsSpilledOut);

    /// <summary>
    /// #035 slice E — runs right after wounds are applied, in BOTH the shooting <c>FireStage</c> and the
    /// melee <c>SwingMeleeWeaponStage</c> (they share the AssignWounds → ApplyWounds tail). If the unit that
    /// just took wounds is a Transport that has now been destroyed, its embarked units spill out mid-combat:
    /// each is placed within 6" of the wreck (an interactive <see cref="PlaceObjectsRequest{T}"/> over a
    /// <see cref="CircularZone"/>) and takes the destruction consequences — un-embark, Shaken, and a
    /// per-model dangerous-terrain test — via <see cref="TransportUtilities.ApplySpilloutEffects"/>.
    ///
    /// "Immediate, mid-combat" (decided with the user): the spillout resolves as part of the attack that
    /// destroyed the transport, before the activation continues. The wreck's models are dead but retain
    /// their last positions, so the 6" placement zone is valid. The common case (defender isn't a destroyed
    /// transport, or carries no one) is a cheap no-op.
    /// </summary>
    public class SpilloutOccupantsStage<TMetadata>
        : CombatStage<SpilloutResults, SpilloutOccupantsStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public SpilloutOccupantsStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent)
            : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<SpilloutResults, Task> onFinished)
        {
            UnitData defender = metaData.DefendingUnit.GetValue();

            if (!TransportUtilities.IsTransport(defender) || !defender.GetIsDead())
            {
                await onFinished(new SpilloutResults(0));
                return;
            }

            int spilled = await SpillOccupants(defender);
            await onFinished(new SpilloutResults(spilled));
        }

        private async Task<int> SpillOccupants(UnitData transport)
        {
            List<IUnit> allUnits = GameContext.GameDataStore.GetAllValues<UnitData>().Cast<IUnit>().ToList();
            List<IUnit> occupants = TransportUtilities.GetOccupants(transport, allUnits).ToList();
            if (occupants.Count == 0)
            {
                return 0;
            }

            Position wreck = RepresentativePosition(transport);
            CircularZone zone = new CircularZone(wreck.Position2D, TransportUtilities.MaxTransportRangeInches);

            GameContext.Log($"{transport.Name} destroyed — {occupants.Count} embarked unit(s) spill out.");

            foreach (IUnit occupant in occupants)
            {
                UnitData occupantUnit = (UnitData)occupant;

                var request = new PlaceObjectsRequest<ModelData>(occupantUnit.PlayerID,
                    $"Spill out {occupantUnit.Name} (within 6\" of the wreck)", zone, occupantUnit.ModelBindings);
                List<PlacedObjectEntry<ModelData>> placements = await GameContext.PlayerRequester
                    .RequestDecision<PlaceObjectsRequest<ModelData>, List<PlacedObjectEntry<ModelData>>>(request);

                foreach (PlacedObjectEntry<ModelData> placement in placements)
                {
                    placement.Binding.GetValue().SetPosition(placement.Position);
                }

                // Un-embark + Shaken + a per-model dangerous-terrain test (the deterministic core, unit-tested
                // in slice A). Run after placement so the dangerous test rolls for the now-on-table models.
                TransportUtilities.ApplySpilloutEffects(occupantUnit, GameContext.DiceRoller);
                GameContext.Log($"{occupantUnit.Name} spilled out of {transport.Name} (Shaken + dangerous test).");
            }

            return occupants.Count;
        }

        // A destroyed transport's models are all dead but retain their last positions — read the wreck spot.
        private static Position RepresentativePosition(UnitData unit) =>
            unit.Models.Count > 0 ? unit.Models[0].Position : new Position(0f, 0f);
    }
}
