using FDG.Data;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Verifies CoverCheckStage majority-rule: more than half of defending models
    // must have a Cover LoS from any attacker to earn the +1 defense bonus.
    //
    // Geometry (all tests share one attacker at (0,5) and Cover terrain at x:8-12, z:3-7):
    //   - "in cover"     defender position: (20, 5)  — line passes through Cover rect
    //   - "not in cover" defender position: (20, 15) — line goes above the rect
    [TestFixture]
    public class CoverMajorityTests
    {
        private static readonly Position AttackerPos  = new Position(0, 5);
        private static readonly Position InCoverPos   = new Position(20, 5);
        private static readonly Position NotInCoverPos = new Position(20, 15);
        private static readonly RectangularZone CoverRect = new RectangularZone(8, 12, 3, 7);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;
        private List<ITerrain> _coverTerrain = null!;
        private DataBinding<UnitData> _attacker = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            _coverTerrain = new List<ITerrain> { new TerrainData(ETerrainType.Cover, CoverRect) };
            _store.Create(new TerrainData(ETerrainType.Cover, CoverRect));
            _attacker = MakeUnit(new[] { AttackerPos });
        }

        [Test]
        public async Task OneDefender_NotInCover_ZeroBonus()
        {
            DataBinding<UnitData> defender = MakeUnit(new[] { NotInCoverPos });
            CoverCheckResults result = await RunStage(defender);
            Assert.That(result.DefenseRollBonus, Is.EqualTo(0));
        }

        [Test]
        public async Task OneDefender_InCover_BonusOne()
        {
            DataBinding<UnitData> defender = MakeUnit(new[] { InCoverPos });
            CoverCheckResults result = await RunStage(defender);
            Assert.That(result.DefenseRollBonus, Is.EqualTo(1));
        }

        [Test]
        public async Task TwoDefenders_OneInCover_ZeroBonus()
        {
            // 1/2 = 50 % — not strictly greater than half, so no bonus.
            DataBinding<UnitData> defender = MakeUnit(new[] { InCoverPos, NotInCoverPos });
            CoverCheckResults result = await RunStage(defender);
            Assert.That(result.DefenseRollBonus, Is.EqualTo(0));
        }

        [Test]
        public async Task TwoDefenders_BothInCover_BonusOne()
        {
            DataBinding<UnitData> defender = MakeUnit(new[] { InCoverPos, InCoverPos });
            CoverCheckResults result = await RunStage(defender);
            Assert.That(result.DefenseRollBonus, Is.EqualTo(1));
        }

        [Test]
        public async Task ThreeDefenders_TwoInCover_BonusOne()
        {
            // 2/3 > 50 % → bonus.
            DataBinding<UnitData> defender = MakeUnit(new[] { InCoverPos, InCoverPos, NotInCoverPos });
            CoverCheckResults result = await RunStage(defender);
            Assert.That(result.DefenseRollBonus, Is.EqualTo(1));
        }

        [Test]
        public async Task ThreeDefenders_OneInCover_ZeroBonus()
        {
            // 1/3 ≤ 50 % → no bonus.
            DataBinding<UnitData> defender = MakeUnit(new[] { InCoverPos, NotInCoverPos, NotInCoverPos });
            CoverCheckResults result = await RunStage(defender);
            Assert.That(result.DefenseRollBonus, Is.EqualTo(0));
        }

        // Helpers

        private async Task<CoverCheckResults> RunStage(DataBinding<UnitData> defender)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new CoverCheckStage(_ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 24f, attacks: 1, armorPenetration: 0,
                specialRules: new HashSet<ISpecialRule_Weapon>());
            var metadata = new CombatMetadata(_ctx, _attacker, defender, weapon, weaponCount: 1);

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
                    specialRules: new List<SpecialRule>(),
                    initialPosition: pos,
                    gameDataStore: _store);
                return _store.GetDataBinding<ModelData>(_store.Create(md));
            }).ToList();

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(), modelBindings: models);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
