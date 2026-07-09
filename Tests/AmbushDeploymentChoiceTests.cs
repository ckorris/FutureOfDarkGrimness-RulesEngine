using System;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for the per-unit Ambush deployment choice: unlike Scout, an
    // Ambush unit stays in the normal deploy pool and the player decides at selection time whether to
    // hold it in reserve or deploy it now.
    //  - Pool: DeploymentTurnContext keeps a LaterRound (Ambush) unit in UndeployedUnits, not DeferredUnits.
    //  - Label: ChooseUnitToDeployStage shows the unit as "Name (Ambush)" so the choice is visible.
    //  - Hold: choosing to hold fires OnDeferred (counts as the turn, no placement), unit stays off-table.
    //  - Deploy: choosing to deploy fires OnFinish with the unit set as the current deploying unit.
    //  - Back: backing out of the hold-or-deploy prompt re-prompts the unit list without committing.
    //  - Crash regression: a player whose whole army was set aside (all Scout) no longer throws on the
    //    first DetermineNextDeployPlayerStage entry — it finishes normal deployment instead.
    [TestFixture]
    public class AmbushDeploymentChoiceTests
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
        public void AmbushUnit_StaysInNormalPool_NotDeferred()
        {
            DataBinding<UnitData> ambush = MakeUnit("Infiltrators", reserveRule: CoreRuleCatalog.Ambush);
            DataBinding<UnitData> normal = MakeUnit("Grunts", reserveRule: null);
            MakeArmy(ambush, normal);

            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            var deployment = new DeploymentTurnContext(ctx, new List<ITeam> { _team }, _zones);

            Assert.That(deployment.DeferredUnits, Is.Empty, "an Ambush unit is not pulled into the deferred pool.");
            Assert.That(deployment.UndeployedUnits[_player], Is.EquivalentTo(new[] { ambush, normal }),
                "the Ambush unit deploys from the normal pool alongside the normal unit.");
        }

        [Test]
        public async Task ChooseUnit_LabelsAmbushUnitWithRuleName()
        {
            DataBinding<UnitData> ambush = MakeUnit("Infiltrators", reserveRule: CoreRuleCatalog.Ambush);
            MakeArmy(ambush);

            var requester = new DeployChoiceRequester(ambush, "Deploy");
            (var deployment, _, _) = await RunChooseUnit(requester);

            string label = requester.UnitPrompts[0].Single().Name;
            Assert.That(label, Is.EqualTo("Infiltrators (Ambush)"),
                "an Ambush-capable unit is labelled with its rule name in the deploy list.");
        }

        [Test]
        public async Task ChooseUnit_Hold_DefersWithoutPlacing()
        {
            DataBinding<UnitData> ambush = MakeUnit("Infiltrators", reserveRule: CoreRuleCatalog.Ambush);
            MakeArmy(ambush);

            var requester = new DeployChoiceRequester(ambush, "Hold");
            (var deployment, bool finished, bool deferred) = await RunChooseUnit(requester);

            Assert.That(deferred, Is.True, "holding in Ambush fires OnDeferred.");
            Assert.That(finished, Is.False, "holding in Ambush does not fire OnFinish (no placement).");
            Assert.That(deployment.CurrentDeployingUnit, Is.Null, "a held unit is never set as the deploying unit.");
            Assert.That(deployment.UndeployedUnits[_player], Is.Empty, "holding consumes the player's deployment for the turn.");
            AssertAllAtOrigin(ambush, "a held Ambush unit stays off-table.");
            Assert.That(Rules.Dispatch.ReserveRules.IsInReserve(ambush.GetValue()), Is.True,
                "holding records reserve as unit state, so nothing has to infer it from model positions.");
            Assert.That(ambush.GetValue().GetIsOnBattlefield(), Is.False,
                "a held unit is off the battlefield: not activatable, not targetable, not drawn.");
        }

        [Test]
        public async Task ChooseUnit_DeployNormally_ProceedsToPlacement()
        {
            DataBinding<UnitData> ambush = MakeUnit("Infiltrators", reserveRule: CoreRuleCatalog.Ambush);
            MakeArmy(ambush);

            var requester = new DeployChoiceRequester(ambush, "Deploy");
            (var deployment, bool finished, bool deferred) = await RunChooseUnit(requester);

            Assert.That(finished, Is.True, "deploying normally fires OnFinish toward placement.");
            Assert.That(deferred, Is.False, "deploying normally does not fire OnDeferred.");
            Assert.That(deployment.CurrentDeployingUnit, Is.EqualTo(ambush), "the chosen unit is set as the deploying unit.");
            Assert.That(deployment.UndeployedUnits[_player], Is.Empty, "the deployed unit leaves the pool.");
        }

        [Test]
        public async Task ChooseUnit_Back_RepromptsThenCommits()
        {
            DataBinding<UnitData> ambush = MakeUnit("Infiltrators", reserveRule: CoreRuleCatalog.Ambush);
            MakeArmy(ambush);

            // First the player backs out of the hold-or-deploy prompt, then deploys on the re-prompt.
            var requester = new DeployChoiceRequester(ambush, "Back", "Deploy");
            (var deployment, bool finished, bool deferred) = await RunChooseUnit(requester);

            Assert.That(requester.UnitPrompts.Count, Is.EqualTo(2), "backing out re-prompts the unit list.");
            Assert.That(requester.StringPromptCount, Is.EqualTo(2), "the hold-or-deploy prompt is shown again after backing out.");
            Assert.That(finished, Is.True, "after backing out, deploying normally still commits.");
            Assert.That(deferred, Is.False);
            Assert.That(deployment.CurrentDeployingUnit, Is.EqualTo(ambush));
        }

        [Test]
        public async Task DetermineNextDeployPlayer_AllUnitsSetAside_DoesNotThrow()
        {
            // Whole army is Scout, so the normal pool is empty. The first-entry fast path used to hand
            // ChooseUnitToDeployStage an empty pool and throw; now it finishes normal deployment instead.
            DataBinding<UnitData> scout = MakeUnit("Scouts", reserveRule: CoreRuleCatalog.Scout);
            MakeArmy(scout);

            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            var deployment = new DeploymentTurnContext(ctx, new List<ITeam> { _team }, _zones);
            Assume.That(deployment.UndeployedUnits[_player], Is.Empty, "precondition: the normal pool is empty.");

            var stage = new DetermineNextDeployPlayerStage(ctx, new NoOpLayer<IDeploymentTurnContext>());
            bool toChooseUnit = false, finishedDeploying = false;
            stage.OnFinish.Bind("choose"); stage.OnFinish.OnWillActivate += _ => toChooseUnit = true;
            stage.OnFinishedDeployingAllUnits.Bind("done"); stage.OnFinishedDeployingAllUnits.OnWillActivate += _ => finishedDeploying = true;

            Assert.DoesNotThrowAsync(async () => await stage.Enter(deployment));
            Assert.That(toChooseUnit, Is.False, "with no normal units, we do not enter unit selection.");
            Assert.That(finishedDeploying, Is.True, "normal deployment finishes straight away.");
        }

        [Test]
        public async Task ChooseUnit_DeferredScoutUnit_ShownGrayedWithRuleName()
        {
            // A Scout unit is set aside for the post-deployment pass, so it can't be picked now — but it
            // must still appear (grayed, as an invalid option, with its rule name) so the player isn't
            // confused about a missing unit. A normal unit is present so the choose stage actually runs.
            DataBinding<UnitData> scout = MakeUnit("Pathfinders", reserveRule: CoreRuleCatalog.Scout);
            DataBinding<UnitData> normal = MakeUnit("Grunts", reserveRule: null);
            MakeArmy(scout, normal);

            var requester = new DeployChoiceRequester(normal);
            await RunChooseUnit(requester);

            Assert.That(requester.UnitPrompts[0].Select(o => o.Name), Is.EqualTo(new[] { "Grunts" }),
                "only the normal unit is selectable.");
            SelectionRequest<UnitData>.InvalidOption grayed = requester.LastInvalidOptions.Single();
            Assert.That(grayed.Option, Is.EqualTo(scout));
            Assert.That(grayed.Name, Is.EqualTo("Pathfinders (Scout)"),
                "the deferred unit is labelled with its rule name.");
        }

        // #029 — an Aircraft must deploy before all other units. While it's still undeployed, only the Aircraft
        // is selectable; the normal unit is grayed out (until the Aircraft is placed).
        [Test]
        public async Task ChooseUnit_AircraftDeploysFirst_OnlyAircraftSelectable()
        {
            DataBinding<UnitData> aircraft = MakeUnit("Jet", reserveRule: CoreRuleCatalog.Aircraft);
            DataBinding<UnitData> normal = MakeUnit("Grunts", reserveRule: null);
            MakeArmy(aircraft, normal);

            var requester = new DeployChoiceRequester(aircraft);
            await RunChooseUnit(requester);

            Assert.That(requester.UnitPrompts[0].Select(o => o.Name), Is.EqualTo(new[] { "Jet" }),
                "only the Aircraft is selectable while it's undeployed.");
            Assert.That(requester.LastInvalidOptions.Any(o => o.Option == normal), Is.True,
                "the normal unit is grayed out until the Aircraft is placed.");
        }

        // Runs ChooseUnitToDeployStage once against the given requester, returning the context plus which
        // outgoing binding fired.
        private async Task<(DeploymentTurnContext Context, bool Finished, bool Deferred)> RunChooseUnit(
            IPlayerRequestByID requester)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var deployment = new DeploymentTurnContext(ctx, new List<ITeam> { _team }, _zones);

            var stage = new ChooseUnitToDeployStage(ctx, new NoOpLayer<IDeploymentTurnContext>());
            bool finished = false, deferred = false;
            stage.OnFinish.Bind("finish"); stage.OnFinish.OnWillActivate += _ => finished = true;
            stage.OnDeferred.Bind("deferred"); stage.OnDeferred.OnWillActivate += _ => deferred = true;

            await stage.Enter(deployment);
            return (deployment, finished, deferred);
        }

        private static void AssertAllAtOrigin(DataBinding<UnitData> unit, string because)
        {
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                Position pos = model.GetValue().PositionBinding.GetValue();
                Assert.That(pos.x == 0f && pos.z == 0f, Is.True, because);
            }
        }

        private DataBinding<UnitData> MakeUnit(string name, SpecialRuleDefinition? reserveRule)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            if (reserveRule != null)
            {
                binding.GetValue().AttachRuleDefinition(new ResolvedRule(reserveRule.Name, reserveRule));
            }

            return binding;
        }

        private void MakeArmy(params DataBinding<UnitData>[] units)
        {
            _store.Create(new ArmyData(_player, units.ToList()));
        }
    }

    // Answers the deploy-unit selection by picking a fixed unit, and the hold-or-deploy string prompt by
    // matching a queued substring ("Hold"/"Deploy"/"Back") against the offered options. Captures the option
    // lists it was shown so tests can assert labels and re-prompt counts.
    internal sealed class DeployChoiceRequester : IPlayerRequestByID
    {
        private readonly DataBinding<UnitData> _pick;
        private readonly Queue<string> _stringAnswers;

        public List<IReadOnlyList<SelectionRequest<UnitData>.ValidOption>> UnitPrompts { get; } = new();
        public IReadOnlyList<SelectionRequest<UnitData>.InvalidOption> LastInvalidOptions { get; private set; } =
            new List<SelectionRequest<UnitData>.InvalidOption>();
        public int StringPromptCount { get; private set; }

        public DeployChoiceRequester(DataBinding<UnitData> pick, params string[] stringAnswers)
        {
            _pick = pick;
            _stringAnswers = new Queue<string>(stringAnswers);
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            switch (request)
            {
                case SelectionRequest<UnitData> selection:
                    UnitPrompts.Add(selection.ValidOptions);
                    LastInvalidOptions = selection.InvalidOptions;
                    SelectionRequest<UnitData>.ValidOption picked =
                        selection.ValidOptions.First(o => o.Option == _pick);
                    return Task.FromResult((TReply)(object)picked.Option);

                case StringSelectionRequest stringSelection:
                    StringPromptCount++;
                    string want = _stringAnswers.Dequeue();
                    string chosen = stringSelection.ValidOptions
                        .First(o => o.Contains(want, StringComparison.OrdinalIgnoreCase));
                    return Task.FromResult((TReply)(object)chosen);

                default:
                    throw new InvalidOperationException("Unexpected request type: " + request.GetType());
            }
        }
    }
}
