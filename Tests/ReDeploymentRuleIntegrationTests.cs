using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #197 P21 Re-Deployment, driven through the real ReDeploymentStage:
    // "After all other units are deployed (excluding set-aside units), you may remove up to two friendly
    // units per Re-Deployment unit and deploy them again; players alternate, starting with whoever activates
    // next." Proves: the budget is 2 per Re-Deployment unit owned (stacking), a player passes by declining,
    // set-aside (off-table) units are never eligible, players alternate in deployment-roll order, and each
    // redeploy re-places the unit's models in its owner's zone. A unit with no Re-Deployment gets no prompt.
    [TestFixture]
    public class ReDeploymentRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private TeamData _teamA = null!;
        private TeamData _teamB = null!;
        private PlayerID _playerA;
        private PlayerID _playerB;
        private Dictionary<ITeam, DataBinding<RectangularZone>> _zones = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _playerA = new PlayerID(Guid.NewGuid());
            _playerB = new PlayerID(Guid.NewGuid());
            _teamA = new TeamData(0, new List<PlayerID> { _playerA });
            _teamB = new TeamData(1, new List<PlayerID> { _playerB });

            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float d = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
            float h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            _zones = new Dictionary<ITeam, DataBinding<RectangularZone>>
            {
                [_teamA] = _store.GetDataBinding<RectangularZone>(_store.Create(new RectangularZone(0f, w, 0f, d))),
                [_teamB] = _store.GetDataBinding<RectangularZone>(_store.Create(new RectangularZone(0f, w, h - d, h))),
            };
        }

        [Test]
        public async Task NoReDeploymentUnit_FinishesWithoutPrompt()
        {
            MakeUnit(_playerA, "Grunts", reDeployment: false, onTable: true);
            MakeUnit(_playerA, "Riflemen", reDeployment: false, onTable: true);

            var requester = new ReDeployRequester(pickFirst: true);
            bool finished = await RunReDeployment(requester, new[] { (ITeam)_teamA });

            Assert.That(finished, Is.True);
            Assert.That(requester.PromptCount, Is.EqualTo(0), "no Re-Deployment unit, so no sub-phase prompt.");
            Assert.That(requester.PlacementCount, Is.EqualTo(0));
        }

        [Test]
        public async Task OneReDeploymentUnit_GrantsTwoRedeploys()
        {
            MakeUnit(_playerA, "Warpers", reDeployment: true, onTable: true);
            MakeUnit(_playerA, "Grunts", reDeployment: false, onTable: true);
            MakeUnit(_playerA, "Riflemen", reDeployment: false, onTable: true);
            MakeUnit(_playerA, "Gunners", reDeployment: false, onTable: true);

            var requester = new ReDeployRequester(pickFirst: true);
            await RunReDeployment(requester, new[] { (ITeam)_teamA });

            Assert.That(requester.PlacementCount, Is.EqualTo(2),
                "one Re-Deployment unit -> a budget of two redeploys, then the player is done.");
        }

        [Test]
        public async Task BudgetStacks_TwoReDeploymentUnits_GiveFour()
        {
            MakeUnit(_playerA, "Warpers A", reDeployment: true, onTable: true);
            MakeUnit(_playerA, "Warpers B", reDeployment: true, onTable: true);
            for (int i = 0; i < 4; i++) MakeUnit(_playerA, $"Grunts {i}", reDeployment: false, onTable: true);

            var requester = new ReDeployRequester(pickFirst: true);
            await RunReDeployment(requester, new[] { (ITeam)_teamA });

            Assert.That(requester.PlacementCount, Is.EqualTo(4),
                "the budget stacks: two Re-Deployment units -> four redeploys (owner ruling).");
        }

        [Test]
        public async Task Pass_EndsParticipation_NoPlacement()
        {
            MakeUnit(_playerA, "Warpers", reDeployment: true, onTable: true);
            MakeUnit(_playerA, "Grunts", reDeployment: false, onTable: true);

            var requester = new ReDeployRequester(pickFirst: false); // always passes
            await RunReDeployment(requester, new[] { (ITeam)_teamA });

            Assert.That(requester.PromptCount, Is.EqualTo(1), "prompted once; the pass ends participation.");
            Assert.That(requester.PlacementCount, Is.EqualTo(0), "declining redeploys nothing.");
        }

        [Test]
        public async Task SetAsideUnit_NotEligible()
        {
            MakeUnit(_playerA, "Warpers", reDeployment: true, onTable: true);
            MakeUnit(_playerA, "Reserved", reDeployment: false, onTable: false); // off-table (set aside)

            var requester = new ReDeployRequester(pickFirst: true);
            await RunReDeployment(requester, new[] { (ITeam)_teamA });

            Assert.That(requester.AllOfferedNames, Does.Not.Contain("Reserved"),
                "a unit still off the table (set aside) is never offered for re-deployment.");
            Assert.That(requester.PlacementCount, Is.EqualTo(1),
                "only the on-table Re-Deployment unit itself is eligible; after it redeploys, nothing is left.");
        }

        [Test]
        public async Task Alternation_TwoPlayers_StartWithRollOrderHead_AndRedeployInOwnZone()
        {
            DataBinding<UnitData> aUnit = MakeUnit(_playerA, "A-Warpers", reDeployment: true, onTable: true);
            DataBinding<UnitData> bUnit = MakeUnit(_playerB, "B-Warpers", reDeployment: true, onTable: true);
            // A second on-table unit each so the budget of two can actually be spent (and the alternation
            // runs a full two cycles).
            MakeUnit(_playerA, "A-Grunts", reDeployment: false, onTable: true);
            MakeUnit(_playerB, "B-Grunts", reDeployment: false, onTable: true);

            var requester = new ReDeployRequester(pickFirst: true);
            // Roll order head = team A -> player A activates next -> A places first.
            await RunReDeployment(requester, new ITeam[] { _teamA, _teamB });

            Assert.That(requester.PromptedPlayers.First(), Is.EqualTo(_playerA),
                "the head of the deployment roll order (who activates next) redeploys first.");
            Assert.That(requester.PromptedPlayers, Is.EqualTo(new[] { _playerA, _playerB, _playerA, _playerB }),
                "players alternate one unit at a time until both spend their budget of two.");

            // Each unit landed in its own team's zone (A bottom, B top).
            Assert.That(aUnit.GetValue().ModelBindings[0].GetValue().Position.z,
                Is.LessThan(GameWideConstants.DEPLOYMENT_DISTANCE_INCHES + 0.001f), "A redeployed into A's bottom zone.");
            Assert.That(bUnit.GetValue().ModelBindings[0].GetValue().Position.z,
                Is.GreaterThan(GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES - GameWideConstants.DEPLOYMENT_DISTANCE_INCHES - 0.001f),
                "B redeployed into B's top zone.");
        }

        // --- helpers ---

        private async Task<bool> RunReDeployment(IPlayerRequestByID requester, IReadOnlyList<ITeam> rollOrder)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var deployment = new DeploymentTurnContext(ctx, rollOrder.ToList(), _zones);

            var stage = new ReDeploymentStage(ctx, new NoOpLayer<IDeploymentTurnContext>());
            bool finished = false;
            stage.OnFinish.Bind("finish");
            stage.OnFinish.OnWillActivate += _ => finished = true;

            await stage.Enter(deployment);
            return finished;
        }

        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, bool reDeployment, bool onTable)
        {
            // A single model at (10,10) counts as on the battlefield; left at origin it reads as set aside.
            Position pos = onTable ? new Position(10f, 10f) : new Position(0f, 0f);
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };

            var unit = new UnitData(player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (reDeployment)
            {
                binding.GetValue().AttachRuleDefinition(new ResolvedRule(
                    CoreRuleCatalog.ReDeploymentRuleName, CoreRuleCatalog.ReDeployment));
            }

            // Each player's army accumulates; find-or-create keeps one ArmyData per player.
            ArmyData? army = _store.GetAllValues<ArmyData>().FirstOrDefault(a => a.IsOwnedBy(player));
            if (army == null)
            {
                _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            }
            else
            {
                army.UnitBindings.Add(binding);
            }
            return binding;
        }
    }

    // Answers the Re-Deployment prompts: a CancellableSelectionRequest is either picked (first offered unit,
    // when pickFirst) or passed (Cancelled); a PlaceObjectsRequest re-places every model at the request's
    // zone center. Records prompt order, offered names, and placement count for the assertions.
    internal sealed class ReDeployRequester : IPlayerRequestByID
    {
        private readonly bool _pickFirst;

        public int PromptCount { get; private set; }
        public int PlacementCount { get; private set; }
        public List<PlayerID> PromptedPlayers { get; } = new();
        public List<string> AllOfferedNames { get; } = new();

        public ReDeployRequester(bool pickFirst) => _pickFirst = pickFirst;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is CancellableSelectionRequest<UnitData> sel)
            {
                PromptCount++;
                PromptedPlayers.Add(sel.TargetPlayerID);
                AllOfferedNames.AddRange(sel.ValidOptions.Select(o => o.Name));

                if (_pickFirst && sel.ValidOptions.Count > 0)
                {
                    return Task.FromResult((TReply)(object)(CancellableResult<DataBinding<UnitData>>)
                        new Selected<DataBinding<UnitData>>(sel.ValidOptions.First().Option));
                }
                return Task.FromResult((TReply)(object)(CancellableResult<DataBinding<UnitData>>)
                    new Cancelled<DataBinding<UnitData>>());
            }

            if (request is PlaceObjectsRequest<ModelData> place)
            {
                PlacementCount++;
                var dest = new Position(place.DeploymentZone.Bounds.CenterX, place.DeploymentZone.Bounds.CenterZ);
                var entries = place.ModelsToPlace
                    .Select(m => new PlacedObjectEntry<ModelData>(m, dest))
                    .ToList();
                return Task.FromResult((TReply)(object)new Selected<List<PlacedObjectEntry<ModelData>>>(entries));
            }

            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
