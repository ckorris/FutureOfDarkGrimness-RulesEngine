using System;
using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 A5-10 (owner's reversal of the #335 decline, 2026-08-15): the Tactician loads its
    // transports at deploy time. It is the one profile with a drop-off plan (A5-5 arrival timing,
    // M12 DeliverCargo, #355 disembark-to-charge), so riding beats walking for it - while solo and
    // Gunline keep the #335 decline (TransportDeploymentChoiceTests pins that side). Also covered
    // here: transports deploy before potential cargo (the embark offer only exists for a hold
    // already on the table) and the tightest-fit transport pick.
    [TestFixture]
    public class TacticianDeployEmbarkTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private RuleEvaluator _evaluator = null!;
        private TeamData _team = null!;
        private PlayerID _player;
        private Dictionary<ITeam, DataBinding<RectangularZone>> _zones = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            _player = new PlayerID(Guid.NewGuid());
            _team = new TeamData(0, new List<PlayerID> { _player });

            var zone = new RectangularZone(0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                0f, GameWideConstants.DEPLOYMENT_DISTANCE_INCHES);
            _zones = new Dictionary<ITeam, DataBinding<RectangularZone>>
            {
                [_team] = _store.GetDataBinding<RectangularZone>(_store.Create(zone)),
            };
        }

        // Driven through the REAL ChooseDeployActionStage with the real Tactician resolver - the
        // A5-10 mirror of TransportDeploymentChoiceTests.DeployAction_AiPlayer_NeverEmbarksAndDeploysNormally.
        [Test]
        public async Task DeployAction_Tactician_EmbarksAtDeployment()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6);
            Deploy(transport);
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2);
            MakeArmy(transport, squad);

            var requester = new TacticianRequester(MakeResolver());
            (var deployment, bool finished, bool embarked) =
                await RunChooseDeployAction(requester, squad);

            Assert.That(embarked, Is.True, "the Tactician takes the ride (A5-10).");
            Assert.That(finished, Is.False, "and skips ordinary placement.");
            Assert.That(TransportUtilities.IsEmbarked(squad.GetValue()), Is.True);
            Assert.That(TransportUtilities.GetTransportId(squad.GetValue()),
                Is.EqualTo(transport.GetValue().ID));
            Assert.That(deployment.CurrentDeployingUnit, Is.Null,
                "the embarked unit is set aside, not left pending placement.");
        }

        [Test]
        public async Task EmbarkPrompt_PicksTheTightestFit()
        {
            // Both holds fit the squad; the big one is listed FIRST so an option-0 fallback would
            // take it. Tightest fit picks the small hold, leaving the big one for bigger cargo.
            DataBinding<UnitData> bigHold = MakeTransport("Land Barge", capacity: 8);
            DataBinding<UnitData> smallHold = MakeTransport("Buggy", capacity: 4);
            Deploy(bigHold); Deploy(smallHold);
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2);
            MakeArmy(bigHold, smallHold, squad);
            TacticianUnitSelectionResolver resolver = MakeResolver();

            DataBinding<UnitData> pick = await resolver.Resolve(EmbarkPrompt(squad, bigHold, smallHold));

            Assert.That(pick.Reference, Is.EqualTo(smallHold.Reference),
                "the least remaining capacity that still fits wins");
        }

        [Test]
        public async Task DeployOrder_TransportDeploysBeforeItsCargo()
        {
            // The squad is listed first, so the solo front-of-list order (and the A5-9 tie rule)
            // would deploy it before its ride - and cargo deployed before its transport never gets
            // the embark offer. The A5-10 bias puts the hold on the table first.
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2);
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6);
            MakeArmy(squad, transport);
            TacticianUnitSelectionResolver resolver = MakeResolver();

            DataBinding<UnitData> pick = await resolver.Resolve(new SelectionRequest<UnitData>(
                _player, TacticianUnitSelectionResolver.DeployOrderInstructions,
                new List<SelectionRequest<UnitData>.ValidOption> { new(squad, "Grunts"), new(transport, "Rhino") },
                new List<SelectionRequest<UnitData>.InvalidOption>(), allowCancel: false));

            Assert.That(pick.Reference, Is.EqualTo(transport.Reference),
                "the transport deploys first so later cargo can be offered the ride");
        }

        // G3 fallback discipline: built without a table state (the A0 scaffold shape), the embark
        // prompt falls through to the solo resolver, which declines it (#335).
        [Test]
        public async Task EmbarkPrompt_WithoutTableState_FallsThroughToTheSoloDecline()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6);
            Deploy(transport);
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2);
            MakeArmy(transport, squad);
            var resolver = new TacticianUnitSelectionResolver(
                new TacticianPlanner(_tableState, _evaluator),
                new FDG.Ai.Resolvers.AiSelectionResolver<UnitData>());

            DataBinding<UnitData> pick = await resolver.Resolve(EmbarkPrompt(squad, transport));

            Assert.That(pick, Is.Null, "no table state means no capacity read - the solo decline stands");
        }

        // --- fixtures ---

        private TacticianUnitSelectionResolver MakeResolver() => new(
            new TacticianPlanner(_tableState, _evaluator),
            new FDG.Ai.Resolvers.AiSelectionResolver<UnitData>(), _tableState, _evaluator);

        // The prompt exactly as ChooseDeployActionStage.PromptEmbarkChoice words it - keyed by the
        // DEPLOY_NORMALLY_CHOICE cancel label, the discriminator both AI layers match on.
        private SelectionRequest<UnitData> EmbarkPrompt(DataBinding<UnitData> unit,
            params DataBinding<UnitData>[] transports)
        {
            var options = new List<SelectionRequest<UnitData>.ValidOption>();
            foreach (DataBinding<UnitData> transport in transports)
                options.Add(new(transport, $"Embark into {transport.GetValue().Name}"));

            return new SelectionRequest<UnitData>(_player,
                $"Deploy {unit.GetValue().Name} inside a transport, or on the table?",
                options, new List<SelectionRequest<UnitData>.InvalidOption>(), allowCancel: true,
                displayName: $"Deploying {unit.GetValue().Name}",
                cancelLabel: ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE);
        }

        private async Task<(DeploymentTurnContext Context, bool Finished, bool Embarked)> RunChooseDeployAction(
            IPlayerRequestByID requester, DataBinding<UnitData> currentUnit)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var deployment = new DeploymentTurnContext(ctx, new List<ITeam> { _team }, _zones)
            {
                CurrentDeployingUnit = currentUnit,
            };

            var stage = new ChooseDeployActionStage(ctx, new NoOpLayer<IDeploymentTurnContext>());
            bool finished = false, embarked = false;
            stage.OnFinish.Bind("finish"); stage.OnFinish.OnWillActivate += _ => finished = true;
            stage.OnEmbarked.Bind("embarked"); stage.OnEmbarked.OnWillActivate += _ => embarked = true;

            await stage.Enter(deployment);
            return (deployment, finished, embarked);
        }

        private DataBinding<UnitData> MakeUnit(string name, int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private DataBinding<UnitData> MakeTransport(string name, int capacity)
        {
            DataBinding<UnitData> binding = MakeUnit(name, modelCount: 1);
            binding.GetValue().AttachRuleDefinition(new ResolvedRule(
                TransportUtilities.TransportRuleName, CoreRuleCatalog.Transport,
                new RuleArgument[] { new RuleArgument.Int(capacity) }));
            return binding;
        }

        private static void Deploy(DataBinding<UnitData> unit)
        {
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
                model.GetValue().SetPosition(new Position(10f, 10f));
        }

        private void MakeArmy(params DataBinding<UnitData>[] units)
        {
            _store.Create(new ArmyData(_player, units.ToList()));
        }
    }

    // Routes the stage's embark selection through the REAL Tactician resolver, mirroring
    // TransportDeploymentChoiceTests.AiRequester so the accept is proven end to end.
    internal sealed class TacticianRequester : IPlayerRequestByID
    {
        private readonly TacticianUnitSelectionResolver _resolver;

        public TacticianRequester(TacticianUnitSelectionResolver resolver) => _resolver = resolver;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is SelectionRequest<UnitData> selection)
            {
                return _resolver.Resolve(selection)
                    .ContinueWith(t => (TReply)(object)t.Result!);
            }

            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
