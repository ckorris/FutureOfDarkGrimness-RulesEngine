using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 P17c: Reinforcement - "when a unit where all models have this rule is Shaken or fully
    // destroyed, you may remove it from the table as destroyed and place a new copy of it fully within
    // 12\" of any table edge at the beginning of the next round after Ambushers have been deployed.
    // Units that deploy via Reinforcement can't seize or contest objectives on the round they deploy,
    // and this rule doesn't apply to the new copy of the unit."
    //
    // Two trigger arms meeting on one token: the Shaken moment (the new Morale_OnShakenApplied
    // evaluation) and the unit's own destruction (P17b's Lifecycle_OnSelfDestroyed). Accepting stamps
    // ReinforcementSpent BEFORE the Shaken arm's removal-as-destroyed lands on the destruction seam,
    // which is what stops the destroyed-arm entry re-prompting - the double-fire this fixture pins.
    // The copy is a fresh unit held in reserve; the round-start pass places it after the ambushers,
    // MANDATORILY (the "you may" was spent at removal).
    [TestFixture]
    public class ReinforcementRuleIntegrationTests
    {
        private const string RuleName = "Reinforcement";

        private static Condition Gate() => new Condition.And(
            new Condition.AllModelsHaveThisRule(),
            new Condition.Not(new Condition.TokenPresent(TokenType.ReinforcementSpent)));

        private static SpecialRuleDefinition Definition() => new(RuleName,
            new[]
            {
                new HookEntry(EHookID.Lifecycle_OnSelfDestroyed, Gate(),
                    new Effect.ReinforceUnit(), ELifetime.UntilEndOfGame),
                new HookEntry(EHookID.Morale_OnShakenApplied, Gate(),
                    new Effect.ReinforceUnit(), ELifetime.UntilEndOfGame),
            },
            System.Array.Empty<ActivatedAbility>());

        private GameDataStore _store = null!;
        private RuleResolver _resolver = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _resolver = CoreRuleCatalog.CreateResolver();
            _resolver.Register(Definition());
            _player = new PlayerID(System.Guid.NewGuid());
        }

        [Test]
        public async Task ShakenAccepted_RemovesTheOriginal_QueuesTheCopy_AndNeverDoublePrompts()
        {
            (DataBinding<UnitData> stalkers, DataBinding<ArmyData> army) = MakeArmy();
            var requester = new ReinforceRequester { Accept = true };

            await MoraleUtilities.ApplyShakenWithPresentation(Ctx(requester), stalkers);

            Assert.That(requester.YesNoAsked, Is.EqualTo(1),
                "ONE prompt: the removal lands on the destruction seam, where the rule's destroyed-arm " +
                "entry must be held shut by the spent gate");
            Assert.That(stalkers.GetValue().GetIsAlive(), Is.False,
                "'remove it from the table as destroyed'");

            Assert.That(army.GetValue().UnitBindings, Has.Count.EqualTo(2), "the copy registered");
            UnitData copy = army.GetValue().UnitBindings[1].GetValue();
            Assert.That(ReserveRules.IsInReserve(copy), Is.True, "held until the next round start");
            Assert.That(copy.Tokens.HasToken(TokenType.PendingReinforcementArrival), Is.True);
            Assert.That(copy.Models.Count, Is.EqualTo(3), "a fresh FULL-strength copy");
            Assert.That(copy.Models.All(m => m.GetIsAlive()), Is.True);
            Assert.That(copy.RuleDefinitions.Any(r => r.Definition.Name == RuleName), Is.False,
                "'this rule doesn't apply to the new copy'");
        }

        [Test]
        public async Task FullyDestroyed_AlsoTriggers_EvenKillerless()
        {
            (DataBinding<UnitData> stalkers, DataBinding<ArmyData> army) = MakeArmy();
            foreach (DataBinding<ModelData> model in stalkers.GetValue().ModelBindings)
            {
                model.GetValue().DealWounds(model.GetValue().TotalWounds);
            }

            var requester = new ReinforceRequester { Accept = true };
            await UnitDestructionNotifier.NotifyUnitDestroyed(Ctx(requester), stalkers.GetValue(), killer: null);

            Assert.That(requester.YesNoAsked, Is.EqualTo(1));
            Assert.That(army.GetValue().UnitBindings, Has.Count.EqualTo(2),
                "a rout-style death still queues the copy");
        }

        [Test]
        public async Task DeclinedAtShaken_KeepsTheUnit_AndTheDestroyedArmStillOffersLater()
        {
            (DataBinding<UnitData> stalkers, DataBinding<ArmyData> army) = MakeArmy();
            var requester = new ReinforceRequester { Accept = false };

            await MoraleUtilities.ApplyShakenWithPresentation(Ctx(requester), stalkers);

            Assert.That(stalkers.GetValue().GetIsAlive(), Is.True, "declining keeps the Shaken unit");
            Assert.That(army.GetValue().UnitBindings, Has.Count.EqualTo(1));

            // Killed later: declining did not spend the gate, so the destroyed arm re-offers.
            foreach (DataBinding<ModelData> model in stalkers.GetValue().ModelBindings)
            {
                model.GetValue().DealWounds(model.GetValue().TotalWounds);
            }

            requester.Accept = true;
            await UnitDestructionNotifier.NotifyUnitDestroyed(Ctx(requester), stalkers.GetValue(), killer: null);

            Assert.That(requester.YesNoAsked, Is.EqualTo(2), "the decline saved the choice, not spent it");
            Assert.That(army.GetValue().UnitBindings, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task TheCopy_ArrivesMandatorily_InTheEdgeBand_AfterAmbushers()
        {
            (DataBinding<UnitData> stalkers, DataBinding<ArmyData> army) = MakeArmy();
            await MoraleUtilities.ApplyShakenWithPresentation(
                Ctx(new ReinforceRequester { Accept = true }), stalkers);
            UnitData copy = army.GetValue().UnitBindings[1].GetValue();

            // The round-start pass: the arrival is PLACED, never offered - any YesNo fails the test.
            var requester = new ReinforceRequester { ThrowOnYesNo = true, DestX = 2f, DestZ = 2f };
            var ctx = Ctx(requester);
            var stage = new StartOfRoundExtraActionStage(ctx, new NoOpLayer<IMainPhaseContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(new TestMainPhaseContext(ctx, roundCount: 2));

            Assert.That(requester.PlaceRequest, Is.Not.Null, "the copy was placed");
            var band = requester.PlaceRequest!.DeploymentZone as TableEdgeBandZone;
            Assert.That(band, Is.Not.Null, "'fully within 12\" of any table edge'");
            Assert.That(band!.BandWidthInches, Is.EqualTo(12f).Within(0.001f));
            Assert.That(band.IsPointWithinZone(new Float2(2f, 2f)), Is.True, "a corner is in the band");
            Assert.That(band.IsPointWithinZone(new Float2(36f, 24f)), Is.False, "mid-table is not");

            Assert.That(ReserveRules.IsInReserve(copy), Is.False, "arrived");
            Assert.That(copy.Tokens.HasToken(TokenType.PendingReinforcementArrival), Is.False,
                "the pending marker is spent by arriving");
            Assert.That(copy.Tokens.HasToken(TokenType.ArrivedFromReserve), Is.True,
                "'can't seize or contest objectives on the round they deploy'");
        }

        // ── The transport-spillout arm (was DEFERRED; shipped 2026-07-30) ────────────────────────────────

        // A destroyed transport Shakens its cargo, and that is a real Shaken - so the Shaken arm fires for
        // a spilled-out unit exactly as it does after a failed morale test. The gap was structural, not
        // conditional: TransportUtilities.ApplySpilloutEffects sets the token from the Rules layer, which
        // cannot reach a stage, so nobody ever evaluated Morale_OnShakenApplied on that path.
        [Test]
        public async Task SpilledOutOfADestroyedTransport_OffersTheShakenArm()
        {
            (DataBinding<UnitData> stalkers, DataBinding<ArmyData> army) = MakeArmy();
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, new Position(10f, 10f));
            TransportUtilities.Embark(stalkers.GetValue(), transport.GetValue());
            transport.GetValue().ModelBindings[0].GetValue().DealWounds(
                transport.GetValue().ModelBindings[0].GetValue().TotalWounds);

            var requester = new ReinforceRequester { Accept = true };
            int spilled = await SpilloutExecutor.SpillIfDestroyedTransport(Ctx(requester), transport.GetValue());

            Assert.That(spilled, Is.EqualTo(1), "precondition: the cargo actually spilled out.");
            Assert.That(stalkers.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True,
                "precondition: spilling out Shakens the occupant.");
            Assert.That(requester.YesNoAsked, Is.EqualTo(1),
                "and being Shaken that way must offer Reinforcement - one prompt, like every other " +
                "Shaken moment.");
            Assert.That(stalkers.GetValue().GetIsAlive(), Is.False, "'remove it from the table as destroyed'");
            Assert.That(army.GetValue().UnitBindings, Has.Count.EqualTo(2), "the copy registered");
            Assert.That(army.GetValue().UnitBindings[1].GetValue().Tokens
                .HasToken(TokenType.PendingReinforcementArrival), Is.True);
        }

        // Declining leaves the spilled unit on the table, Shaken, with its choice unspent - the same
        // save-it-for-later semantics the morale path has.
        [Test]
        public async Task SpilledOut_Declined_KeepsTheUnitAndTheChoice()
        {
            (DataBinding<UnitData> stalkers, DataBinding<ArmyData> army) = MakeArmy();
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, new Position(10f, 10f));
            TransportUtilities.Embark(stalkers.GetValue(), transport.GetValue());
            transport.GetValue().ModelBindings[0].GetValue().DealWounds(
                transport.GetValue().ModelBindings[0].GetValue().TotalWounds);

            var requester = new ReinforceRequester { Accept = false };
            await SpilloutExecutor.SpillIfDestroyedTransport(Ctx(requester), transport.GetValue());

            Assert.That(requester.YesNoAsked, Is.EqualTo(1));
            Assert.That(stalkers.GetValue().GetIsAlive(), Is.True, "declining keeps the spilled unit");
            Assert.That(army.GetValue().UnitBindings, Has.Count.EqualTo(1), "and queues no copy");
            Assert.That(stalkers.GetValue().Tokens.HasToken(TokenType.ReinforcementSpent), Is.False,
                "a decline saves the choice for the unit's eventual destruction, it does not spend it");
        }

        // The no-double-prompt guard for the path where BOTH arms are in play: the spillout's
        // dangerous-terrain test finishes off the occupant, so the destroyed arm fires inside
        // PresentSpilloutRolls and the Shaken offer follows in the same pass. Exactly one prompt.
        // Every model rolls a 1 (a wound apiece) and each Stalker is on its last wound, so the unit dies.
        //
        // Measured honestly (mutation-checked): what makes this one prompt is the rule's own
        // Not(TokenPresent(ReinforcementSpent)) gate, which holds whichever arm reaches it first - moving
        // the offer ahead of the wounds still passes. The alive-gate and the after-the-wounds placement in
        // SpilloutExecutor are therefore a correctness/presentation choice (don't evaluate Shaken rules on
        // a corpse, don't remove a unit and then animate wounds on it), NOT what this test pins.
        [Test]
        public async Task SpilloutThatKillsTheOccupant_PromptsOnceViaTheDestroyedArm()
        {
            (DataBinding<UnitData> stalkers, DataBinding<ArmyData> army) = MakeArmy();
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, new Position(10f, 10f));
            TransportUtilities.Embark(stalkers.GetValue(), transport.GetValue());
            transport.GetValue().ModelBindings[0].GetValue().DealWounds(
                transport.GetValue().ModelBindings[0].GetValue().TotalWounds);

            var requester = new ReinforceRequester { Accept = true };
            var ctx = new TriggeredMoveTestContext(_store, requester, new AllOnesDiceRoller(),
                ruleResolver: _resolver);
            await SpilloutExecutor.SpillIfDestroyedTransport(ctx, transport.GetValue());

            Assert.That(stalkers.GetValue().GetIsAlive(), Is.False,
                "precondition: the dangerous-terrain test wiped the spilled unit.");
            Assert.That(requester.YesNoAsked, Is.EqualTo(1),
                "ONE prompt: the destroyed arm fired inside the spillout and spent the gate, so the " +
                "Shaken offer must not add a second.");
            Assert.That(army.GetValue().UnitBindings, Has.Count.EqualTo(2), "exactly one copy queued");
        }

        private DataBinding<UnitData> MakeTransport(string name, int capacity, Position pos)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(new UnitData(_player, name,
                quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>>
                    { _store.GetDataBinding<ModelData>(_store.Create(model)) })));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule(TransportUtilities.TransportRuleName,
                CoreRuleCatalog.Transport, new RuleArgument[] { new RuleArgument.Int(capacity) }));
            return binding;
        }

        // Every die a 1: the batched spillout dangerous-terrain test wounds every model.
        private sealed class AllOnesDiceRoller : IDiceRoller
        {
            public IDiceResults Roll(int sideCount, float rollCount)
            {
                float[] perSide = new float[sideCount];
                perSide[0] = rollCount;
                return new DiceResults(perSide);
            }
        }

        private TriggeredMoveTestContext Ctx(IPlayerRequestByID requester) =>
            new(_store, requester, ruleResolver: _resolver);

        private (DataBinding<UnitData> Unit, DataBinding<ArmyData> Army) MakeArmy()
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 3; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(20f + i, 20f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, "Stalkers", quality: 4, defense: 4,
                modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule(RuleName, Definition()));

            var armyData = new ArmyData(_player, new List<DataBinding<UnitData>> { binding });
            DataBinding<ArmyData> armyBinding = _store.GetDataBinding<ArmyData>(_store.Create(armyData));

            _store.Create(new TeamData(0, new List<PlayerID> { _player }));
            return (binding, armyBinding);
        }

        private sealed class ReinforceRequester : IPlayerRequestByID
        {
            public bool Accept = true;
            public bool ThrowOnYesNo;
            public float DestX = 2f, DestZ = 2f;
            public int YesNoAsked;
            public PlaceObjectsRequest<ModelData>? PlaceRequest;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case YesNoRequest:
                        if (ThrowOnYesNo)
                        {
                            throw new System.InvalidOperationException(
                                "A YesNo was asked where the arrival must be mandatory.");
                        }

                        YesNoAsked++;
                        return Task.FromResult((TReply)(object)Accept);
                    case PlaceObjectsRequest<ModelData> place:
                        PlaceRequest = place;
                        var dest = new Position(DestX, DestZ);
                        var entries = place.ModelsToPlace
                            .Select(m => new PlacedObjectEntry<ModelData>(m, dest))
                            .ToList();
                        return Task.FromResult(
                            (TReply)(object)new Selected<List<PlacedObjectEntry<ModelData>>>(entries));
                    default:
                        throw new System.InvalidOperationException(
                            "Unexpected request: " + request.GetType());
                }
            }
        }
    }
}
