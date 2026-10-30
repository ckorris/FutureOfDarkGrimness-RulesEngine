using System.Text.Json;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #434 - OPR 3.6 rewrote the activation-time reposition family as a MOVE: Bounding / Dash "you may move all models
    // with this rule in it by up to D3+1"", Nimble / Rapid Blink / Wolfborn "by up to D3"" (3.5: "place ... anywhere fully
    // within"). Effect.RepositionAtActivation gained Move: the rolled distance becomes one optional triggered move
    // (GameOperationServices.MoveUnit, the post-combat family's movement request - terrain, other units, coherency),
    // folded by the same two stages that fold the placement: ActivationStartStage (Bounding & co.) and
    // ReconcileEndOfActivationStage (Dash). The placement form (Move = false, what old data parses to) is unchanged
    // and still pinned by RepositionAtActivationTests / DashRuleIntegrationTests.
    [TestFixture]
    public class ActivationMoveRuleIntegrationTests
    {
        // D3 on a die fixed to 2: 2" (Wolfborn) or, with the +1, 3" (Bounding).
        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(System.Guid.NewGuid());
        }

        private static SpecialRuleDefinition ActivationRule(string name, int plusInches, bool move) => new(name,
            new[]
            {
                new HookEntry(EHookID.Activation_OnActivationStart, new Condition.MostModelsHaveThisRule(),
                    new Effect.RepositionAtActivation(new DiceExpression.D3(), plusInches, Move: move),
                    ELifetime.ThisActivation),
            },
            System.Array.Empty<ActivatedAbility>());

        private static SpecialRuleDefinition DashRule(bool move) => new("Dash",
            System.Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnEndOfActivation, new Cost.OncePerRound(),
                    new TargetSelector(0f, 1, 1, ETargetAffinity.Self, RequireLineOfSight: false),
                    new Effect.RepositionAtActivation(new DiceExpression.D3(), PlusInches: 1, Move: move),
                    new Condition.MostModelsHaveThisRule(), Label: "Dash"),
            });

        [Test]
        public async Task Bounding_AtActivationStart_OffersAMove_NotAPlacement()
        {
            DataBinding<UnitData> unit = MakeUnit(ActivationRule("Bounding", plusInches: 1, move: true));
            var requester = new MoveOrPlaceRequester { Dx = 2.5f };

            await RunActivationStart(unit, requester);

            Assert.That(requester.Move, Is.Not.Null, "a movement-path request, the triggered move's");
            Assert.That(requester.Place, Is.Null, "no placement: 3.6 moves the models");
            Assert.That(requester.Move!.MaxDistanceInches, Is.EqualTo(3f).Within(0.001f), "D3 (fixed to 2) + 1");
            Assert.That(requester.Move.AllowCancel, Is.True, "'you MAY move'");
            AssertAllModelsAtX(unit, 10f + 2.5f, "the accepted path moved the models");
        }

        [Test]
        public async Task Wolfborn_AMoveOverTheBudget_IsDeclined_AndTheUnitStays()
        {
            // A real move is validated against its budget (a placement only checks each destination's radius).
            DataBinding<UnitData> unit = MakeUnit(ActivationRule("Wolfborn", plusInches: 0, move: true));
            var requester = new MoveOrPlaceRequester { Dx = 6f };

            await RunActivationStart(unit, requester);

            Assert.That(requester.Move!.MaxDistanceInches, Is.EqualTo(2f).Within(0.001f), "D3 (fixed to 2)");
            AssertAllModelsAtX(unit, 10f, "a 6\" path on a 2\" budget is rejected; an optional move declines");
        }

        [Test]
        public async Task Wolfborn_Cancelled_LeavesTheUnitWhereItWas()
        {
            DataBinding<UnitData> unit = MakeUnit(ActivationRule("Wolfborn", plusInches: 0, move: true));
            var requester = new MoveOrPlaceRequester { Cancel = true };

            await RunActivationStart(unit, requester);

            Assert.That(requester.Move, Is.Not.Null);
            AssertAllModelsAtX(unit, 10f, "declined");
        }

        [Test]
        public async Task ThePlacementForm_IsUnchanged()
        {
            // Move = false is the 3.5 shape a saved list or bundled 3.5 book still carries.
            DataBinding<UnitData> unit = MakeUnit(ActivationRule("Wolfborn", plusInches: 0, move: false));
            var requester = new MoveOrPlaceRequester { Cancel = true };

            await RunActivationStart(unit, requester);

            Assert.That(requester.Place, Is.Not.Null);
            Assert.That(requester.Move, Is.Null);
        }

        [Test]
        public async Task AMoveCopy_AndAPlacementCopy_FoldIntoOneMove_ForTheSummedDistance()
        {
            var unitRule = ActivationRule("Rapid Blink", plusInches: 0, move: true);
            DataBinding<UnitData> unit = MakeUnit(unitRule);
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Rapid Blink (placement)",
                ActivationRule("Rapid Blink (placement)", plusInches: 0, move: false)));
            var requester = new MoveOrPlaceRequester { Dx = 3f };

            await RunActivationStart(unit, requester);

            Assert.That(requester.MovesAsked, Is.EqualTo(1), "one prompt");
            Assert.That(requester.Place, Is.Null);
            Assert.That(requester.Move!.MaxDistanceInches, Is.EqualTo(4f).Within(0.001f), "D3 + D3, both fixed to 2");
            AssertAllModelsAtX(unit, 13f);
        }

        [Test]
        public async Task Dash_AtTheEndOfTheActivation_OffersTheMove_WithNoYesNo()
        {
            DataBinding<UnitData> unit = MakeUnit(DashRule(move: true));
            var requester = new MoveOrPlaceRequester { Dx = 3f, ThrowOnYesNo = true };

            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(2));
            var turn = new SingleTurnContext(ctx, unit.GetValue().PlayerID, new List<DataBinding<UnitData>> { unit });
            turn.ChooseUnitToActivate(unit);
            var stage = new ReconcileEndOfActivationStage(ctx, new NoOpLayer<ISingleTurnContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(turn);

            Assert.That(requester.Move, Is.Not.Null);
            Assert.That(requester.Move!.MaxDistanceInches, Is.EqualTo(3f).Within(0.001f));
            AssertAllModelsAtX(unit, 13f);
        }

        [Test]
        public void TheMoveFlag_RoundTripsThroughJson_AndOldDataParsesAsAPlacement()
        {
            Effect move = new Effect.RepositionAtActivation(new DiceExpression.D3(), PlusInches: 1, Move: true);
            string json = JsonSerializer.Serialize(move, RuleJson.Options);
            Assert.That(json, Does.Contain("\"move\":true").Or.Contain("\"move\": true"));
            Assert.That(JsonSerializer.Deserialize<Effect>(json, RuleJson.Options), Is.EqualTo(move));

            // A 3.5 book / saved list: no "move" field.
            string legacy = "{\"kind\":\"repositionAtActivation\",\"distance\":{\"kind\":\"d3\"},\"plusInches\":1}";
            Assert.That(JsonSerializer.Deserialize<Effect>(legacy, RuleJson.Options),
                Is.EqualTo(new Effect.RepositionAtActivation(new DiceExpression.D3(), PlusInches: 1, Move: false)));
        }

        [Test]
        public void TheOperation_DescribesAMove()
        {
            Assert.That(new RuleOperation.RepositionModels(3f, AsMove: true).Describe(),
                Is.EqualTo("may move its models up to 3\""));
        }

        // --- helpers --------------------------------------------------------------------------------------------

        private async Task RunActivationStart(DataBinding<UnitData> unit, IPlayerRequestByID requester)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(2));
            var unitContext = new UnitActionContext(ctx, unit);
            unitContext.Reset(unit);
            var stage = new ActivationStartStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(unitContext);
        }

        private DataBinding<UnitData> MakeUnit(SpecialRuleDefinition rule)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(10f, 10f + i * 1.2f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, "Hunters", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private static void AssertAllModelsAtX(DataBinding<UnitData> unit, float x, string because = "")
        {
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
                Assert.That(model.GetValue().PositionBinding.GetValue().x, Is.EqualTo(x).Within(0.001f), because);
        }

        // Answers a movement-path request by translating every model Dx along x (or cancelling), records a placement
        // request (cancelling it), and throws on a Yes/No when asked to.
        private sealed class MoveOrPlaceRequester : IPlayerRequestByID
        {
            public float Dx;
            public bool Cancel;
            public bool ThrowOnYesNo;
            public int MovesAsked;
            public DefineMovementPathRequest? Move;
            public PlaceObjectsRequest<ModelData>? Place;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case YesNoRequest:
                        if (ThrowOnYesNo) throw new System.InvalidOperationException("A Yes/No was asked.");
                        return Task.FromResult((TReply)(object)true);
                    case DefineMovementPathRequest move:
                        MovesAsked++;
                        Move = move;
                        if (Cancel)
                            return Task.FromResult((TReply)(object)new Cancelled<List<ModelMoveEntry>>());
                        var entries = new List<ModelMoveEntry>();
                        foreach (DataBinding<ModelData> model in move.UnitDataBinding.GetValue().ModelBindings)
                        {
                            Position start = model.GetValue().PositionBinding.GetValue();
                            entries.Add(new ModelMoveEntry(model, new List<Position> { new Position(start.x + Dx, start.z) }));
                        }
                        return Task.FromResult((TReply)(object)new Selected<List<ModelMoveEntry>>(entries));
                    case PlaceObjectsRequest<ModelData> place:
                        Place = place;
                        return Task.FromResult((TReply)(object)new Cancelled<List<PlacedObjectEntry<ModelData>>>());
                    default:
                        throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
                }
            }
        }
    }
}
