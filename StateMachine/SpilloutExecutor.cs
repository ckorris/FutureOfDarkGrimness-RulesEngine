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
                // #284 (was #282 pre-reconciliation-27): commit-time overlap check - spilled cargo must not land inside another unit.
                List<PlacedObjectEntry<ModelData>> placements = await PlacementCommitGuard
                    .RequestClearPlacement(gameContext, request);

                // #309: un-embark BEFORE the positions land. Each SetPosition replicates immediately
                // and a networked client's renderer snapshots the unit's battlefield status as each
                // position arrives; with EmbarkedIn still set the client rendered the spilled unit
                // label-only until it next moved. ApplySpilloutEffects below still calls Disembark -
                // an idempotent token removal, by then a no-op - keeping its unit-tested core intact.
                TransportUtilities.Disembark(occupantUnit);

                foreach (PlacedObjectEntry<ModelData> placement in placements)
                {
                    placement.Binding.GetValue().SetPosition(placement.Position);
                    if (placement.Facing.HasValue) placement.Binding.GetValue().SetFacing(placement.Facing.Value);
                }

                // Un-embark + Shaken + the batched dangerous-terrain ROLL (the deterministic core, unit-tested
                // in slice A). Run after placement so the dangerous test rolls for the now-on-table models. The
                // wounds it rolls are left pending and landed by the presentation below, one at a time, so every
                // model stands on the table until its own death animation plays.
                TransportUtilities.SpilloutRollResult rolls = TransportUtilities.ApplySpilloutEffects(
                    occupantUnit, gameContext.DiceRoller, gameContext.Settings.RandomnessType);

                // Per-occupant detail under the wreck Notice above: one per unit that was aboard, so it
                // rides along with the spillout placement rather than pausing once per occupant.
                await gameContext.Announce($"{occupantUnit.Name} spills out - Shaken!", ShakenBannerColor,
                    EBannerTier.Toast);
                await PresentSpilloutRolls(gameContext, occupantUnit, rolls);

                // #197 P17c: the spillout Shaken is a real Shaken, so rules that trigger on the moment fire
                // here too - above all Reinforcement's "when this unit is Shaken ... you may remove it as
                // destroyed and place a copy". ApplySpilloutEffects sets the token from the Rules layer,
                // which cannot reach a stage, so the offer has to be made from this side.
                //
                // AFTER the dangerous-terrain wounds land, and only for a survivor, on purpose: a test that
                // finishes the occupant off has already gone through the destruction seam inside
                // PresentSpilloutRolls, where the SAME rule's destroyed arm fires and stamps
                // ReinforcementSpent. Offering first would either double-prompt or remove the unit and then
                // wound the wreckage.
                if (occupantUnit.GetIsAlive())
                {
                    await MoraleUtilities.OfferShakenTriggeredRules(gameContext, occupantUnit);
                }
            }

            return occupants.Count;
        }

        // Present the occupant's dangerous-terrain test as ONE row of dice (2+ safe, a 1 wounds — the same
        // batched beat MovementExecutor uses for dangerous terrain), then LAND each pending wound, its hurt
        // flinch or death animation playing as the wound lands. Sound falls out via PresentationSoundCues.CueFor.
        //
        // The wounds are applied here rather than back in ApplySpilloutEffects on purpose: the front-end hides
        // any model that is dead in state with no death beat registered, so applying them before the dice beat
        // left the casualties invisible for the whole roll and then made them reappear to die. Every model now
        // survives placement intact, the roll is read, and only then do the casualties drop.
        private static async Task PresentSpilloutRolls(IGameContext gameContext, UnitData occupant,
            TransportUtilities.SpilloutRollResult result)
        {
            if (!result.AnyTested) return;

            string summary = result.Wounds <= 0f
                ? "All safe"
                : $"{result.Wounds:0.##} wound{(result.Wounds == 1f ? "" : "s")}!";
            await gameContext.Presenter.Present(DiceRolledBeat.From(result.Roll!, successThreshold: 2,
                gameContext.Settings.RandomnessType, "Dangerous Terrain", summary));

            List<PendingModelWound> pending = result.PendingWounds
                .Select(w => new PendingModelWound(w.Model, w.Wounds)).ToList();

            bool wasAlive = occupant.GetIsAlive();

            await CasualtyPresentation.ApplyAndPresent(gameContext, pending, occupant.ID, occupant.Name);

            // The spillout's own dangerous test can finish off a battered occupant. That is a destruction
            // like any other and takes the same seam: its OwnerDestroyed marks clear, and an occupant that
            // was itself a Transport spills the cargo it was carrying. Killer-less, like every other
            // dangerous-terrain death. Re-entrant into SpillOccupants only for a carrier-inside-a-carrier,
            // which terminates because each level needs a distinct unit to die.
            if (wasAlive && !occupant.GetIsAlive())
            {
                await UnitDestructionNotifier.NotifyUnitDestroyed(gameContext, occupant, killer: null);
            }
        }

        // A destroyed transport's models are all dead but retain their last positions — read the wreck spot.
        private static Position RepresentativePosition(IUnit unit) =>
            unit.Models.Count > 0 ? unit.Models[0].Position : new Position(0f, 0f);
    }
}
