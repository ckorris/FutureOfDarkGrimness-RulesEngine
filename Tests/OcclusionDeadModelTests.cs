using FDG.Data;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #385: OcclusionCheckStage used to iterate raw model bindings, so a DEAD model's sight line
    // could keep a shot alive (or, on the defender side, keep the unit visible) after mid-activation
    // casualties - disagreeing with the targeting stage, which only ever offered living models'
    // shots. The stage now asks ShotEligibility.UnitSeesUnit (living, placed models on both sides),
    // the same per-model sight test the previews and the attack animation use.
    //
    // Geometry (shared with IndirectLineOfSightRuleIntegrationTests): a Blocking wall at x:8-12,
    // z:3-7. A model at (0,5) shooting at (20,5) is blocked; a model at (0,25) - or a target at
    // (20,25) - has a clear line over the wall.
    [TestFixture]
    public class OcclusionDeadModelTests
    {
        private static readonly Position BlockedShooterPos = new Position(0, 5);
        private static readonly Position ClearShooterPos   = new Position(0, 25);
        private static readonly Position BehindWallPos     = new Position(20, 5);
        private static readonly Position VisiblePos        = new Position(20, 25);
        private static readonly RectangularZone WallRect   = new RectangularZone(8, 12, 3, 7);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            _store.Create(new TerrainData(ETerrainType.Blocking, WallRect));
        }

        [Test]
        public async Task DeadAttackerWithTheOnlyClearLine_ShotIsOccluded()
        {
            DataBinding<UnitData> attacker = MakeUnit(BlockedShooterPos, ClearShooterPos);
            Kill(attacker, 1); // the model with the clear line dies
            DataBinding<UnitData> defender = MakeUnit(BehindWallPos);

            Assert.That(await RunOcclusionStage(attacker, defender), Is.True,
                "a dead model's sight line must not keep the volley alive");
        }

        [Test]
        public async Task LivingAttackerWithAClearLine_ShotNotOccluded()
        {
            // Sanity converse: same unit, nobody dead - the clear line stands.
            DataBinding<UnitData> attacker = MakeUnit(BlockedShooterPos, ClearShooterPos);
            DataBinding<UnitData> defender = MakeUnit(BehindWallPos);

            Assert.That(await RunOcclusionStage(attacker, defender), Is.False);
        }

        [Test]
        public async Task DeadDefenderAsTheOnlyVisibleModel_ShotIsOccluded()
        {
            DataBinding<UnitData> attacker = MakeUnit(BlockedShooterPos);
            DataBinding<UnitData> defender = MakeUnit(BehindWallPos, VisiblePos);
            Kill(defender, 1); // the visible model dies; only the one behind the wall lives

            Assert.That(await RunOcclusionStage(attacker, defender), Is.True,
                "a corpse must not keep its unit visible to the shooter");
        }

        // Helpers

        private static void Kill(DataBinding<UnitData> unit, int modelIndex)
        {
            ModelData model = unit.GetValue().ModelBindings[modelIndex].GetValue();
            model.DealWounds(model.TotalWounds - model.WoundsDealt);
        }

        // Returns true if the shot was occluded (OnOccluded fired, no result stored).
        private async Task<bool> RunOcclusionStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var stage = new OcclusionCheckStage(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            stage.OnOccluded.Bind("occluded");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);

            await stage.Enter(metadata);

            return !metadata.QueryForResult(out OcclusionCheckResults _);
        }

        private DataBinding<UnitData> MakeUnit(params Position[] positions)
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
