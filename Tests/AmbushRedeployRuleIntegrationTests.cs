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
    // #197 P22: Ambush Re-Deployment - "once per game, when a unit where all models have this rule ends
    // its activation, you may immediately remove it from the table (dropping any objectives it might hold
    // within 1\"), and deploy it as if it had Ambush at the beginning of the next round." Owner-ruled
    // 2026-07-28: the return is MANDATORY - the next round start PLACES the unit without asking; only the
    // spot is the player's.
    //
    // The rule is two halves that meet on a token: an end-of-activation ability whose executable removal
    // stamps PendingAmbushArrival, and a deferDeployment entry GATED on that token, so the ordinary
    // round-start arrival pass finds the return leg with no special case.
    [TestFixture]
    public class AmbushRedeployRuleIntegrationTests
    {
        private const string RuleName = "Ambush Re-Deployment";

        private static SpecialRuleDefinition Definition() => new(RuleName,
            new[]
            {
                new HookEntry(EHookID.Deployment_OnPreDeploymentSelect,
                    new Condition.TokenPresent(TokenType.PendingAmbushArrival),
                    new Effect.DeferDeployment(EDeferTiming.LaterRound, PlacementRangeInches: 9f,
                        MandatoryArrival: true),
                    ELifetime.UntilEndOfGame),
            },
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnEndOfActivation,
                    new Cost.OncePerGame(),
                    new TargetSelector(RangeInches: 0f, MinCount: 1, MaxCount: 1, ETargetAffinity.Self,
                        RequireLineOfSight: false),
                    new Effect.AmbushRedeploy(),
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
        public async Task Accepted_RemovesTheUnit_AndTheReturnIsMandatoryNextRoundStart()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10f, 10f));
            var requester = new RedeployRequester { AnswerToUse = true };

            await RunEndOfActivation(unit, requester);

            Assert.That(requester.YesNoAsked, Is.EqualTo(1), "the removal is offered, not forced");
            Assert.That(ReserveRules.IsInReserve(unit.GetValue()), Is.True, "removed from the table");
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.PendingAmbushArrival), Is.True);
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                Position pos = model.GetValue().PositionBinding.GetValue();
                Assert.That(pos.x == 0f && pos.z == 0f, Is.True, "models park at the unplaced sentinel");
            }

            // The beginning of the next round: the return is PLACED, never offered - a YesNo here fails
            // the test outright (owner-ruled mandatory).
            requester.ThrowOnYesNo = true;
            await RunRoundStart(requester, roundCount: 2);

            Assert.That(requester.PlaceRequest, Is.Not.Null, "the arrival placement was requested");
            Assert.That(requester.PlaceRequest!.MinDistanceFromEnemiesInches, Is.EqualTo(9f).Within(0.001f),
                "'as if it had Ambush' - the over-9\" constraint holds");
            Assert.That(ReserveRules.IsInReserve(unit.GetValue()), Is.False, "it is back on the table");
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.ArrivedFromReserve), Is.True,
                "an arrival is an arrival - it can't seize objectives the round it returns");
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.PendingAmbushArrival), Is.False,
                "the pending-return marker is spent by arriving");

            // Once per game: its next activation-end offers nothing.
            requester.ThrowOnYesNo = false;
            await RunEndOfActivation(unit, requester);
            Assert.That(requester.YesNoAsked, Is.EqualTo(1), "the once-per-game gate is spent");
        }

        [Test]
        public async Task Declined_StaysOnTable_AndTheOncePerGameIsNotSpent()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10f, 10f));
            var requester = new RedeployRequester { AnswerToUse = false };

            await RunEndOfActivation(unit, requester);

            Assert.That(ReserveRules.IsInReserve(unit.GetValue()), Is.False, "declining changes nothing");
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.PendingAmbushArrival), Is.False);

            requester.AnswerToUse = true;
            await RunEndOfActivation(unit, requester);

            Assert.That(requester.YesNoAsked, Is.EqualTo(2), "declining did not spend the gate");
            Assert.That(ReserveRules.IsInReserve(unit.GetValue()), Is.True);
        }

        [Test]
        public async Task Removal_DropsOnlyTheObjectiveItsSideHoldsWithinOneInch()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10f, 10f));
            var enemy = new PlayerID(System.Guid.NewGuid());

            ObjectiveData heldNear = CreateObjective(new Position(10.8f, 10f));   // ~0.05" from the base edge
            heldNear.SetOwner(_player);
            ObjectiveData enemyNear = CreateObjective(new Position(10f, 10.8f));  // near, but not ours
            enemyNear.SetOwner(enemy);
            ObjectiveData heldFar = CreateObjective(new Position(20f, 10f));      // ours, but out of reach
            heldFar.SetOwner(_player);

            await RunEndOfActivation(unit, new RedeployRequester { AnswerToUse = true });

            Assert.That(heldNear.OwnerID, Is.Null,
                "'dropping any objectives it might hold within 1\"'");
            Assert.That(enemyNear.OwnerID, Is.EqualTo(enemy),
                "an enemy's marker is not the unit's to drop");
            Assert.That(heldFar.OwnerID, Is.EqualTo(_player),
                "a marker seized earlier and left behind stays seized - only within 1\" drops");
        }

        [Test]
        public async Task AUnitWipedOutDuringItsOwnActivation_IsNeverOffered()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10f, 10f));
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                model.GetValue().DealWounds(model.GetValue().TotalWounds);
            }

            var requester = new RedeployRequester { AnswerToUse = true, ThrowOnYesNo = true };
            await RunEndOfActivation(unit, requester);

            Assert.That(requester.YesNoAsked, Is.EqualTo(0),
                "a destroyed unit has no end-of-activation choices to make");
        }

        private async Task RunEndOfActivation(DataBinding<UnitData> unit, RedeployRequester requester)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var turn = new SingleTurnContext(ctx, unit.GetValue().PlayerID,
                new List<DataBinding<UnitData>> { unit });
            turn.ChooseUnitToActivate(unit);

            var stage = new ReconcileEndOfActivationStage(ctx, new NoOpLayer<ISingleTurnContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(turn);
        }

        private async Task RunRoundStart(RedeployRequester requester, int roundCount)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var stage = new StartOfRoundExtraActionStage(ctx, new NoOpLayer<IMainPhaseContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(new TestMainPhaseContext(ctx, roundCount));
        }

        private DataBinding<UnitData> MakeUnit(Position around)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var model = new ModelData(0.75f, new List<Weapon>(),
                    new Position(around.x + i, around.z), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, "Harlequins", quality: 4, defense: 4,
                modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule(RuleName, Definition()));

            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private ObjectiveData CreateObjective(Position position)
        {
            var obj = new ObjectiveData(position, _store);
            _store.Create(obj);
            return obj;
        }

        // Answers the end-of-activation "Use ...?" with a fixed choice and the mandatory-return placement
        // by dropping every model at a fixed destination; counts YesNo prompts, and can be armed to FAIL
        // on any YesNo - the observable for "the return was placed, never offered".
        private sealed class RedeployRequester : IPlayerRequestByID
        {
            public bool AnswerToUse;
            public bool ThrowOnYesNo;
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
                        return Task.FromResult((TReply)(object)AnswerToUse);
                    case PlaceObjectsRequest<ModelData> place:
                        PlaceRequest = place;
                        var dest = new Position(30f, 30f);
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
