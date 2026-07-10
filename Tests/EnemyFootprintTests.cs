using System;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #207 pin: an embarked unit's models are parked at the origin (EmbarkStage), and
    // GetEnemyModelFootprints used to include them - making (0,0) an invisible wall that
    // rejected any legal move sweeping near the table corner whenever the enemy had a loaded
    // transport (every Dark-Elf-transport pool fault). Off-battlefield units are not obstacles.
    [TestFixture]
    public class EnemyFootprintTests
    {
        private GameDataStore _store = null!;
        private PlayerID _us;
        private PlayerID _them;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public void EmbarkedEnemyUnit_LeavesNoFootprintAtTheOrigin()
        {
            DataBinding<UnitData> mover = MakeUnit(_us, "Mover", modelCount: 2, atX: 5f, atZ: 5f);
            DataBinding<UnitData> transport = MakeTransport("Raider", capacity: 6, atX: 30f, atZ: 30f);
            DataBinding<UnitData> cargo = MakeUnit(_them, "Passengers", modelCount: 5, atX: 0f, atZ: 0f);
            TransportUtilities.Embark(cargo.GetValue(), transport.GetValue());

            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            var footprints = MovementUtilities.GetEnemyModelFootprints(mover, ctx);

            Assert.That(footprints.Count, Is.EqualTo(1),
                "only the deployed transport is an obstacle; its embarked passengers are not.");
            Assert.That(footprints[0].Center.x, Is.EqualTo(30f).Within(0.001f));
        }

        [Test]
        public void DeployedEnemyUnit_IsStillAnObstacle()
        {
            DataBinding<UnitData> mover = MakeUnit(_us, "Mover", modelCount: 2, atX: 5f, atZ: 5f);
            MakeUnit(_them, "Deployed", modelCount: 3, atX: 12f, atZ: 12f);

            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            var footprints = MovementUtilities.GetEnemyModelFootprints(mover, ctx);

            Assert.That(footprints.Count, Is.EqualTo(3), "on-battlefield enemies keep their footprints.");
        }

        // --- fixtures ---

        private DataBinding<UnitData> MakeUnit(PlayerID owner, string name, int modelCount,
            float atX, float atZ)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(),
                    new Position(atX + (i % 2) * 1.1f, atZ + (i / 2) * 1.1f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: modelBindings);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private DataBinding<UnitData> MakeTransport(string name, int capacity, float atX, float atZ)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(atX, atZ), _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(_them, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            unit.AttachRuleDefinition(new ResolvedRule(TransportUtilities.TransportRuleName,
                CoreRuleCatalog.Transport, new RuleArgument[] { new RuleArgument.Int(capacity) }));
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_them, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
