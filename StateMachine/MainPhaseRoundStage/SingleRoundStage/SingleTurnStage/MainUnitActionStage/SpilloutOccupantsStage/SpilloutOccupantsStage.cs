using FDG.Data;
using FDG.Presentation.Beats;
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
        // The wreck is dramatic (red, like a Rout); each occupant spilling out is Shaken (amber, matching
        // the morale Shaken banner). Kept local — MoraleUtilities' copies are private.
        private static readonly TextColor WreckBannerColor  = new TextColor(220, 40, 40, 255);
        private static readonly TextColor ShakenBannerColor = new TextColor(255, 170, 60, 255);

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

            await GameContext.Announce(
                $"{transport.Name} destroyed - {occupants.Count} unit(s) spill out!", WreckBannerColor);

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
                // in slice A). Run after placement so the dangerous test rolls for the now-on-table models; the
                // returned rolls drive the presentation below.
                IReadOnlyList<TransportUtilities.SpilloutModelRoll> rolls =
                    TransportUtilities.ApplySpilloutEffects(occupantUnit, GameContext.DiceRoller);

                await GameContext.Announce($"{occupantUnit.Name} spills out - Shaken!", ShakenBannerColor);
                await PresentSpilloutRolls(occupantUnit, rolls);
            }

            return occupants.Count;
        }

        // Present each occupant model's dangerous-terrain test in lockstep with the visuals: the d6 (2+ safe,
        // a 1 wounds — the same beat MovementExecutor uses for dangerous terrain), then a hurt flinch or a
        // death animation for a model the test wounded. Sound falls out via PresentationSoundCues.CueFor.
        private async Task PresentSpilloutRolls(UnitData occupant,
            IReadOnlyList<TransportUtilities.SpilloutModelRoll> rolls)
        {
            foreach (TransportUtilities.SpilloutModelRoll r in rolls)
            {
                float[] faces = new float[6];
                faces[r.Roll - 1] = 1f;
                await GameContext.Presenter.Present(new DiceRolledBeat(faces, sideMin: 1, successThreshold: 2,
                    GameContext.Settings.RandomnessType, "Dangerous Terrain", r.Wounded ? "1 wound!" : "Safe"));

                if (r.Died)
                    await GameContext.Presenter.Present(new ModelDiedBeat(r.Model, occupant.ID, occupant.Name, r.Position));
                else if (r.Wounded)
                    await GameContext.Presenter.Present(new ModelWoundedBeat(r.Model, r.Position));
            }
        }

        // A destroyed transport's models are all dead but retain their last positions — read the wreck spot.
        private static Position RepresentativePosition(UnitData unit) =>
            unit.Models.Count > 0 ? unit.Models[0].Position : new Position(0f, 0f);
    }
}
