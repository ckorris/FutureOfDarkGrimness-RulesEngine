using System;
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
    // Vertical-slice integration test for #035 slice B: deploy-time embarking, driven through the real
    // ChooseDeployActionStage (the stage the codebase earmarked for "place in a Transport"). When the unit
    // being deployed could ride a friendly transport already on the table, the owner is offered the choice:
    //  - Embark: stamp an EmbarkedIn token, set the unit aside off-table, fire OnEmbarked (skip placement).
    //  - Deploy normally (cancel): fire OnFinish toward placement, no token.
    //  - No eligible transport (none deployed / over capacity): no prompt at all, OnFinish as before.
    [TestFixture]
    public class TransportDeploymentChoiceTests
    {
        private GameDataStore _store = null!;
        private TeamData _team = null!;
        private PlayerID _player;
        private Dictionary<ITeam, DataBinding<RectangularZone>> _zones = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
            _team = new TeamData(0, new List<PlayerID> { _player });

            var zone = new RectangularZone(0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                0f, GameWideConstants.DEPLOYMENT_DISTANCE_INCHES);
            _zones = new Dictionary<ITeam, DataBinding<RectangularZone>>
            {
                [_team] = _store.GetDataBinding<RectangularZone>(_store.Create(zone)),
            };
        }

        [Test]
        public async Task DeployAction_NoTransport_DeploysNormallyWithoutPrompt()
        {
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2);
            MakeArmy(squad);

            var requester = new EmbarkChoiceRequester(pickTransport: null);
            (var deployment, bool finished, bool embarked) = await RunChooseDeployAction(requester, squad);

            Assert.That(requester.SelectionPromptCount, Is.EqualTo(0), "no transport on the table → no embark prompt.");
            Assert.That(finished, Is.True, "with nothing to embark, the unit deploys normally (OnFinish).");
            Assert.That(embarked, Is.False);
            Assert.That(deployment.CurrentDeployingUnit, Is.EqualTo(squad), "the unit stays current, headed for placement.");
        }

        [Test]
        public async Task DeployAction_EmbarkChosen_SetsUnitAsideOffTable()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6);
            Deploy(transport);
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2);
            MakeArmy(transport, squad);

            var requester = new EmbarkChoiceRequester(pickTransport: transport);
            (var deployment, bool finished, bool embarked) = await RunChooseDeployAction(requester, squad);

            Assert.That(embarked, Is.True, "embarking fires OnEmbarked (skips placement).");
            Assert.That(finished, Is.False, "embarking does not fire OnFinish.");
            Assert.That(TransportUtilities.IsEmbarked(squad.GetValue()), Is.True);
            Assert.That(TransportUtilities.GetTransportId(squad.GetValue()), Is.EqualTo(transport.GetValue().ID),
                "the EmbarkedIn token is owned by the chosen transport.");
            Assert.That(squad.GetValue().GetIsOnBattlefield(), Is.False, "an embarked unit stays off-table at origin.");
            Assert.That(deployment.CurrentDeployingUnit, Is.Null, "the embarked unit is no longer the current deploying unit.");
        }

        [Test]
        public async Task DeployAction_DeployNormallyChosen_ProceedsToPlacement()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6);
            Deploy(transport);
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2);
            MakeArmy(transport, squad);

            // An eligible transport exists, so the choice is offered — but the player cancels (deploy normally).
            var requester = new EmbarkChoiceRequester(pickTransport: null);
            (var deployment, bool finished, bool embarked) = await RunChooseDeployAction(requester, squad);

            Assert.That(requester.SelectionPromptCount, Is.EqualTo(1), "an eligible transport is offered.");
            Assert.That(finished, Is.True, "cancelling the embark choice deploys normally (OnFinish).");
            Assert.That(embarked, Is.False);
            Assert.That(TransportUtilities.IsEmbarked(squad.GetValue()), Is.False);
            Assert.That(deployment.CurrentDeployingUnit, Is.EqualTo(squad), "the unit stays current, headed for placement.");
        }

        // #331: "deploy normally" used to be reachable only by pressing Back, which reads as "I picked the
        // wrong unit", not as one of the two ways to deploy. The choice now names itself on the request, so
        // both front ends label the button (and the CLI's [0] row) with what it actually does.
        [Test]
        public async Task DeployAction_EmbarkPrompt_NamesTheDeployNormallyChoice()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6);
            Deploy(transport);
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2);
            MakeArmy(transport, squad);

            var requester = new EmbarkChoiceRequester(pickTransport: null);
            await RunChooseDeployAction(requester, squad);

            SelectionRequest<UnitData> prompt = requester.LastSelection!;
            Assert.That(prompt.AllowCancel, Is.True, "declining the transport has to stay reachable.");
            Assert.That(prompt.CancelLabel, Is.EqualTo(ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE),
                "the exit is a real deployment choice, so it is labelled with what it does, not 'Back'.");
            Assert.That(prompt.Instructions, Does.Not.Contain("Cancel"),
                "the instructions no longer have to explain the Back button's hidden second meaning.");
            Assert.That(prompt.DisplayName, Does.Contain("Grunts"),
                "the #322 'Waiting on' HUD names the unit rather than the C# type.");
        }

        // #331 (owner's call, 2026-08-04): the AI never embarks. Riding needs forethought it doesn't have -
        // it has no policy for where the cargo gets out - and before this the option-0 fallback embarked
        // every eligible unit because "Embark into Rhino" was simply first in the list. Driven through the
        // REAL AiSelectionResolver against the real stage, so the decline is proven end to end rather than
        // in the resolver alone.
        [Test]
        public async Task DeployAction_AiPlayer_NeverEmbarksAndDeploysNormally()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6);
            Deploy(transport);
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2);
            MakeArmy(transport, squad);

            var requester = new AiRequester();
            (var deployment, bool finished, bool embarked) = await RunChooseDeployAction(requester, squad);

            Assert.That(embarked, Is.False, "the AI declines the transport.");
            Assert.That(finished, Is.True, "and heads for ordinary placement instead.");
            Assert.That(TransportUtilities.IsEmbarked(squad.GetValue()), Is.False);
            Assert.That(deployment.CurrentDeployingUnit, Is.EqualTo(squad));
        }

        // Every other cancellable selection keeps the plain Back wording — the label is opt-in, so a stage
        // that says nothing gets exactly the button it had before.
        [Test]
        public void SelectionRequest_WithoutAnExplicitLabel_StillSaysBack()
        {
            var request = new SelectionRequest<UnitData>(_player, "Pick one.",
                new List<SelectionRequest<UnitData>.ValidOption>(),
                new List<SelectionRequest<UnitData>.InvalidOption>(), allowCancel: true);

            Assert.That(request.CancelLabel, Is.EqualTo(SelectionRequest<UnitData>.DEFAULT_CANCEL_LABEL));
        }

        [Test]
        public async Task DeployAction_UndeployedTransport_NotOffered()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6); // NOT deployed (at origin)
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2);
            MakeArmy(transport, squad);

            var requester = new EmbarkChoiceRequester(pickTransport: null);
            (_, bool finished, bool embarked) = await RunChooseDeployAction(requester, squad);

            Assert.That(requester.SelectionPromptCount, Is.EqualTo(0), "a transport that isn't on the table yet can't be loaded.");
            Assert.That(finished, Is.True);
            Assert.That(embarked, Is.False);
        }

        [Test]
        public async Task DeployAction_OverCapacityTransport_NotOffered()
        {
            DataBinding<UnitData> transport = MakeTransport("Buggy", capacity: 1); // only 1 space
            Deploy(transport);
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2); // needs 2 spaces
            MakeArmy(transport, squad);

            var requester = new EmbarkChoiceRequester(pickTransport: null);
            (_, bool finished, bool embarked) = await RunChooseDeployAction(requester, squad);

            Assert.That(requester.SelectionPromptCount, Is.EqualTo(0), "a transport without room is not offered.");
            Assert.That(finished, Is.True);
            Assert.That(embarked, Is.False);
        }

        // Runs ChooseDeployActionStage once with the given unit set as the current deploying unit (as
        // ChooseUnitToDeployStage would have done), returning the context plus which outgoing binding fired.
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

        // Places a unit's models on the table (non-origin), so it counts as deployed / on the battlefield.
        private static void Deploy(DataBinding<UnitData> unit)
        {
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                model.GetValue().SetPosition(new Position(10f, 10f));
            }
        }

        private void MakeArmy(params DataBinding<UnitData>[] units)
        {
            _store.Create(new ArmyData(_player, units.ToList()));
        }
    }

    // Routes the stage's requests through the production AI resolver, so a test exercises what an AI player
    // actually does rather than a hand-written stand-in (#331).
    internal sealed class AiRequester : IPlayerRequestByID
    {
        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is SelectionRequest<UnitData> selection)
            {
                return new FDG.Ai.Resolvers.AiSelectionResolver<UnitData>().Resolve(selection)
                    .ContinueWith(t => (TReply)(object)t.Result!);
            }

            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }

    // Answers the deploy-time embark selection: returns the chosen transport binding, or null (cancel) to
    // deploy normally. Records how many embark prompts it was shown so tests can assert the choice was (or
    // was not) offered.
    internal sealed class EmbarkChoiceRequester : IPlayerRequestByID
    {
        private readonly DataBinding<UnitData>? _pickTransport;

        public int SelectionPromptCount { get; private set; }

        /// <summary>The last embark prompt seen, so a test can assert on how it was worded (#331).</summary>
        public SelectionRequest<UnitData>? LastSelection { get; private set; }

        public EmbarkChoiceRequester(DataBinding<UnitData>? pickTransport) => _pickTransport = pickTransport;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is SelectionRequest<UnitData> selection)
            {
                SelectionPromptCount++;
                LastSelection = selection;

                if (_pickTransport == null)
                {
                    // Cancel = deploy normally; the resolver contract returns null on cancel.
                    return Task.FromResult((TReply)(object)null!);
                }

                SelectionRequest<UnitData>.ValidOption picked =
                    selection.ValidOptions.First(option => option.Option == _pickTransport);
                return Task.FromResult((TReply)(object)picked.Option);
            }

            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
