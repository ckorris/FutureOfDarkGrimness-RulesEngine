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
    //  - No eligible transport (none / over capacity): no prompt at all, OnFinish as before.
    //  - #406: the transport need not be on the table - one in Ambush or not yet deployed is offered too,
    //    and its cargo stays out of play until the transport arrives.
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

        // #335: "deploy normally" used to be reachable only by pressing Back, which reads as "I picked the
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

        // #191 A5-10 (owner's reversal of the 2026-08-04 #335 decline): every AI profile embarks at
        // deploy time - "during deployment, it's almost always best to put something in transports."
        // The forethought #335 said was missing now exists: AiStringSelectionResolver.ShouldDisembark
        // is the solo get-out rule (the Tactician has A5-5). Driven through the REAL
        // AiSelectionResolver against the real stage, so the accept is proven end to end.
        [Test]
        public async Task DeployAction_AiPlayer_EmbarksAtDeployment()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6);
            Deploy(transport);
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2);
            MakeArmy(transport, squad);

            var requester = new AiRequester();
            (var deployment, bool finished, bool embarked) = await RunChooseDeployAction(requester, squad);

            Assert.That(embarked, Is.True, "the AI takes the ride (#191 A5-10).");
            Assert.That(finished, Is.False, "and skips ordinary placement.");
            Assert.That(TransportUtilities.IsEmbarked(squad.GetValue()), Is.True);
            Assert.That(deployment.CurrentDeployingUnit, Is.Null);
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

        // #406: a transport that hasn't been placed yet is still a ride. The cargo is set aside now and goes
        // wherever the transport goes - onto the table when it deploys, or into Ambush with it.
        [Test]
        public async Task DeployAction_UndeployedTransport_IsOfferedAndLoads()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6); // NOT deployed (at origin)
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2);
            MakeArmy(transport, squad);

            var requester = new EmbarkChoiceRequester(pickTransport: transport);
            (var deployment, bool finished, bool embarked) = await RunChooseDeployAction(requester, squad);

            Assert.That(requester.SelectionPromptCount, Is.EqualTo(1), "an undeployed transport is offered.");
            Assert.That(requester.LastSelection!.ValidOptions.Single().Name, Is.EqualTo("Embark into Rhino (not yet deployed)"),
                "the option says the transport isn't on the table yet.");
            Assert.That(embarked, Is.True);
            Assert.That(finished, Is.False);
            Assert.That(TransportUtilities.GetTransportId(squad.GetValue()), Is.EqualTo(transport.GetValue().ID));
            Assert.That(deployment.CurrentDeployingUnit, Is.Null);
        }

        // #406: the Discord report - a transport held in Ambush could not be loaded at all.
        [Test]
        public async Task DeployAction_TransportInAmbush_IsOfferedAndLoads()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6);
            ReserveRules.PlaceInReserve(transport.GetValue());
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2);
            MakeArmy(transport, squad);

            var requester = new EmbarkChoiceRequester(pickTransport: transport);
            (_, bool finished, bool embarked) = await RunChooseDeployAction(requester, squad);

            Assert.That(requester.LastSelection!.ValidOptions.Single().Name, Is.EqualTo("Embark into Rhino (in reserve)"));
            Assert.That(embarked, Is.True, "a unit can board a transport waiting in Ambush.");
            Assert.That(finished, Is.False);
            Assert.That(TransportUtilities.GetTransportId(squad.GetValue()), Is.EqualTo(transport.GetValue().ID));
        }

        // #406: an off-table transport's room is counted the same way as a deployed one's - by its cargo.
        [Test]
        public async Task DeployAction_TransportInAmbush_CountsCargoAlreadyAboard()
        {
            DataBinding<UnitData> transport = MakeTransport("Buggy", capacity: 3);
            ReserveRules.PlaceInReserve(transport.GetValue());
            DataBinding<UnitData> first = MakeUnit("Grunts", modelCount: 2);
            DataBinding<UnitData> second = MakeUnit("More Grunts", modelCount: 2);
            MakeArmy(transport, first, second);
            TransportUtilities.Embark(first.GetValue(), transport.GetValue());

            var requester = new EmbarkChoiceRequester(pickTransport: null);
            (_, bool finished, _) = await RunChooseDeployAction(requester, second);

            Assert.That(requester.SelectionPromptCount, Is.EqualTo(0), "2 of 3 spaces are taken; a 2-model unit doesn't fit.");
            Assert.That(finished, Is.True);
        }

        // Now that off-table transports qualify, a deploying transport must not be offered a ride in itself.
        [Test]
        public async Task DeployAction_TransportIsNotOfferedItself()
        {
            DataBinding<UnitData> transport = MakeTransport("Buggy", capacity: 6);
            MakeArmy(transport);

            var requester = new EmbarkChoiceRequester(pickTransport: null);
            (_, bool finished, _) = await RunChooseDeployAction(requester, transport);

            Assert.That(requester.SelectionPromptCount, Is.EqualTo(0));
            Assert.That(finished, Is.True);
        }

        // #406: cargo of a transport in Ambush is out of play with it - it can't activate (and so can't
        // disembark) while the transport is off the table. Once the transport arrives (reserve cleared,
        // models placed, as StartOfRoundExtraActionStage.PlaceFromReserve does), the cargo is back in the
        // activation pool and can get out as normal.
        [Test]
        public void RoundPool_CargoActivatesOnlyOnceItsAmbushTransportArrives()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6);
            ReserveRules.PlaceInReserve(transport.GetValue());
            DataBinding<UnitData> squad = MakeUnit("Grunts", modelCount: 2);
            MakeArmy(transport, squad);
            TransportUtilities.Embark(squad.GetValue(), transport.GetValue());
            _store.Create(_team);
            var ctx = new TriggeredMoveTestContext(_store, new EmbarkChoiceRequester(pickTransport: null));

            var waiting = new SingleRoundContext(ctx, new List<ITeam> { _team });
            Assert.That(waiting.UnactivatedUnits[_player], Does.Not.Contain(squad),
                "the cargo waits in reserve with its transport.");
            Assert.That(waiting.UnactivatedUnits[_player], Does.Not.Contain(transport));

            ReserveRules.ClearReserve(transport.GetValue());
            Deploy(transport);

            var arrived = new SingleRoundContext(ctx, new List<ITeam> { _team });
            Assert.That(arrived.UnactivatedUnits[_player], Does.Contain(transport));
            Assert.That(arrived.UnactivatedUnits[_player], Does.Contain(squad),
                "once the transport is on the table its cargo activates, so it can disembark.");
            Assert.That(TransportUtilities.GetTransportId(squad.GetValue()), Is.EqualTo(transport.GetValue().ID),
                "arriving doesn't unload anyone; the cargo is still aboard.");
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
    // actually does rather than a hand-written stand-in (#335).
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

        /// <summary>The last embark prompt seen, so a test can assert on how it was worded (#335).</summary>
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
