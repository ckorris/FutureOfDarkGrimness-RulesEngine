using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #359 — the lane-clearing half of Chris's crowded-game remedy (#296 built the ordering half).
    // A front unit with no reason to advance (long-ranged gun already in range) must still step
    // ASIDE when it is standing on the advance lane of a friendly that has not activated yet;
    // once that friendly HAS activated (or no one is behind), standing and shooting is correct.
    // Written red by design against the pre-#359 planner: with no SideStep candidates and no
    // MoveLaneBlock penalty, Hold-and-shoot wins the packed scene and the front unit never moves.
    [TestFixture]
    public class TacticianLaneClearTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private RuleEvaluator _evaluator = null!;
        private PlayerID _us;
        private PlayerID _them;
        private List<string> _decisions = null!;

        private static readonly string[] MoveShootPass =
        {
            ChooseActionStage.MOVEMENT_CHOICE_NAME, ChooseActionStage.SHOOT_CHOICE_NAME,
            ChooseActionStage.PASS_CHOICE_NAME,
        };

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _decisions = new List<string>();
        }

        // --- The packed-front-line shape: our 5-rifle line at z=20 is already in range of the
        // enemy gunline at z~38 (Hold shoots for the same expected damage as any in-range spot -
        // a plain rifle carries no move-tied penalty), and an 11-model friendly sits directly
        // behind at z~14 with its advance lane running straight through us. Standing earns
        // nothing over stepping aside; the only term that separates the endpoints is the lane.

        private (DataBinding<UnitData> Front, DataBinding<UnitData> Rear) MakePackedScene()
        {
            var front = MakeUnitAt(_us, 5, Rifle(), i => new Position(21.8f + i * 1.1f, 20f));
            var rear = MakeUnitAt(_us, 11, Rifle(),
                i => new Position(22.4f + (i % 4) * 1.1f, 13f + (i / 4) * 1.1f));
            MakeUnitAt(_them, 5, Rifle(),
                i => new Position(24f + (i % 2) * 1.1f, 38f + (i / 2) * 1.1f));
            return (front, rear);
        }

        [Test]
        public void FrontUnit_OnARearLane_ClearsItInsteadOfStandingToShoot()
        {
            // Forward or aside both clear the lane (Chris's correction: a friendly walks into the
            // ground an advance vacates, so blocking only prices in the NEAR corridor) - this pin
            // asserts the CLEARING, not the direction. The walled variant below pins the aside.
            (DataBinding<UnitData> front, _) = MakePackedScene();
            var planner = new TacticianPlanner(_tableState, _evaluator, _decisions.Add);
            planner.BeginActivation(front);
            Position start = Centroid(front);
            List<LaneGeometry.AdvanceLane> lanes =
                LaneGeometry.Build(_tableState, _evaluator, front.GetValue());
            Assert.That(LaneGeometry.BlockValue(lanes, start), Is.GreaterThan(0.3f),
                "scene check: the front unit really is standing squarely on the rear unit's lane");

            string? action = planner.ChooseAction(MoveShootPass);

            Assert.That(action, Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                "in-range shooting is not worth walling the 11-model friendly behind - clear the lane\n"
                + DecisionTable());
            List<ModelMoveEntry>? move = planner.TakePlannedMove(front);
            Assert.That(move, Is.Not.Null, "the movement action must carry a planned move\n" + DecisionTable());
            Position end = EndCentroid(move!, front);
            Assert.That(Distance(start, end), Is.GreaterThanOrEqualTo(3f),
                $"the clearance must be a real move, end=({end.x:F1},{end.z:F1})\n" + DecisionTable());
            Assert.That(LaneGeometry.BlockValue(lanes, end),
                Is.LessThan(LaneGeometry.BlockValue(lanes, start) / 2f),
                $"the endpoint must actually clear the lane, end=({end.x:F1},{end.z:F1})\n" + DecisionTable());
        }

        [Test]
        public void FrontUnit_WalledAhead_StepsAsideRatherThanHoldingTheLane()
        {
            // Chris's original scenario made geometric: the front unit has a real reason not to
            // advance (an impassible wall right ahead), an 11-model friendly waits behind, and its
            // gun is already in range from anywhere nearby - so the RIGHT move is lateral, out of
            // the lane, and standing to shoot must lose to it.
            (DataBinding<UnitData> front, _) = MakePackedScene();
            _store.Create(new TerrainData(ETerrainType.Impassible, new RectangularZone(14f, 34f, 23f, 25f)));
            var planner = new TacticianPlanner(_tableState, _evaluator, _decisions.Add);
            planner.BeginActivation(front);
            Position start = Centroid(front);
            List<LaneGeometry.AdvanceLane> lanes =
                LaneGeometry.Build(_tableState, _evaluator, front.GetValue());

            string? action = planner.ChooseAction(MoveShootPass);

            Assert.That(action, Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                "walled ahead with a friendly behind: step out of the lane, don't stand in it\n"
                + DecisionTable());
            List<ModelMoveEntry>? move = planner.TakePlannedMove(front);
            Assert.That(move, Is.Not.Null, "the movement action must carry a planned move\n" + DecisionTable());
            Position end = EndCentroid(move!, front);
            Assert.That(MathF.Abs(end.x - start.x), Is.GreaterThanOrEqualTo(3f),
                $"with forward walled, the clearance must be LATERAL, end=({end.x:F1},{end.z:F1})\n"
                + DecisionTable());
            Assert.That(LaneGeometry.BlockValue(lanes, end),
                Is.LessThan(LaneGeometry.BlockValue(lanes, start) / 2f),
                $"the endpoint must actually clear the lane, end=({end.x:F1},{end.z:F1})\n" + DecisionTable());
        }

        [Test]
        public void RearFriendAlreadyActivated_FrontUnitHoldsAndShoots()
        {
            // The same scene with the rear unit's activation spent: no one still needs the lane,
            // so the in-range gun stands and shoots - the penalty and the M13 gate both read the
            // ActivatedThisRound token, not mere adjacency.
            (DataBinding<UnitData> front, DataBinding<UnitData> rear) = MakePackedScene();
            rear.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ActivatedThisRound));
            var planner = new TacticianPlanner(_tableState, _evaluator, _decisions.Add);
            planner.BeginActivation(front);

            string? action = planner.ChooseAction(MoveShootPass);

            Assert.That(action, Is.EqualTo(ChooseActionStage.SHOOT_CHOICE_NAME),
                "with the lane spoken for by no one, standing and shooting is the right call\n"
                + DecisionTable());
        }

        [Test]
        public void SideStepCandidates_ExistOnlyWhileStandingOnAnUnactivatedFriendlysLane()
        {
            (DataBinding<UnitData> front, DataBinding<UnitData> rear) = MakePackedScene();

            List<MacroAction> blocked = MacroActionGenerator.Enumerate(_evaluator, _tableState, front);
            Assert.That(blocked.Any(c => c.Intent == EMacroIntent.SideStep), Is.True,
                "standing on the rear unit's lane must offer the side-step family");

            rear.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ActivatedThisRound));
            List<MacroAction> clear = MacroActionGenerator.Enumerate(_evaluator, _tableState, front);
            Assert.That(clear.Any(c => c.Intent == EMacroIntent.SideStep), Is.False,
                "once the rear unit has activated there is no lane to clear - keep the budget lean");
        }

        [Test]
        public async Task ActivationPick_FlagsBiasDecisive_WhenUrgencyIsFlat()
        {
            // #359 measurement pin: everyone is out of range of everything (urgency 0 across the
            // board), so ONLY the frontline bias separates the options - and the narration must
            // say so. Options are ordered rear-first: a flat urgency argmax keeps the first
            // option, so a front pick here is the bias and nothing else.
            var rear = MakeUnitAt(_us, 4, Rifle(), i => new Position(23f + i * 1.1f, 4f));
            var front = MakeUnitAt(_us, 3, Rifle(), i => new Position(23f + i * 1.1f, 14f));
            MakeUnitAt(_them, 3, Rifle(), i => new Position(23f + i * 1.1f, 46f));
            var resolver = new TacticianActivationResolver(_tableState, _evaluator,
                planner: null, decisionLog: _decisions.Add);

            DataBinding<UnitData> chosen = await resolver.Resolve(new ChooseUnitToActivateRequest(_us,
                new List<SelectionRequest<UnitData>.ValidOption>
                {
                    new(rear, "rear"), new(front, "front"),
                },
                new List<SelectionRequest<UnitData>.InvalidOption>()));

            Assert.That(chosen, Is.EqualTo(front), "flat urgency: the frontmost unit acts first (#296)");
            Assert.That(_decisions, Has.Count.EqualTo(1), string.Join("\n", _decisions));
            Assert.That(_decisions[0], Does.Contain("bias-decisive"),
                "the pick was decided by the frontline bias and the measurement line must flag it: "
                + _decisions[0]);
        }

        // --- helpers (the TacticianWalledUnitTests construction) ------------------------------------

        private static Weapon Rifle() => new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);

        private DataBinding<UnitData> MakeUnitAt(PlayerID owner, int modelCount, Weapon weapon,
            Func<int, Position> positionFor)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon }, positionFor(i), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"U{owner.GetHashCode() % 100}-{modelCount}", quality: 4,
                defense: 4, modelBindings: modelBindings);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private string DecisionTable() =>
            _decisions.Count == 0 ? "(no decision log)" : string.Join("\n", _decisions);

        private static float Distance(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private static Position Centroid(DataBinding<UnitData> unit)
        {
            var alive = unit.GetValue().Models.Where(m => m.GetIsAlive()).ToList();
            return new Position(alive.Average(m => m.Position.x), alive.Average(m => m.Position.z));
        }

        private static Position EndCentroid(List<ModelMoveEntry> move, DataBinding<UnitData> unit)
        {
            var ends = move.Where(e => e.Positions.Count > 0).Select(e => e.Positions[^1]).ToList();
            if (ends.Count == 0) return Centroid(unit);
            return new Position(ends.Average(p => p.x), ends.Average(p => p.z));
        }
    }
}
