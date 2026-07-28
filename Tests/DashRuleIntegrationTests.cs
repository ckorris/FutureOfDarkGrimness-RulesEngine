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
    // #197 Dash - "once per round, when a unit where all models have this rule ends its activation, it may
    // place all its models anywhere fully within D3+1in of their position." Reposition-at-activation's twin
    // at the far end of the activation: the EFFECT is Bounding's body verbatim (repositionAtActivation, D3,
    // +1), so what is asserted here is the trigger, the gate, and the prompting - not the placement maths,
    // which RepositionAtActivationTests already covers.
    //
    // The prompting is the owner call this slice turned on (2026-07-28). ReconcileEndOfActivationStage asks
    // a single-ability rule a Yes/No that DEFAULTS TO NO - right for Ambush Re-Deployment's once-per-game
    // self-removal, wrong for a free repositioning buff, which would then be double-prompted for a human and
    // permanently declined by every automated resolver. An ability whose effect is itself a cancellable
    // placement therefore skips that Yes/No: the placement IS the "you may".
    [TestFixture]
    public class DashRuleIntegrationTests
    {
        private const string RuleName = "Dash";

        // D3 rolled on a die fixed to 2, plus 1 -> a 3in radius. Pinning the exact number keeps the test
        // honest about the +1 (a dropped PlusInches would read as 2in).
        private const float ExpectedRadiusInches = 3f;

        private static SpecialRuleDefinition Definition() => new(RuleName,
            System.Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnEndOfActivation,
                    new Cost.OncePerRound(),
                    new TargetSelector(RangeInches: 0f, MinCount: 1, MaxCount: 1, ETargetAffinity.Self,
                        RequireLineOfSight: false),
                    new Effect.RepositionAtActivation(new DiceExpression.D3(), PlusInches: 1),
                    new Condition.AllModelsHaveThisRule(),
                    Label: RuleName),
            });

        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(System.Guid.NewGuid());
        }

        [Test]
        public async Task EndingAnActivation_OffersThePlacementDirectly_WithNoYesNoInFrontOfIt()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10f, 10f), onAllModels: true);
            var requester = new DashRequester { Destination = new Position(12f, 10f), ThrowOnYesNo = true };

            await RunEndOfActivation(unit, requester);

            Assert.That(requester.PlaceRequest, Is.Not.Null,
                "the reposition placement is offered at the end of the activation");
            Assert.That(requester.PlaceRequest!.MaxDistanceFromStartInches,
                Is.EqualTo(ExpectedRadiusInches).Within(0.001f),
                "D3 (fixed to 2) + 1 - a per-model radius, not a table-wide placement");
            Assert.That(requester.YesNoAsked, Is.EqualTo(0),
                "the placement's own cancel IS the 'you may'; asking a Yes/No first would double-prompt " +
                "the player and, defaulting to NO, hide the rule from every automated resolver");

            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                Assert.That(model.GetValue().PositionBinding.GetValue().x, Is.EqualTo(12f).Within(0.001f),
                    "the accepted placement moved the models");
            }
        }

        [Test]
        public async Task TheOncePerRoundGate_IsSpentByUsingIt()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10f, 10f), onAllModels: true);
            var requester = new DashRequester { Destination = new Position(12f, 10f) };

            await RunEndOfActivation(unit, requester);
            Assert.That(requester.PlaceAsked, Is.EqualTo(1));

            // A second activation in the same round (a reactivate, which is the only way this happens) is
            // offered nothing. Without the once-per-round cost the rule would fire on every activation.
            await RunEndOfActivation(unit, requester);
            Assert.That(requester.PlaceAsked, Is.EqualTo(1), "the once-per-round gate is spent");
        }

        [Test]
        public async Task DecliningThePlacement_LeavesTheUnitWhereItWas()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10f, 10f), onAllModels: true);
            var requester = new DashRequester { Cancel = true };

            await RunEndOfActivation(unit, requester);

            Assert.That(requester.PlaceAsked, Is.EqualTo(1), "it was offered");
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                Assert.That(model.GetValue().PositionBinding.GetValue().x, Is.EqualTo(10f).Within(1.001f),
                    "declining leaves every model put");
            }

            // Recorded consequence of the signed-off no-Yes/No shape: the cost is emitted when the ability
            // RESOLVES, before the placement is offered, so backing out still spends the round's use. Same
            // as Vanguard / Fanatic at deployment today. Pinned so a change of heart is a failing test, not
            // a silent behaviour drift.
            requester.Cancel = false;
            await RunEndOfActivation(unit, requester);
            Assert.That(requester.PlaceAsked, Is.EqualTo(1),
                "cancelling still spent the once-per-round use");
        }

        [Test]
        public async Task AUnitWhereNotEveryModelHasTheRule_IsNotOffered()
        {
            // #267: a unit-wide reposition must gate on AllModelsHaveThisRule, or one joined hero's copy
            // would move the whole squad. The offer is gathered from the carrying MODEL, so only the
            // ability's availability condition can stop it.
            DataBinding<UnitData> unit = MakeUnit(new Position(10f, 10f), onAllModels: false);
            var requester = new DashRequester { Destination = new Position(12f, 10f), ThrowOnYesNo = true };

            await RunEndOfActivation(unit, requester);

            Assert.That(requester.PlaceAsked, Is.EqualTo(0),
                "'a unit where all models have this rule' - one model's copy does not move the squad");
        }

        private async Task RunEndOfActivation(DataBinding<UnitData> unit, DashRequester requester)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(2));
            var turn = new SingleTurnContext(ctx, unit.GetValue().PlayerID,
                new List<DataBinding<UnitData>> { unit });
            turn.ChooseUnitToActivate(unit);

            var stage = new ReconcileEndOfActivationStage(ctx, new NoOpLayer<ISingleTurnContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(turn);
        }

        private DataBinding<UnitData> MakeUnit(Position around, bool onAllModels)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var model = new ModelData(0.75f, new List<Weapon>(),
                    new Position(around.x + i, around.z), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, "Wardens", quality: 4, defense: 4,
                modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            var resolved = new ResolvedRule(RuleName, Definition());
            if (onAllModels) binding.GetValue().AttachRuleDefinition(resolved);
            else modelBindings[0].GetValue().AttachRuleDefinition(resolved);

            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // Answers the reposition placement by dropping every model at a fixed destination (or cancelling),
        // and counts both prompt kinds. ThrowOnYesNo is the observable for "no Yes/No gate in front of the
        // placement" - a reintroduced gate fails the test outright rather than quietly changing a count.
        private sealed class DashRequester : IPlayerRequestByID
        {
            public Position Destination;
            public bool Cancel;
            public bool ThrowOnYesNo;
            public int YesNoAsked;
            public int PlaceAsked;
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
                                "A YesNo was asked in front of a cancellable placement.");
                        }

                        YesNoAsked++;
                        return Task.FromResult((TReply)(object)true);
                    case PlaceObjectsRequest<ModelData> place:
                        PlaceAsked++;
                        PlaceRequest = place;
                        if (Cancel)
                        {
                            return Task.FromResult(
                                (TReply)(object)new Cancelled<List<PlacedObjectEntry<ModelData>>>());
                        }

                        var entries = place.ModelsToPlace
                            .Select(m => new PlacedObjectEntry<ModelData>(m, Destination))
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
