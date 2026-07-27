using FDG.Ai.Tactician;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 A2 — TacticalAnalysis unit tests on authored states: mobility/threat queries, the
    // objective-control projection (mirroring ReconcileObjectivesStage's rules and exclusions),
    // and the unit value model's rough calibration against real book stat lines.
    [TestFixture]
    public class TacticalAnalysisTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private RuleEvaluator _evaluator = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
        }

        // --- Mobility / threat ------------------------------------------------------------------

        [Test]
        public void MoveDistances_BaseAndFast()
        {
            var plain = MakeUnit(3, Rifle());
            var fast = MakeUnit(3, Rifle());
            AttachRule(fast, "Fast", CoreRuleCatalog.Fast);

            Assert.Multiple(() =>
            {
                Assert.That(TacticalAnalysis.AdvanceDistance(plain.GetValue(), _evaluator), Is.EqualTo(6f));
                Assert.That(TacticalAnalysis.RushDistance(plain.GetValue(), _evaluator), Is.EqualTo(12f));
                Assert.That(TacticalAnalysis.AdvanceDistance(fast.GetValue(), _evaluator), Is.EqualTo(8f));
                Assert.That(TacticalAnalysis.RushDistance(fast.GetValue(), _evaluator), Is.EqualTo(16f));
            });
        }

        [Test]
        public void ChargeDistance_FastAddsFour()
        {
            var charger = MakeUnit(3, Blade());
            var fastCharger = MakeUnit(3, Blade());
            AttachRule(fastCharger, "Fast", CoreRuleCatalog.Fast);
            var target = MakeUnit(3, null);

            Assert.That(TacticalAnalysis.ChargeDistanceAgainst(
                charger.GetValue(), target.GetValue(), _evaluator), Is.EqualTo(12f));
            Assert.That(TacticalAnalysis.ChargeDistanceAgainst(
                fastCharger.GetValue(), target.GetValue(), _evaluator), Is.EqualTo(16f));
        }

        [Test]
        public void ThreatRange_ShooterIsAdvancePlusRange_MeleeIsChargeReach()
        {
            var shooter = MakeUnit(3, Rifle());          // 6 + 24
            var brawler = MakeUnit(3, Blade());          // charge 12
            var target = MakeUnit(3, null);

            Assert.That(TacticalAnalysis.ThreatRangeAgainst(
                shooter.GetValue(), target.GetValue(), _evaluator), Is.EqualTo(30f));
            Assert.That(TacticalAnalysis.ThreatRangeAgainst(
                brawler.GetValue(), target.GetValue(), _evaluator), Is.EqualTo(12f));
            Assert.That(TacticalAnalysis.MaxWeaponRange(
                brawler.GetValue(), target.GetValue(), _evaluator), Is.EqualTo(0f));
        }

        // --- Objective projection -----------------------------------------------------------------

        [Test]
        public void Projection_SinglePlayerInRange_Seizes()
        {
            var objective = MakeObjective(new Position(10f, 10f));
            var unit = MakeUnit(1, null, atX: 12f, atZ: 10f); // center 2" away, edge ~1.5"

            List<ObjectiveProjection> projections = TacticalAnalysis.ProjectObjectives(_tableState);

            Assert.That(projections.Single().ProjectedOwner, Is.EqualTo(unit.GetValue().PlayerID));
            Assert.That(TacticalAnalysis.ProjectedScore(_tableState, unit.GetValue().PlayerID), Is.EqualTo(1));
        }

        [Test]
        public void Projection_UsesBaseEdgeDistance_NotCenter()
        {
            MakeObjective(new Position(10f, 10f));
            // Center 3.4" away with a 0.5" base: edge 2.9" -> in range only because of the base edge.
            var unit = MakeUnit(1, null, atX: 13.4f, atZ: 10f);

            Assert.That(TacticalAnalysis.ProjectObjectives(_tableState).Single().ProjectedOwner,
                Is.EqualTo(unit.GetValue().PlayerID));

            // And a unit whose EDGE is past 3" counts as out.
            Assert.That(TacticalAnalysis.MinBaseEdgeDistanceToPoint(
                MakeUnit(1, null, atX: 14f, atZ: 10f).GetValue(), new Position(10f, 10f)),
                Is.GreaterThan(TacticalAnalysis.ObjectiveSeizureRadiusInches));
        }

        [Test]
        public void Projection_TwoPlayersInRange_ContestsToNeutral()
        {
            var objective = MakeObjective(new Position(10f, 10f));
            objective.GetValue().SetOwner(MakeUnit(1, null, atX: 60f).GetValue().PlayerID); // far-away owner
            MakeUnit(1, null, atX: 11f, atZ: 10f);
            MakeUnit(1, null, atX: 9f, atZ: 10f);

            Assert.That(TacticalAnalysis.ProjectObjectives(_tableState).Single().ProjectedOwner, Is.Null);
        }

        // #297: the projection mirrors the team-aware reconcile - two ALLIED players in range hold
        // the marker for their side (sticky toward the current owner) instead of contesting it.
        [Test]
        public void Projection_TwoAlliedPlayersInRange_SideHoldsMarker()
        {
            var objective = MakeObjective(new Position(10f, 10f));
            var a = MakeUnit(1, null, atX: 11f, atZ: 10f);
            var b = MakeUnit(1, null, atX: 9f, atZ: 10f);
            _store.Create(new TeamData(0, new List<PlayerID>
                { a.GetValue().PlayerID, b.GetValue().PlayerID }));
            objective.GetValue().SetOwner(b.GetValue().PlayerID);

            Assert.That(TacticalAnalysis.ProjectObjectives(_tableState).Single().ProjectedOwner,
                Is.EqualTo(b.GetValue().PlayerID),
                "allied players sharing the marker keep it with its current on-side owner.");
        }

        [Test]
        public void Projection_NobodyInRange_OwnerIsSticky()
        {
            var objective = MakeObjective(new Position(10f, 10f));
            var farUnit = MakeUnit(1, null, atX: 60f);
            objective.GetValue().SetOwner(farUnit.GetValue().PlayerID);

            Assert.That(TacticalAnalysis.ProjectObjectives(_tableState).Single().ProjectedOwner,
                Is.EqualTo(farUnit.GetValue().PlayerID));
        }

        [Test]
        public void Projection_ExcludesShakenReserveArrivalsAndAircraft()
        {
            var objective = MakeObjective(new Position(10f, 10f));
            var owner = MakeUnit(1, null, atX: 60f);
            objective.GetValue().SetOwner(owner.GetValue().PlayerID);

            var shaken = MakeUnit(1, null, atX: 10f, atZ: 11f);
            shaken.GetValue().Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.RoundEnd()));

            var fresh = MakeUnit(1, null, atX: 10f, atZ: 9f);
            fresh.GetValue().Tokens.AddToken(new Token(TokenType.ArrivedFromReserve, 1, new TokenClearTrigger.RoundEnd()));

            var aircraft = MakeUnit(1, null, atX: 11f, atZ: 10f);
            AttachRule(aircraft, "Aircraft", CoreRuleCatalog.Aircraft);

            ObjectiveProjection projection = TacticalAnalysis.ProjectObjectives(_tableState).Single();
            Assert.That(projection.PlayersInRange, Is.Empty,
                "Shaken, fresh-from-reserve, and Aircraft units must count toward nothing");
            Assert.That(projection.ProjectedOwner, Is.EqualTo(owner.GetValue().PlayerID));
        }

        // --- Unit value calibration ------------------------------------------------------------------

        // Real HumanDefenseForce stat lines (2026-07-09 book snapshot), rule-free on purpose so the
        // formula's f(wounds, quality, weapon output) is the only thing under test. Assertions are
        // ORDERING (the plan's "calibrate roughly"), not ratios. Known counterexample recorded in the
        // #191 ledger: Recruits (10 @ 75pts) vs GRUNT Robots (5 @ 80pts) - the book prices the model
        // count below quality there; this formula does not.
        [Test]
        public void UnitValue_OrdersLikeBookPoints_OnCalibrationPairs()
        {
            // Infantry Squad 115pts: 10x Q5 D5, Rifle + CCW  vs  Recruits 75pts: 10x Q6 D5, same gear.
            float infantry = TacticalAnalysis.UnitValue(
                MakeUnit(10, Rifle(), quality: 5, defense: 5, alsoCcw: true).GetValue());
            float recruits = TacticalAnalysis.UnitValue(
                MakeUnit(10, Rifle(), quality: 6, defense: 5, alsoCcw: true).GetValue());

            // Storm Troopers 115pts: 5x Q4 D4, Heavy Rifle(AP1) + CCW  vs  Veterans 80pts: 5x Q4 D5, Rifle + CCW.
            float stormTroopers = TacticalAnalysis.UnitValue(
                MakeUnit(5, Rifle(ap: 1), quality: 4, defense: 4, alsoCcw: true).GetValue());
            float veterans = TacticalAnalysis.UnitValue(
                MakeUnit(5, Rifle(), quality: 4, defense: 5, alsoCcw: true).GetValue());

            // Tank Company Leader 420pts: 1x Q4 D2 Tough(9), three guns.
            var tankModel = new ModelData(
                baseRadiusInches: 1.5f,
                weapons: new List<Weapon>
                {
                    new Weapon("Nova Cannon", 36f, 1, 1),
                    new Weapon("Pintle-Machinegun", 30f, 2, 1),
                    new Weapon("Twin Heavy Flamer", 12f, 2, 1),
                },
                initialPosition: new Position(40f, 40f),
                gameDataStore: _store);
            tankModel.SetMaxWounds(9);
            var tank = new UnitData(new PlayerID(Guid.NewGuid()), "Tank", quality: 4, defense: 2,
                modelBindings: new List<DataBinding<ModelData>>
                    { _store.GetDataBinding<ModelData>(_store.Create(tankModel)) });
            float tankValue = TacticalAnalysis.UnitValue(
                _store.GetDataBinding<UnitData>(_store.Create(tank)).GetValue());

            Assert.Multiple(() =>
            {
                Assert.That(infantry, Is.GreaterThan(recruits), "Infantry Squad (115) vs Recruits (75)");
                Assert.That(stormTroopers, Is.GreaterThan(veterans), "Storm Troopers (115) vs Veterans (80)");
                Assert.That(tankValue, Is.GreaterThan(stormTroopers), "Tank (420) must outrank infantry units");
                Assert.That(tankValue, Is.GreaterThan(infantry), "Tank (420) must outrank infantry units");
            });
        }

        [Test]
        public void UnitValue_FallsWithCasualties()
        {
            var unit = MakeUnit(5, Rifle());
            float fullStrength = TacticalAnalysis.UnitValue(unit.GetValue());
            unit.GetValue().Models[0].DealWounds(1f); // kill one model
            float afterCasualty = TacticalAnalysis.UnitValue(unit.GetValue());

            Assert.That(afterCasualty, Is.LessThan(fullStrength));
        }

        // --- Fixtures -----------------------------------------------------------------------------

        private static Weapon Rifle(int ap = 0) =>
            new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: ap);

        private static Weapon Blade() =>
            new Weapon("Blade", rangeInches: 0f, attacks: 2, armorPenetration: 0);

        private static void AttachRule(DataBinding<UnitData> unit, string name,
            Rules.Definitions.SpecialRuleDefinition definition) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(name, definition));

        private DataBinding<ObjectiveData> MakeObjective(Position position)
        {
            var objective = new ObjectiveData(position, _store);
            return _store.GetDataBinding<ObjectiveData>(_store.Create(objective));
        }

        private DataBinding<UnitData> MakeUnit(int modelCount, Weapon? weapon,
            int quality = 4, int defense = 4, float atX = 20f, float atZ = 20f,
            int woundsPerModel = 1, bool alsoCcw = false)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var weapons = new List<Weapon>();
                if (weapon != null) weapons.Add(weapon);
                if (alsoCcw) weapons.Add(new Weapon("CCW", 0f, 1, 0));
                var model = new ModelData(
                    baseRadiusInches: 0.5f,
                    weapons: weapons,
                    initialPosition: new Position(atX, atZ + i * 1.2f),
                    gameDataStore: _store);
                if (woundsPerModel > 1)
                    model.SetMaxWounds(woundsPerModel);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit",
                quality: quality, defense: defense,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
