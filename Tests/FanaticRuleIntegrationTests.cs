using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #197 P21 Fanatic: "after this model is deployed, it may be placed
    // anywhere fully within 9in of its position." Vanguard's deploy-hook shape (an activated ability at
    // Deployment_OnUnitDeployed, once per game) but a PLACEMENT, not a move - the owner's
    // reposition-is-a-placement ruling. Proves: the effect emits a flat RepositionModels(9) op, the rule is
    // offered at the deploy hook and gated once-per-game, and - through the REAL DeployUnitStage - accepting
    // folds the op into a within-9in placement (radius + allowCancel reach the resolver) while declining
    // leaves the unit where it deployed.
    [TestFixture]
    public class FanaticRuleIntegrationTests
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
        public void Catalog_Fanatic_IsADeployHookAbility_WithNineInchPlacementEffect()
        {
            ActivatedAbility ability = CoreRuleCatalog.Fanatic.Activated.Single();

            Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Deployment_OnUnitDeployed));
            Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerGame>(),
                "deployment happens once, so the gate is naturally spent - matching Vanguard.");
            Assert.That(ability.Effect, Is.InstanceOf<Effect.RepositionOnDeploy>());
            Assert.That(((Effect.RepositionOnDeploy)ability.Effect).MaxInches, Is.EqualTo(9f).Within(0.001f));
        }

        [Test]
        public void ResolveAbility_EmitsAFlatRepositionOp_NoDiceRoll()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnit("Zealots", fanatic: true);

            AbilityOffer offer = ctx.RuleEvaluator.GatherOffers(new UnitDeployedContext(unit.GetValue()))
                .Single(o => o.RuleName == "Fanatic");
            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.ResolveAbility(offer, new[] { unit.GetValue() });

            List<RuleOperation.RepositionModels> repositions =
                ops.OfType<RuleOperation.RepositionModels>().ToList();
            Assert.That(repositions, Has.Count.EqualTo(1), "a single flat reposition op, no dice.");
            Assert.That(repositions[0].MaxInches, Is.EqualTo(9f).Within(0.001f));
        }

        [Test]
        public void GatherOffers_OncePerGameUsed_FanaticNotOfferedAgain()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = MakeUnit("Zealots", fanatic: true);
            unit.GetValue().Tokens.AddToken(
                new Token(new TokenType("AbilityUsed:Fanatic"), 1, new TokenClearTrigger.ManualOnly()));

            var offers = ctx.RuleEvaluator.GatherOffers(new UnitDeployedContext(unit.GetValue()));

            Assert.That(offers.Any(o => o.RuleName == "Fanatic"), Is.False,
                "the once-per-game marker suppresses a second offer.");
        }

        [Test]
        public async Task DeployStage_FanaticAccepted_RepositionsWithinNineInches()
        {
            DataBinding<UnitData> unit = MakeUnit("Zealots", fanatic: true);
            MakeArmy(unit);

            // Deploy each model at (10,10), then reposition +5in in x (inside the 9in radius).
            var requester = new FanaticRequester(accept: true, deployAt: new Position(10f, 10f), shiftX: 5f);
            await RunDeployUnit(requester, unit);

            Assert.That(requester.RepositionRequest, Is.Not.Null, "the reposition placement was offered.");
            Assert.That(requester.RepositionRequest!.MaxDistanceFromStartInches, Is.EqualTo(9f).Within(0.001f),
                "the 9in radius reaches the resolver.");
            Assert.That(requester.RepositionRequest!.AllowCancel, Is.True,
                "'it may be placed' - declining is legal.");

            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                Assert.That(model.GetValue().Position.x, Is.EqualTo(15f).Within(0.001f),
                    "the model repositioned 5in from its deploy position, through the real stage.");
            }
        }

        [Test]
        public async Task DeployStage_FanaticDeclined_StaysAtDeployPosition()
        {
            DataBinding<UnitData> unit = MakeUnit("Zealots", fanatic: true);
            MakeArmy(unit);

            var requester = new FanaticRequester(accept: false, deployAt: new Position(10f, 10f), shiftX: 5f);
            await RunDeployUnit(requester, unit);

            Assert.That(requester.RepositionRequest, Is.Null, "declining Fanatic skips the reposition placement.");
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                Assert.That(model.GetValue().Position.x, Is.EqualTo(10f).Within(0.001f),
                    "the unit stays exactly where it deployed.");
            }
        }

        [Test]
        public async Task DeployStage_NonFanaticUnit_NoRepositionPrompt()
        {
            DataBinding<UnitData> unit = MakeUnit("Grunts", fanatic: false);
            MakeArmy(unit);

            var requester = new FanaticRequester(accept: true, deployAt: new Position(10f, 10f), shiftX: 5f);
            await RunDeployUnit(requester, unit);

            Assert.That(requester.YesNoCount, Is.EqualTo(0), "no deploy ability, so no Yes/No offer.");
            Assert.That(requester.RepositionRequest, Is.Null, "and no reposition placement.");
        }

        // --- helpers ---

        // Drives the real DeployUnitStage for one unit set as current deployer (as ChooseUnitToDeployStage
        // would have), so the deploy placement, the Deployment_OnUnitDeployed offer, and the reposition fold
        // all run through production code.
        private async Task RunDeployUnit(IPlayerRequestByID requester, DataBinding<UnitData> unit)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var deployment = new DeploymentTurnContext(ctx, new List<ITeam> { _team }, _zones)
            {
                CurrentDeployingUnit = unit,
            };

            var stage = new DeployUnitStage(ctx, new NoOpLayer<IDeploymentTurnContext>());
            stage.OnFinish.Bind("finish");
            await stage.Enter(deployment);
        }

        private DataBinding<UnitData> MakeUnit(string name, bool fanatic)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (fanatic)
            {
                binding.GetValue().AttachRuleDefinition(new ResolvedRule("Fanatic", CoreRuleCatalog.Fanatic));
            }
            return binding;
        }

        private void MakeArmy(params DataBinding<UnitData>[] units)
        {
            _store.Create(new ArmyData(_player, units.ToList()));
        }
    }

    // Answers the two placements DeployUnitStage issues - the mandatory deploy placement (radius 0) puts every
    // model at deployAt; the Fanatic reposition placement (radius > 0) shifts each model shiftX in x - plus the
    // "Use Fanatic?" Yes/No. Captures the reposition request so the test can assert its radius and cancellability.
    internal sealed class FanaticRequester : IPlayerRequestByID
    {
        private readonly bool _accept;
        private readonly Position _deployAt;
        private readonly float _shiftX;

        public int YesNoCount { get; private set; }
        public PlaceObjectsRequest<ModelData>? RepositionRequest { get; private set; }

        public FanaticRequester(bool accept, Position deployAt, float shiftX)
        {
            _accept = accept;
            _deployAt = deployAt;
            _shiftX = shiftX;
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is YesNoRequest)
            {
                YesNoCount++;
                return Task.FromResult((TReply)(object)_accept);
            }

            if (request is PlaceObjectsRequest<ModelData> place)
            {
                bool isReposition = place.MaxDistanceFromStartInches > 0f;
                if (isReposition) RepositionRequest = place;

                var entries = place.ModelsToPlace.Select(m =>
                {
                    Position dest = isReposition
                        ? new Position(m.GetValue().Position.x + _shiftX, m.GetValue().Position.z)
                        : _deployAt;
                    return new PlacedObjectEntry<ModelData>(m, dest);
                }).ToList();

                return Task.FromResult((TReply)(object)new Selected<List<PlacedObjectEntry<ModelData>>>(entries));
            }

            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
