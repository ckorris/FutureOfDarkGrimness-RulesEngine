using FDG.Presentation.Beats;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// Engine-side implementation of <see cref="IOperationServices"/>: enacts imperative rule
    /// operations against live game state through the existing stage subsystems. Holds the
    /// <see cref="IGameContext"/> so an operation queue (e.g. one produced by accepting an
    /// activated ability) can be run from anywhere via <c>OperationExecutor</c>.
    /// </summary>
    public class GameOperationServices : IOperationServices
    {
        // The amber the Shaken banners use (MoraleUtilities' / SpilloutExecutor's copies are private).
        private static readonly TextColor RecoveryBannerColor = new TextColor(255, 170, 60, 255);

        private readonly IGameContext _gameContext;

        public GameOperationServices(IGameContext gameContext)
        {
            _gameContext = gameContext;
        }

        public async Task MoveUnit(IUnit unit, float maxInches, bool isOptional, PlayerID? controller = null)
        {
            DataBinding<UnitData> unitBinding = ResolveUnitBinding(unit);

            bool canMoveThroughEnemies = Rules.Dispatch.MovementRuleQueries.CanMoveThroughEnemies(
                unitBinding.GetValue(), _gameContext.RuleEvaluator);
            bool ignoresDifficultTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresDifficultTerrain(
                unitBinding.GetValue(), _gameContext.RuleEvaluator);
            bool ignoresImpassibleTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresAllTerrain(
                unitBinding.GetValue(), _gameContext.RuleEvaluator);

            // The move is directed by the controller (a forced enemy move routes to the caster),
            // falling back to the unit's own owner for the self-move case.
            PlayerID mover = controller ?? unit.PlayerID;

            // A triggered move has a single budget; offer it in every slot so the resolver
            // renders one ring rather than the Advance/Rush/Charge tiers.
            //
            // allowCancel tracks optionality: an optional "may move" (Harassing / Hit & Run and the rest
            // of the post-combat family) can be declined, so the resolver may reply Cancelled and a human
            // gets a Back button. A FORCED move (e.g. a spell pushing an enemy) is not cancellable - the
            // rule has already fired and there is nowhere to return to.
            var pathRequest = new DefineMovementPathRequest(mover, $"Moving {unitBinding.GetValue().Name}",
                unitBinding, maxInches, maxInches, maxInches,
                WeaponSightProfileBuilder.For(unitBinding.GetValue(), _gameContext.RuleEvaluator),
                canMoveThroughEnemies, ignoresDifficultTerrain, ignoresImpassibleTerrain,
                allowCancel: isOptional);

            CancellableResult<List<ModelMoveEntry>> pathResult = await _gameContext.PlayerRequester
                .RequestDecision<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>>(pathRequest);

            if (pathResult is not Selected<List<ModelMoveEntry>> selectedPath)
            {
                // Declined. For an optional move that is a legal "no thanks" - typically a unit still
                // intermingled with the enemy after melee whose living models cannot re-pack into
                // cohesion without crossing an enemy base, so no legal destination exists. The unit
                // simply does not move (the caller sees no position change and keeps its budget).
                if (isOptional)
                {
                    _gameContext.Log($"{unit.Name} declines its triggered move - no legal destination.");
                    return;
                }
                throw new RequestResponseInvalidException(
                    $"Triggered move for {unit.Name} cannot be cancelled - the rule that forced it has already fired.");
            }
            List<ModelMoveEntry> movements = selectedPath.Value;

            if (!MovementExecutor.TryMove(_gameContext, unitBinding, movements, maxInches,
                    out List<ReasonForInvalidMove> errors,
                    out MovementExecutor.DangerousTerrainResult dangerResult))
            {
                // The resolver returned a path the authoritative validator rejects. For an optional move
                // this is the same "no legal destination" case as a cancel - a resolver with no decline
                // channel (the headless CLI auto-play) submits its best invalid guess instead of cancelling.
                // Treat it as a decline: the unit does not move. A forced move still faults, surfacing the bug.
                if (isOptional)
                {
                    _gameContext.Log($"{unit.Name} declines its triggered move - no legal destination.");
                    _gameContext.LogDebug($"Triggered move for {unit.Name} declined - resolver path invalid: "
                        + string.Join(", ", errors.Select(e => e.ToString())) + ".");
                    return;
                }
                throw new RequestResponseInvalidException(
                    $"Triggered move for {unit.Name} was invalid: "
                    + string.Join(", ", errors.Select(e => e.ToString())));
            }

            // Land the dangerous-terrain test now the models are in place, same as the normal-move stage —
            // a triggered move (Vanguard, forced move, etc.) that crosses dangerous terrain shows the roll
            // and animates its casualties, not just the wound.
            await MovementExecutor.ResolveDangerousTerrain(_gameContext, dangerResult);
        }

        public Task ApplyFatigue(IUnit unit)
        {
            FatigueUtilities.ApplyFatigued(unit);
            return Task.CompletedTask;
        }

        // #197 P8: the immediate arm of Dangerous Terrain Debuff - the victim tests where it stands, no
        // move involved. Rolls through the same batched primitive a real crossing uses and lands the
        // wounds through the same dice beat / casualty / destruction seam, so a unit killed by a forced
        // test dies exactly like one killed walking into a minefield.
        //
        // Flying (the AllTerrain ignore scope) waives the test outright, per the owner ruling: the roll
        // is skipped rather than rolled-and-discarded, so the seeded dice stream is untouched and a
        // realistic-mode replay stays identical. It is logged, because a rule that visibly fires and then
        // does nothing is the exact confusion the trace channel exists to prevent.
        public async Task ForceDangerousTerrainTest(IUnit unit)
        {
            bool ignoresDangerousTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresAllTerrain(
                unit, _gameContext.RuleEvaluator);
            if (ignoresDangerousTerrain)
            {
                _gameContext.Log($"{unit.Name} ignores all terrain, so it takes no dangerous terrain test.");
                return;
            }

            MovementExecutor.DangerousTerrainResult result =
                MovementExecutor.RollForcedDangerousTerrain(_gameContext, unit);
            await MovementExecutor.ResolveDangerousTerrain(_gameContext, result);
        }

        // #197: the non-spell home for Effect.MoraleTestThen (Mind Control, Fatigue Debuff). Same two
        // halves CastSpellStage's multi-target version runs - the rule-aware morale test, then the
        // on-failure effect - but for ONE target and with its own log line, since a unit rule announces
        // itself rather than riding a spell banner.
        //
        // The on-failure effect is resolved against the failing unit as both bearer and target, then its
        // operations are applied AND executed: the corpus consequences are a token grant (fatigue) and an
        // imperative move, and the two filters are disjoint, so running both is safe for either.
        public async Task MoraleTestThen(IUnit bearer, IUnit unit, Effect onFailure)
        {
            // The test needs the concrete unit (MoraleUtilities mutates Shaken/Routed state on it); every
            // live IUnit in play is a UnitData, and a non-UnitData caller is a programming error, not a
            // game state - so this warns and no-ops rather than throwing mid-stage.
            if (unit is not UnitData unitData)
            {
                RuleDiagnostics.Warn($"MoraleTestThen on '{unit.Name}', which is not a live unit; skipped.");
                return;
            }

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                _gameContext, unitData, HeroStatRules.GetMoraleQuality(unitData));

            if (outcome.Passed)
            {
                _gameContext.Log($"{unit.Name} passed the morale test - no effect.");
                return;
            }

            _gameContext.Log($"{unit.Name} failed the morale test.");

            List<RuleOperation> operations = new List<RuleOperation>();
            onFailure.Apply(
                new RuleInvocation(null, bearer, System.Array.Empty<Rules.Foundation.RuleArgument>(),
                    unit, _gameContext.DiceRoller),
                operations);
            OperationApplier.ApplyTokenOperations(operations);
            await OperationExecutor.Execute(operations, this);
        }

        public async Task ClearTokenOnRoll(IUnit unit, Rules.Foundation.TokenType tokenType, int minRoll)
        {
            // RollDecisiveFace, never Roll(1): the outcome is binary — the unit either sheds the token or it
            // does not — so under the probabilistic roller a histogram would want to remove a FRACTION of a
            // token. RollDecisive commits to one face (and RollDecisiveFace throws if a roller ever stops
            // honouring that), which is the same threshold-on-a-decisive-roll shape every other pass/fail
            // test in the engine uses. Routing it through the context roller also keeps it seed-reproducible.
            int face = _gameContext.DiceRoller.RollDecisiveFace();
            bool cleared = face >= minRoll;
            if (cleared)
            {
                unit.Tokens.RemoveTokens(tokenType);
            }

            // The catalog display name, not the TokenType record itself — interpolating the record
            // prints its structural form ("TokenType { Id = Shaken }") in player-facing text.
            string tokenName = TokenDefinitionCatalog.Lookup(tokenType).Name;
            _gameContext.Log($"{unit.Name} rolled {face} to shed {tokenName} on a {minRoll}+ - " +
                (cleared ? "recovered." : "no effect."));

            // The beat's histogram is exactly the roll that happened: one die, on the face it showed.
            // #289: FromDecisive, so a probabilistic game still draws that die instead of a success bar.
            float[] perSide = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];
            perSide[face - 1] = 1f;

            await _gameContext.Presenter.Present(DiceRolledBeat.FromDecisive(new DiceResults(perSide), minRoll,
                $"Recover from {tokenName}",
                cleared ? "recovered" : "still " + tokenName));

            // #278: shedding the token deserves a Toast (tier-2) banner beyond the die itself — amber,
            // matching the Shaken-family banners (this path is Steadfast-style Shaken recovery today).
            if (cleared)
            {
                await _gameContext.Announce($"{unit.Name} recovers - no longer {tokenName}!",
                    RecoveryBannerColor, EBannerTier.Toast);
            }
        }

        public async Task GrantTokenOnRoll(IUnit unit, Rules.Tokens.Token token, int minRoll,
            Effect? onFailure = null)
        {
            // Decisive for the same reason as ClearTokenOnRoll above: the marker is either placed or it
            // is not — a histogram would want to place a fraction of one.
            int face = _gameContext.DiceRoller.RollDecisiveFace();
            bool placed = face >= minRoll;
            if (placed)
            {
                unit.Tokens.AddToken(token);
            }

            string label = TokenDefinitionCatalog.Lookup(token.Type).Name;
            bool backfires = !placed && onFailure != null;
            _gameContext.Log($"Rolled {face} to place {label} on {unit.Name} ({minRoll}+ needed) - " +
                (placed ? "placed." : backfires ? "backfired." : "no effect."));

            float[] perSide = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];
            perSide[face - 1] = 1f;

            await _gameContext.Presenter.Present(DiceRolledBeat.FromDecisive(new DiceResults(perSide), minRoll,
                $"Place {label}",
                placed ? "marker placed" : backfires ? "backfired" : "no effect"));

            if (backfires)
            {
                // The gamble's failure arm rides the SAME die (#376 Reckless Piercing): apply the nested
                // effect with the rolling unit as both bearer and target - the MoraleTestThen pattern.
                var operations = new List<RuleOperation>();
                onFailure!.Apply(
                    new RuleInvocation(null, unit, System.Array.Empty<Rules.Foundation.RuleArgument>(),
                        unit, _gameContext.DiceRoller),
                    operations);
                OperationApplier.ApplyTokenOperations(operations);
                await OperationExecutor.Execute(operations, this);
            }
        }

        public async Task RedeployAsAmbush(IUnit unit)
        {
            // "Dropping any objectives it might hold within 1\"" - held means seized by this unit's SIDE
            // (#297: objectives belong to a side), within 1" of a living model measured base-edge to
            // marker centre, the same footprint measure ReconcileObjectivesStage uses for seizing.
            foreach (IObjective objective in _gameContext.TableState.Objectives.Objects)
            {
                if (objective.OwnerID == null) continue;
                if (!ITeamExtensions.AreAllied(_gameContext.TableState.Teams.Objects,
                        objective.OwnerID.Value, unit.PlayerID))
                {
                    continue;
                }

                bool held = unit.Models.Any(model => model.GetIsAlive()
                    && BaseShapeGeometry.SurfaceDistanceToPoint2D(
                        model.BaseShape, model.Position, model.Facing, objective.Position) <= 1f);
                if (!held) continue;

                objective.SetOwner(null);
                _gameContext.Log($"{unit.Name} drops the objective it was holding.");
            }

            // Off the table: reserve is explicit unit state (#202), and the models park at the unplaced
            // sentinel so their stale positions can't block placements or catch line-of-sight scans.
            foreach (IModel model in unit.Models)
            {
                if (model is ModelData modelData)
                {
                    modelData.SetPosition(new Position());
                }
            }

            Rules.Dispatch.ReserveRules.PlaceInReserve(unit);
            unit.Tokens.AddToken(TokenDefinitionCatalog.Create(
                Rules.Foundation.TokenType.PendingAmbushArrival));

            await _gameContext.Announce(
                $"{unit.Name} slips away - it redeploys from Ambush next round!", RecoveryBannerColor);
        }

        public async Task SpawnUnit(IUnit placer, string specName, float radiusInches)
        {
            // The placer's own army carries the spec (each player's Spawn names its own book's units).
            DataBinding<ArmyData>? armyBinding = FindArmyBindingOf(placer);
            ArmyData? army = armyBinding?.GetValue();

            // The compiler keys a spec by the rule's exact argument text in `Id`; `Name` stays the unit's
            // display name ("Spores", not "Spores [5]"). A hand-authored file may key on either.
            SaveLoad.UnitFileEntry? spec = army?.RestoreRuleData()?.AuxiliaryUnits?
                .FirstOrDefault(entry =>
                    string.Equals(entry.Id, specName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.Name, specName, StringComparison.OrdinalIgnoreCase));
            if (armyBinding == null || army == null || spec == null)
            {
                Rules.Dispatch.RuleDiagnostics.Warn($"{placer.Name} tried to place '{specName}', but its " +
                    "army carries no auxiliary unit spec by that name - nothing spawns. (A Forge-compiled " +
                    "army embeds these; a hand-authored one lists them under 'auxiliaryUnits'.)");
                return;
            }

            // Build through the same path a deploying unit takes (ctor registers the models; rules attach
            // via GameBootstrap's helper; creation-time rules - Tough's max wounds, auras - apply the same
            // way FDGServer applies them at launch). The evaluator's resolver is the shared in-game one.
            Rules.Dispatch.IRuleResolver resolver = _gameContext.RuleEvaluator.RuleResolver
                ?? new Rules.Dispatch.RuleResolver();
            var persisted = army.RestoreRuleData();
            var unit = new UnitData(placer.PlayerID, spec, _gameContext.GameDataStore, resolver,
                persisted?.DefaultRangedEffectSet, persisted?.DefaultMeleeEffectSet);
            GameModel.GameBootstrap.AttachRulesFromArmyList(unit, spec, resolver);
            Rules.Dispatch.UnitCreationRules.Apply(unit, _gameContext.RuleEvaluator);

            DataReference unitReference = _gameContext.GameDataStore.Create(unit);
            DataBinding<UnitData> unitBinding =
                _gameContext.GameDataStore.GetDataBinding<UnitData>(unitReference);

            // Register with the army AND re-Set it through the store, so the grown binding list rides the
            // ordinary update broadcast to networked clients (the unit itself replicated on Create above).
            army.UnitBindings.Add(unitBinding);
            _gameContext.GameDataStore.SetValue(armyBinding.Reference, army);

            // "Fully within N of it": a circular zone around the placer, resolved through the normal
            // placement flow (overlap/cohesion/terrain checks included) with the #282 commit guard.
            Position center = PlacerCenter(placer);
            var zone = new CircularZone(new Float2(center.x, center.z), radiusInches);
            var request = new PlaceObjectsRequest<ModelData>(placer.PlayerID, $"Placing {unit.Name}",
                zone, unit.ModelBindings);
            List<PlacedObjectEntry<ModelData>> placements = await PlacementCommitGuard
                .RequestClearPlacement(_gameContext, request);
            foreach (PlacedObjectEntry<ModelData> placement in placements)
            {
                placement.Binding.GetValue().SetPosition(placement.Position);
                if (placement.Facing.HasValue)
                {
                    placement.Binding.GetValue().SetFacing(placement.Facing.Value);
                }
            }

            // Owner-ruled 2026-07-28: a mid-round creation may activate this round. The round context
            // adopts units carrying this marker at its own query seams and clears it.
            unit.Tokens.AddToken(TokenDefinitionCatalog.Create(
                Rules.Foundation.TokenType.JoinsRoundInProgress));

            await _gameContext.Announce($"{placer.Name} spawns {unit.Name}!", RecoveryBannerColor);
        }

        public async Task ReinforceUnit(IUnit unit, string? sourceRuleName)
        {
            // Belt-and-braces with the authored not(tokenPresent) gate: once spent, never again.
            if (unit.Tokens.HasToken(Rules.Foundation.TokenType.ReinforcementSpent))
            {
                return;
            }

            // Stamp BEFORE the removal below lands on the destruction seam, where the rule's own
            // destroyed-arm entry would otherwise fire a second prompt.
            unit.Tokens.AddToken(TokenDefinitionCatalog.Create(
                Rules.Foundation.TokenType.ReinforcementSpent));

            DataBinding<ArmyData>? armyBinding = FindArmyBindingOf(unit);
            if (armyBinding == null)
            {
                Rules.Dispatch.RuleDiagnostics.Warn(
                    $"{unit.Name} triggered Reinforcement but belongs to no registered army - no copy queued.");
                return;
            }

            // A fresh full-strength copy: new models from the originals' shapes and weapon profiles
            // (wounds reset by construction; Tough's max wounds re-derive via the creation rules), the
            // unit's rules re-attached MINUS the firing rule ("this rule doesn't apply to the new copy"),
            // per-model rules (#093 joined-hero relocations) carried over the same way.
            var modelBindings = new List<DataBinding<ModelData>>();
            foreach (IModel model in unit.Models)
            {
                if (model is not ModelData source) continue;
                var copyModel = new ModelData(source.BaseShape, new List<Weapon>(source.Weapons),
                    new Position(), _gameContext.GameDataStore);
                foreach (Rules.Dispatch.ResolvedRule rule in source.RuleDefinitions)
                {
                    copyModel.AttachRuleDefinition(rule);
                }

                modelBindings.Add(_gameContext.GameDataStore.GetDataBinding<ModelData>(
                    _gameContext.GameDataStore.Create(copyModel)));
            }

            var copy = new UnitData(unit.PlayerID, unit.Name, unit.Quality, unit.Defense, modelBindings);
            // #329: the copy displays the same points as the unit it duplicates (it IS that unit,
            // fielded again) - not 0, which would read as a freebie next to its twin.
            copy.PointCost = (unit as UnitData)?.PointCost ?? 0;
            foreach (Rules.Dispatch.ResolvedRule rule in unit.RuleDefinitions)
            {
                if (string.Equals(rule.Definition.Name, sourceRuleName ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                copy.AttachRuleDefinition(rule);
            }

            Rules.Dispatch.UnitCreationRules.Apply(copy, _gameContext.RuleEvaluator);

            DataReference copyReference = _gameContext.GameDataStore.Create(copy);
            DataBinding<UnitData> copyBinding =
                _gameContext.GameDataStore.GetDataBinding<UnitData>(copyReference);

            // Off-table until the next round start places it (after Ambushers) - reserve is the state,
            // the pending token is what the arrival pass looks for.
            Rules.Dispatch.ReserveRules.PlaceInReserve(copy);
            copy.Tokens.AddToken(TokenDefinitionCatalog.Create(
                Rules.Foundation.TokenType.PendingReinforcementArrival));

            ArmyData army = armyBinding.GetValue();
            army.UnitBindings.Add(copyBinding);
            _gameContext.GameDataStore.SetValue(armyBinding.Reference, army);

            await _gameContext.Announce(
                $"{unit.Name} falls back - reinforcements arrive at the start of the next round!",
                RecoveryBannerColor);

            // The Shaken arm: "remove it from the table as destroyed". The destroyed arm arrives here
            // with the unit already dead, so this is a no-op there.
            if (unit.GetIsAlive())
            {
                foreach (IModel model in unit.Models)
                {
                    if (model is ModelData alive && alive.GetIsAlive())
                    {
                        alive.DealWounds(alive.TotalWounds);
                    }
                }

                await UnitDestructionNotifier.NotifyUnitDestroyed(_gameContext, unit, killer: null);
            }
        }

        public async Task RestoreWounds(IUnit unit, int minRoll)
        {
            // One die per missing wound, dead models included. The probabilistic roller leaves
            // fractional wounds dealt; the pool floors them (conservative - a 0.4-wound scratch earns
            // no die), keeping token/dice counts integral without int-locking a roll-derived value.
            float missing = unit.Models.Sum(model => model.WoundsDealt);
            int pool = (int)missing;
            if (pool <= 0)
            {
                return;
            }

            // Decisive faces, never a histogram: each die's outcome is binary (a wound is restored or
            // it is not) - the ClearTokenOnRoll/Storm precedent.
            float[] perSide = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];
            int successes = 0;
            for (int i = 0; i < pool; i++)
            {
                int face = _gameContext.DiceRoller.RollDecisiveFace();
                perSide[face - 1] += 1f;
                if (face >= minRoll)
                {
                    successes++;
                }
            }

            int healed = 0, revived = 0;
            for (int i = 0; i < successes; i++)
            {
                // Wounds first (owner-ruled): top up the first wounded LIVING model. A model revived
                // below is then itself the wounded living model the next success tops up, so revives
                // proceed one at a time and fill before the next body returns.
                ModelData? wounded = unit.Models.OfType<ModelData>()
                    .FirstOrDefault(m => m.GetIsAlive() && m.WoundsDealt > 0.999f);
                if (wounded != null)
                {
                    wounded.DealWounds(-1f);
                    healed++;
                    continue;
                }

                ModelData? corpse = unit.Models.OfType<ModelData>().FirstOrDefault(m => !m.GetIsAlive());
                if (corpse == null)
                {
                    break; // fully healed - surplus successes are wasted, per the wording's cap
                }

                corpse.DealWounds(-(1f - (corpse.TotalWounds - corpse.WoundsDealt)));
                PlaceRevivedInCoherency(unit, corpse);
                revived++;
            }

            _gameContext.Log($"{unit.Name}: Reanimation rolled {pool} dice, {successes} at {minRoll}+ - " +
                $"restored {healed} wound{(healed == 1 ? "" : "s")}, revived {revived} " +
                $"model{(revived == 1 ? "" : "s")}.");
            await _gameContext.Presenter.Present(DiceRolledBeat.FromDecisive(new DiceResults(perSide),
                minRoll, "Reanimation",
                successes > 0 ? $"restored {successes}" : "no effect"));
        }

        // A revived model returns beside the unit's first living model (base-to-base 0.1", eight
        // angles, widening rings) - the wording's "may only be restored if they can be placed in
        // coherency with non-restored models", auto-placed per the owner's call. The last-resort
        // anchor-stack mirrors the deploy resolvers' CohesiveFallback: never lose the model, movement
        // re-packs overlap.
        private void PlaceRevivedInCoherency(IUnit unit, ModelData revived)
        {
            ModelData? anchor = unit.Models.OfType<ModelData>()
                .FirstOrDefault(m => m.GetIsAlive() && !ReferenceEquals(m, revived));
            if (anchor == null)
            {
                return;
            }

            var occupants = new List<(Position pos, IBaseShape shape, Float2 facing)>();
            foreach (IModel model in _gameContext.TableState.Models.Objects)
            {
                if (model is not ModelData other || !other.GetIsAlive()) continue;
                if (ReferenceEquals(other, revived)) continue;
                Position p = other.Position;
                if (p.x == 0f && p.z == 0f) continue;
                occupants.Add((p, other.BaseShape, other.Facing));
            }

            float anchorR = anchor.BaseShape.CircumscribedRadiusInches;
            float ownR = revived.BaseShape.CircumscribedRadiusInches;
            for (float distance = anchorR + ownR + 0.1f; distance <= anchorR + ownR + 2f; distance += 0.6f)
            {
                for (int step = 0; step < 8; step++)
                {
                    float angle = step * MathF.PI / 4f;
                    var candidate = new Position(anchor.Position.x + distance * MathF.Cos(angle),
                        anchor.Position.z + distance * MathF.Sin(angle));
                    if (candidate.x < ownR || candidate.z < ownR
                        || candidate.x > GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES - ownR
                        || candidate.z > GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES - ownR)
                    {
                        continue;
                    }

                    bool clear = occupants.All(o => !BaseShapeGeometry.AreColliding(
                        revived.BaseShape, candidate, revived.Facing, o.shape, o.pos, o.facing));
                    if (clear)
                    {
                        revived.SetPosition(candidate);
                        return;
                    }
                }
            }

            revived.SetPosition(anchor.Position);
        }

        private DataBinding<ArmyData>? FindArmyBindingOf(IUnit unit)
        {
            return _gameContext.GameDataStore.GetAllDataBindings<ArmyData>()
                .FirstOrDefault(b => b.GetValue().UnitBindings
                    .Any(u => u.GetValue().ID.Equals(unit.ID)));
        }

        // The point a spawn's "within N\" of it" measures from: the placer's first living model (the
        // corpus carriers are single models; for a squad the first living model is the deterministic pick).
        private static Position PlacerCenter(IUnit placer)
        {
            foreach (IModel model in placer.Models)
            {
                if (model.GetIsAlive()) return model.Position;
            }

            return placer.Models.Count > 0 ? placer.Models[0].Position : new Position();
        }

        private DataBinding<UnitData> ResolveUnitBinding(IUnit unit)
        {
            foreach (ArmyData army in _gameContext.GameDataStore.GetAllValues<ArmyData>())
            {
                foreach (DataBinding<UnitData> binding in army.UnitBindings)
                {
                    if (binding.GetValue().ID.Equals(unit.ID))
                    {
                        return binding;
                    }
                }
            }

            throw new InvalidOperationException(
                $"No data binding found for unit {unit.Name} ({unit.ID}).");
        }
    }
}
