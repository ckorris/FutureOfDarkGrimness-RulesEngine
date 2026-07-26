using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// #169 — the Transport destruction-spillout flow (#035 slice E), extracted from the retired
    /// SpilloutOccupantsStage so it runs from <see cref="UnitDestructionNotifier"/> — the single
    /// destruction choke point — instead of being hard-wired into individual combat pipelines.
    /// Every death path that notifies (shooting, melee swings, melee Rout, impact hits, spell
    /// damage, strafing, overwatch) now spills a destroyed transport's occupants; previously only
    /// the shooting and melee-swing tails did, stranding occupants permanently embarked when the
    /// transport died any other way (the #169 "ghost" bug).
    ///
    /// The flow itself is unchanged from slice E: each occupant is placed within 6" of the wreck
    /// (an interactive <see cref="PlaceObjectsRequest{T}"/> over a <see cref="CircularZone"/> —
    /// the wreck's models are dead but retain their last positions, so the zone is valid), then
    /// takes the destruction consequences — un-embark, Shaken, and a batched dangerous-terrain
    /// test (one die per living model, rolled as a single row) — via
    /// <see cref="TransportUtilities.ApplySpilloutEffects"/>. "Immediate, mid-resolution"
    /// (decided with the user): the spillout resolves as part of whatever killed the transport,
    /// before play continues. The common case (not a transport, or nobody aboard) is a cheap no-op.
    /// </summary>
    public static class SpilloutExecutor
    {
        // The wreck is dramatic (red, like a Rout); each occupant spilling out is Shaken (amber, matching
        // the morale Shaken banner). Kept local — MoraleUtilities' copies are private.
        private static readonly TextColor WreckBannerColor  = new TextColor(220, 40, 40, 255);
        private static readonly TextColor ShakenBannerColor = new TextColor(255, 170, 60, 255);

        /// <summary>
        /// Spill the dead unit's occupants if it is a destroyed Transport carrying anyone; no-op
        /// otherwise. Returns how many embarked units spilled out.
        /// </summary>
        public static async Task<int> SpillIfDestroyedTransport(IGameContext gameContext, IUnit dead)
        {
            if (!TransportUtilities.IsTransport(dead, gameContext.RuleEvaluator) || !dead.GetIsDead())
            {
                return 0;
            }

            return await SpillOccupants(gameContext, dead);
        }

        private static async Task<int> SpillOccupants(IGameContext gameContext, IUnit transport)
        {
            List<IUnit> allUnits = gameContext.GameDataStore.GetAllValues<UnitData>().Cast<IUnit>().ToList();
            List<IUnit> occupants = TransportUtilities.GetOccupants(transport, allUnits).ToList();
            if (occupants.Count == 0)
            {
                return 0;
            }

            Position wreck = RepresentativePosition(transport);
            CircularZone zone = new CircularZone(wreck.Position2D, TransportUtilities.MaxTransportRangeInches);

            await gameContext.Announce(
                $"{transport.Name} destroyed - {occupants.Count} unit(s) spill out!", WreckBannerColor);

            foreach (IUnit occupant in occupants)
            {
                UnitData occupantUnit = (UnitData)occupant;

                List<DataBinding<ModelData>> livingModels = occupantUnit.ModelBindings
                    .Where(binding => binding.GetValue().GetIsAlive()).ToList();

                var request = new PlaceObjectsRequest<ModelData>(occupantUnit.PlayerID,
                    $"Spill out {occupantUnit.Name} (within 6\" of the wreck)", zone, livingModels);
                // #282: commit-time overlap check - spilled cargo must not land inside another unit.
                List<PlacedObjectEntry<ModelData>> placements = await PlacementCommitGuard
                    .RequestClearPlacement(gameContext, request);

                foreach (PlacedObjectEntry<ModelData> placement in placements)
                {
                    placement.Binding.GetValue().SetPosition(placement.Position);
                    if (placement.Facing.HasValue) placement.Binding.GetValue().SetFacing(placement.Facing.Value);
                }

                // Un-embark + Shaken + the batched dangerous-terrain test (the deterministic core, unit-tested
                // in slice A). Run after placement so the dangerous test rolls for the now-on-table models; the
                // returned batch drives the presentation below.
                TransportUtilities.SpilloutRollResult rolls = TransportUtilities.ApplySpilloutEffects(
                    occupantUnit, gameContext.DiceRoller, gameContext.Settings.RandomnessType);

                // Per-occupant detail under the wreck Notice above: one per unit that was aboard, so it
                // rides along with the spillout placement rather than pausing once per occupant.
                await gameContext.Announce($"{occupantUnit.Name} spills out - Shaken!", ShakenBannerColor,
                    EBannerTier.Toast);
                await PresentSpilloutRolls(gameContext, occupantUnit, rolls);
            }

            return occupants.Count;
        }

        // Present the occupant's dangerous-terrain test as ONE row of dice (2+ safe, a 1 wounds — the same
        // batched beat MovementExecutor uses for dangerous terrain), then a hurt flinch or a death animation
        // per model the test wounded. Sound falls out via PresentationSoundCues.CueFor.
        private static async Task PresentSpilloutRolls(IGameContext gameContext, UnitData occupant,
            TransportUtilities.SpilloutRollResult result)
        {
            if (!result.AnyTested) return;

            string summary = result.Wounds <= 0f
                ? "All safe"
                : $"{result.Wounds:0.##} wound{(result.Wounds == 1f ? "" : "s")}!";
            await gameContext.Presenter.Present(DiceRolledBeat.From(result.Roll!, successThreshold: 2,
                gameContext.Settings.RandomnessType, "Dangerous Terrain", summary));

            foreach (TransportUtilities.SpilloutCasualty c in result.Casualties)
            {
                if (c.Died)
                    await gameContext.Presenter.Present(new ModelDiedBeat(c.Model, occupant.ID, occupant.Name, c.Position));
                else
                    await gameContext.Presenter.Present(new ModelWoundedBeat(c.Model, c.Position));
            }
        }

        // A destroyed transport's models are all dead but retain their last positions — read the wreck spot.
        private static Position RepresentativePosition(IUnit unit) =>
            unit.Models.Count > 0 ? unit.Models[0].Position : new Position(0f, 0f);
    }
}
