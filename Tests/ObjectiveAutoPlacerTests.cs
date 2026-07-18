using System;
using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #243 — the Auto-Placed objective mode and the solo-rules AI share one placement helper
    // (ObjectiveAutoPlacer). These pin the two guarantees that share buys: every placement is legal,
    // and the helper is identical to AiPlaceObjectiveResolver for the same table state + random stream.
    [TestFixture]
    public class ObjectiveAutoPlacerTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private PlayerID _player;

        // Full-table legal band between the two 12" deployment margins, matching PlaceObjectivesStage.
        private static RectangularZone Band() =>
            new RectangularZone(left: 0f, right: 72f, bottom: 12f, top: 36f);

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _player = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public void AutoPlacer_ProducesLegalPlacements()
        {
            var band = Band();
            var rng = new Random(1234);
            var placed = new List<Position>();

            for (int i = 1; i <= 5; i++)
            {
                bool found = ObjectiveAutoPlacer.TryChoosePlacement(
                    band, minSeparationInches: 9f, markerIndex: i, totalMarkers: 5,
                    existing: _tableState.Objectives.Objects.ToList(),
                    impassable: new List<ITerrain>(), rng, out Position p);

                Assert.That(found, Is.True, $"a legal spot exists for marker {i}");
                Assert.That(band.IsPointWithinZone(new Float2(p.x, p.z)), Is.True, "placement is inside the legal band");
                _store.Create(new ObjectiveData(p, _store));
                placed.Add(p);
            }

            for (int i = 0; i < placed.Count; i++)
                for (int j = i + 1; j < placed.Count; j++)
                {
                    float dx = placed[i].x - placed[j].x, dz = placed[i].z - placed[j].z;
                    Assert.That(MathF.Sqrt(dx * dx + dz * dz), Is.GreaterThanOrEqualTo(9f - 0.01f),
                        "markers respect the separation rule");
                }
        }

        // The whole point of the shared helper: the Auto-Placed map-setup path and the AI resolver
        // must place markers identically given the same seed, so a solo-rules game looks the same
        // whichever objective mode the lobby chose. Same seed in -> same positions out, marker by marker.
        [Test]
        public async Task AutoPlacer_MatchesAiResolver_ForSameSeed()
        {
            var band = Band();
            const int seed = 98765;
            const int total = 4;

            var helperRng = new Random(seed);
            var resolver = new AiPlaceObjectiveResolver(_tableState, new Random(seed));

            for (int i = 1; i <= total; i++)
            {
                // Snapshot the store the way the resolver will read it, then run both against it.
                var existing = _tableState.Objectives.Objects.ToList();

                bool found = ObjectiveAutoPlacer.TryChoosePlacement(
                    band, minSeparationInches: 9f, markerIndex: i, totalMarkers: total,
                    existing, new List<ITerrain>(), helperRng, out Position fromHelper);
                Assert.That(found, Is.True);

                Position fromResolver = await resolver.Resolve(new PlaceObjectiveRequest(
                    _player, "Place Objective", i, total, band, minSeparationInches: 9f));

                Assert.That(fromResolver.x, Is.EqualTo(fromHelper.x), $"marker {i} X matches");
                Assert.That(fromResolver.z, Is.EqualTo(fromHelper.z), $"marker {i} Z matches");

                _store.Create(new ObjectiveData(fromResolver, _store));
            }
        }
    }
}
