using FDG.Data;
using FDG.Stages;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FDG.Tests
{
    // #201 cover proximity exceptions (house rules, GameSettings.CoverProximityExceptions, default ON):
    //   Rule 1 - a cover piece whose sight-line exit is within 2" of the shooter's base is voided
    //            (unless the target's base also hugs the exit - the owner amendment).
    //   Rule 2 - shooter and target inside the SAME cover piece void it under 6" base-to-base.
    // Style mirrors CoverMajorityTests (real CoverCheckStage through TestGameContext).
    //
    // Geometry: models use 0.75" radius bases. "ThinWall" is 0.5" deep at x:5.0-5.5, z:0-10 -
    // rule 1 needs a THIN piece (a deep piece's exit is far away by design, and its cover must
    // survive). "Forest" is 12x12 at the origin for the rule 2 cases.
    [TestFixture]
    public class CoverProximityRuleTests
    {
        private static readonly RectangularZone ThinWall = new RectangularZone(5.0f, 5.5f, 0, 10);
        private static readonly RectangularZone Forest = new RectangularZone(0, 12, 0, 12);

        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        // ---- Rule 1: attacker-exit ----

        [Test]
        public async Task ShooterHuggingThinWall_DefenderInOpen_NoBonus()
        {
            // Exit at (5.5, 5); shooter base surface 0.75" from center (4,5) -> 0.75" away: voided.
            CoverCheckResults result = await RunStage(
                shooterPositions: new[] { new Position(4, 5) },
                defenderPositions: new[] { new Position(20, 5) },
                terrain: new[] { new TerrainData(ETerrainType.Cover, ThinWall) });
            Assert.That(result.DefenseRollBonus, Is.EqualTo(0),
                "a wall at the shooter's muzzle must not screen a defender in the open");
        }

        [Test]
        public async Task ShooterWellBehindThinWall_DefenderInOpen_BonusStands()
        {
            // Exit at (5.5, 5) is 2.75" from the shooter's base surface (center at (2,5)) - the
            // accepted residual: only muzzle-adjacent pieces are voided, by design of the 2" const.
            CoverCheckResults result = await RunStage(
                shooterPositions: new[] { new Position(2, 5) },
                defenderPositions: new[] { new Position(20, 5) },
                terrain: new[] { new TerrainData(ETerrainType.Cover, ThinWall) });
            Assert.That(result.DefenseRollBonus, Is.EqualTo(1));
        }

        [Test]
        public async Task ToggleOff_ShooterHuggingThinWall_OldBehaviorPreserved()
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.CoverProximityExceptions = false;
            CoverCheckResults result = await RunStage(
                shooterPositions: new[] { new Position(4, 5) },
                defenderPositions: new[] { new Position(20, 5) },
                terrain: new[] { new TerrainData(ETerrainType.Cover, ThinWall) },
                settings: settings);
            Assert.That(result.DefenseRollBonus, Is.EqualTo(1),
                "with the house rules off, any crossing cover piece grants the bonus (official rules)");
        }

        [Test]
        public async Task BothHuggingSameThinWall_DefenderKeepsCover()
        {
            // Owner amendment: the exit (5.5, 5) is 0.25" from the defender's base surface too -
            // the defender is legitimately behind that wall and keeps the +1.
            CoverCheckResults result = await RunStage(
                shooterPositions: new[] { new Position(4, 5) },
                defenderPositions: new[] { new Position(6.5f, 5) },
                terrain: new[] { new TerrainData(ETerrainType.Cover, ThinWall) });
            Assert.That(result.DefenseRollBonus, Is.EqualTo(1));
        }

        [Test]
        public async Task ShooterAtForestFarEdge_ShootingOut_NoBonus()
        {
            // Shooter inside the forest 0.25" (base surface) from its far edge at x=12; target
            // outside in the open. The exit hugs the shooter -> voided, same as a thin wall.
            CoverCheckResults result = await RunStage(
                shooterPositions: new[] { new Position(11, 6) },
                defenderPositions: new[] { new Position(20, 6) },
                terrain: new[] { new TerrainData(ETerrainType.Cover, Forest) });
            Assert.That(result.DefenseRollBonus, Is.EqualTo(0));
        }

        [Test]
        public async Task ShooterDeepInForest_ShootingOut_BonusStands()
        {
            // Exit at (12,6) is 7.25" from the shooter's base - the shot genuinely traverses the
            // trees, so the defender in the open keeps the bonus (no depth/shoot-through rule).
            CoverCheckResults result = await RunStage(
                shooterPositions: new[] { new Position(4, 6) },
                defenderPositions: new[] { new Position(20, 6) },
                terrain: new[] { new TerrainData(ETerrainType.Cover, Forest) });
            Assert.That(result.DefenseRollBonus, Is.EqualTo(1));
        }

        // ---- Rule 2: shared cover ----

        [Test]
        public async Task BothInSameForest_CloserThanSixInches_NoBonus()
        {
            // Centers 4" apart -> 2.5" base-to-base: a knife fight in the woods, no cover.
            CoverCheckResults result = await RunStage(
                shooterPositions: new[] { new Position(2, 6) },
                defenderPositions: new[] { new Position(6, 6) },
                terrain: new[] { new TerrainData(ETerrainType.Cover, Forest) });
            Assert.That(result.DefenseRollBonus, Is.EqualTo(0));
        }

        [Test]
        public async Task BothInSameForest_BeyondSixInches_BonusStands()
        {
            // Centers 8" apart -> 6.5" base-to-base: enough forest between them, cover stands.
            // (Rule 1 cannot fire here: the target stands inside the piece, so there is no exit.)
            CoverCheckResults result = await RunStage(
                shooterPositions: new[] { new Position(2, 6) },
                defenderPositions: new[] { new Position(10, 6) },
                terrain: new[] { new TerrainData(ETerrainType.Cover, Forest) });
            Assert.That(result.DefenseRollBonus, Is.EqualTo(1));
        }

        [Test]
        public async Task SeparateWallInsideSharedForest_WallStillGrantsCover()
        {
            // Rule 2 voids only the SHARED piece: the forest is voided (3.5" base-to-base), but a
            // distinct wall between the two models still screens the defender.
            CoverCheckResults result = await RunStage(
                shooterPositions: new[] { new Position(2, 6) },
                defenderPositions: new[] { new Position(7, 6) },
                terrain: new[]
                {
                    new TerrainData(ETerrainType.Cover, Forest),
                    new TerrainData(ETerrainType.Cover, new RectangularZone(4.5f, 5.0f, 0, 12)),
                });
            Assert.That(result.DefenseRollBonus, Is.EqualTo(1));
        }

        // ---- Composition with the majority rule ----

        [Test]
        public async Task MajorityComposition_VoidedLinesDropDefendersBelowMajority()
        {
            // Shooter hugs the thin wall: sight lines to defenders A and B are voided; defender C
            // sits behind its OWN wall far from the shooter, which still counts. 1/3 in cover -> no
            // majority -> no bonus. (Toggle off, all three count -> bonus; pinned above.)
            CoverCheckResults result = await RunStage(
                shooterPositions: new[] { new Position(4, 5) },
                defenderPositions: new[]
                {
                    new Position(20, 3),
                    new Position(20, 5),
                    new Position(20, 15),
                },
                terrain: new[]
                {
                    new TerrainData(ETerrainType.Cover, ThinWall),
                    // Defender-side wall crossing only the line to (20, 15).
                    new TerrainData(ETerrainType.Cover, new RectangularZone(18, 18.5f, 12, 17)),
                });
            Assert.That(result.DefenseRollBonus, Is.EqualTo(0));
        }

        // ---- Settings serialization (default-ON contract) ----

        [Test]
        public void Settings_FieldAbsentFromJson_ResolvesToEnabled()
        {
            // Pre-#201 saves have no CoverProximityExceptions field - they must resume with the
            // house rules ON (the default), not silently OFF.
            GameSettings settings = JsonConvert.DeserializeObject<GameSettings>("{}");
            Assert.That(settings.CoverProximityExceptions, Is.Null);
            Assert.That(settings.CoverProximityExceptionsEnabled, Is.True);
        }

        [Test]
        public void Settings_ExplicitOff_SurvivesRoundTrip()
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.CoverProximityExceptions = false;
            string json = JsonConvert.SerializeObject(settings);
            GameSettings back = JsonConvert.DeserializeObject<GameSettings>(json);
            Assert.That(back.CoverProximityExceptionsEnabled, Is.False);
        }

        [Test]
        public void Settings_Default_IsEnabled()
        {
            Assert.That(GameSettings.GetDefault().CoverProximityExceptionsEnabled, Is.True);
        }

        // Helpers (CoverMajorityTests pattern, plus injectable shooter positions/settings)

        private async Task<CoverCheckResults> RunStage(Position[] shooterPositions,
            Position[] defenderPositions, ITerrain[] terrain, GameSettings? settings = null)
        {
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), settings: settings);
            foreach (ITerrain piece in terrain)
            {
                _store.Create((TerrainData)piece);
            }

            DataBinding<UnitData> attacker = MakeUnit(shooterPositions);
            DataBinding<UnitData> defender = MakeUnit(defenderPositions);

            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new CoverCheckStage(ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 1);

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out CoverCheckResults result), Is.True,
                "Stage must store a CoverCheckResults in metadata.");
            return result;
        }

        private DataBinding<UnitData> MakeUnit(IEnumerable<Position> positions)
        {
            var models = positions.Select(pos =>
            {
                var md = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    initialPosition: pos,
                    gameDataStore: _store);
                return _store.GetDataBinding<ModelData>(_store.Create(md));
            }).ToList();

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: models);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
