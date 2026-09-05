using FDG.Ai.Tactician.Learning;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 step 10 (2026-09-05): pins for the v2 per-side feature obj_held_threatened_share
    // (block[15]) - of this side's projected-held markers, the share an opposing unit can still
    // reach this round. The encoder had no unit tests before this (step 4 verified it through the
    // exported data in pandas); these cover the one feature the evaluator's second fix rests on.
    [TestFixture]
    public class PositionEncoderTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private RuleEvaluator _evaluator = null!;
        private PlayerID _us;
        private PlayerID _them;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public void Width_IsV2()
        {
            Assert.That(PositionEncoder.SchemaVersion, Is.EqualTo(2));
            Assert.That(PositionEncoder.PerSideFeatureCount, Is.EqualTo(16));
            Assert.That(PositionEncoder.VectorWidth, Is.EqualTo(71));
        }

        [Test]
        public void HeldMarker_WithAnEnemyInReach_IsThreatened()
        {
            MakeUnit(_us, 3, atX: 30f, atZ: 30f);                 // on the marker: holds it
            _store.Create(new ObjectiveData(new Position(30f, 30f), _store));
            DataBinding<UnitData> enemy = MakeUnit(_them, 3, atX: 30f, atZ: 40f); // 10" away: rifle 24 + advance covers it

            float[] ours = Block(_us, _them);
            Assert.That(ours[6], Is.EqualTo(1f).Within(1e-6f), "we hold the only marker");
            Assert.That(ours[15], Is.EqualTo(1f).Within(1e-6f), "an enemy in reach makes it threatened");

            // Move the enemy far beyond any reach (advance ~6 + rifle 24 + 3 seizure = 33"): safe now.
            foreach (DataBinding<ModelData> model in enemy.GetValue().ModelBindings)
                model.GetValue().PositionBinding.SetValue(new Position(30f, 30f + 60f));
            Assert.That(Block(_us, _them)[15], Is.EqualTo(0f).Within(1e-6f), "out of reach: not threatened");
        }

        [Test]
        public void LastRound_OnlyAnUnactivatedEnemyThreatens()
        {
            MakeUnit(_us, 3, atX: 30f, atZ: 30f);
            _store.Create(new ObjectiveData(new Position(30f, 30f), _store));
            DataBinding<UnitData> enemy = MakeUnit(_them, 3, atX: 30f, atZ: 40f);
            SetRound(GameWideConstants.NUMBER_OF_ROUNDS);

            Assert.That(Block(_us, _them)[15], Is.EqualTo(1f).Within(1e-6f), "last round, enemy unactivated: threat");
            enemy.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ActivatedThisRound));
            Assert.That(Block(_us, _them)[15], Is.EqualTo(0f).Within(1e-6f),
                "last round, enemy already activated: it never acts again, so no threat");
        }

        [Test]
        public void BeforeTheLastRound_AnActivatedEnemyStillThreatens()
        {
            MakeUnit(_us, 3, atX: 30f, atZ: 30f);
            _store.Create(new ObjectiveData(new Position(30f, 30f), _store));
            DataBinding<UnitData> enemy = MakeUnit(_them, 3, atX: 30f, atZ: 40f);
            SetRound(2);
            enemy.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ActivatedThisRound));
            Assert.That(Block(_us, _them)[15], Is.EqualTo(1f).Within(1e-6f),
                "round 2, activated enemy in reach: it acts again next round, so it still threatens");
        }

        [Test]
        public void UnheldMarker_IsNeverCountedAsThreatened()
        {
            MakeUnit(_us, 3, atX: 10f, atZ: 10f);                 // nowhere near the marker
            _store.Create(new ObjectiveData(new Position(30f, 30f), _store));
            MakeUnit(_them, 3, atX: 30f, atZ: 40f);
            float[] ours = Block(_us, _them);
            Assert.That(ours[6], Is.EqualTo(0f).Within(1e-6f));
            Assert.That(ours[15], Is.EqualTo(0f).Within(1e-6f), "only HELD markers can be threatened");
        }

        // --- helpers -------------------------------------------------------------------------------

        private float[] Block(PlayerID side, PlayerID opposing) =>
            PositionEncoder.EncodeSideBlock(_tableState, _evaluator, new List<PlayerID> { side }, new List<PlayerID> { opposing });

        private void SetRound(int round)
        {
            var progress = new GameProgressData(EResumeStage.MainPhase, round,
                new List<int>(), new List<int>(), 0, new Dictionary<int, int>(),
                new List<DataBinding<UnitData>>(), GameSettings.GetDefault());
            GameProgressUtilities.WriteProgress(_store, progress);
        }

        private DataBinding<UnitData> MakeUnit(PlayerID owner, int modelCount, float atX, float atZ,
            int quality = 4, int defense = 4)
        {
            var weapon = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon },
                    new Position(atX + (i % 2) * 1.1f, atZ + (i / 2) * 1.1f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"U{owner}_{atX}_{atZ}", quality, defense, modelBindings: modelBindings);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
