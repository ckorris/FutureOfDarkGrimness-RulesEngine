using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #159: the AI consolidation resolver must never emit a move ConsolidateStage rejects. A unit left out of
    // coherency by a mid-unit casualty can't consolidate as a rigid delta (that preserves the hole), so the
    // resolver re-forms the survivors toward their centroid within the cap — pulling them back toward
    // coherency and always producing a move the (lenient) ConsolidateStage validator accepts.
    [TestFixture]
    public class AiConsolidationMoveResolverTests
    {
        [Test]
        public async Task Resolve_CasualtyHoledUnit_ProducesStageValidMoveThatTightensCohesion()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());

            // Five models in a tight row; the middle three die, leaving survivors ~4.4" apart (badly holed).
            const float r = 0.5f;
            const float spacing = 2f * r + 0.1f;
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 5; i++)
            {
                var m = new ModelData(r, new List<Weapon>(), new Position(i * spacing, 20f), store);
                bindings.Add(store.GetDataBinding<ModelData>(store.Create(m)));
            }
            foreach (int dead in new[] { 1, 2, 3 })
                bindings[dead].GetValue().DealWounds(bindings[dead].GetValue().TotalWounds);

            var unit = new UnitData(selfPlayer, "Survivors", 4, 4, bindings);
            var unitBinding = store.GetDataBinding<UnitData>(store.Create(unit));
            store.Create(new ArmyData(selfPlayer, new List<DataBinding<UnitData>> { unitBinding }));

            // A defender in melee range (drives a Disengage, 1" cap).
            var enemyPos = new Position(0f, 21.5f);
            var enemy = new ModelData(r, new List<Weapon>(), enemyPos, store);
            var enemyBinding = store.GetDataBinding<ModelData>(store.Create(enemy));
            var enemyUnit = new UnitData(enemyPlayer, "Enemies", 4, 4,
                new List<DataBinding<ModelData>> { enemyBinding });
            var enemyUnitBinding = store.GetDataBinding<UnitData>(store.Create(enemyUnit));
            store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemyUnitBinding }));

            var tableState = new TableState(store);
            var resolver = new AiConsolidationMoveResolver(tableState, selfPlayer);
            var request = new ConsolidationMoveRequest(selfPlayer, "Consolidate", unitBinding,
                ConsolidateStage.DISENGAGE_MAX_DISTANCE_INCHES, EConsolidationReason.Disengage,
                canMoveThroughEnemies: false, ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false);

            var footprints = new List<EnemyModelFootprint> { new EnemyModelFootprint(enemyPos, r, 0) };
            List<ModelMoveEntry> result = await resolver.Resolve(request);

            // The move ConsolidateStage will actually run must pass its (lenient) consolidation validator.
            bool valid = MovementUtilities.ValidateConsolidationPaths(result,
                ConsolidateStage.DISENGAGE_MAX_DISTANCE_INCHES, footprints,
                canMoveThroughEnemies: false, ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false,
                terrain: new List<ITerrain>(), out var errors);
            Assert.That(valid, Is.True,
                "AI consolidation must be stage-valid: " + string.Join(", ", errors.Select(e => e.ToString())));

            // And it must be an *attempt to bring them together*: the survivors end closer than the 4.4" hole.
            var living = bindings.Where(b => b.GetValue().GetIsAlive()).ToList();
            var byModel = result.ToDictionary(e => e.Model, e => e.Positions[e.Positions.Count - 1]);
            float endGap = Position.GetDistance2D(byModel[living[0]], byModel[living[1]]);
            Assert.That(endGap, Is.LessThan(4.0f), "consolidation should pull the holed survivors closer together");
        }
    }
}
