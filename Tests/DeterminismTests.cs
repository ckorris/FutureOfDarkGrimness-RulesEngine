using System.Collections.Concurrent;
using System.Text;
using FDG.Ai;
using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #193 — the determinism contract, and the reason it exists: automated self-play (#191) runs many
    // games at once in one process, then re-runs them to compare agents. If any randomness escapes the
    // per-game seed, benchmark numbers are noise and a "bug" can never be reproduced.
    //
    // Two properties are pinned here:
    //   (1) same seed + same build => identical game, and
    //   (2) that stays true when 16 games run concurrently (no static/shared RNG cross-talk).
    [TestFixture]
    public class DeterminismTests
    {
        private const int ConcurrentGames = 16;

        // --- Roller level -------------------------------------------------------------------------

        [Test]
        public void ProbabilisticRoller_SameSeed_ProducesSameDecisiveSequence()
        {
            int[] first = DecisiveFaces(new ProbabilisticDiceRoller(seed: 7), count: 50);
            int[] second = DecisiveFaces(new ProbabilisticDiceRoller(seed: 7), count: 50);

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void ProbabilisticRoller_DifferentSeeds_DivergeSomewhere()
        {
            int[] a = DecisiveFaces(new ProbabilisticDiceRoller(seed: 7), count: 50);
            int[] b = DecisiveFaces(new ProbabilisticDiceRoller(seed: 8), count: 50);

            Assert.That(b, Is.Not.EqualTo(a), "two seeds producing an identical 50-roll run means the seed is ignored.");
        }

        [Test]
        public void ProbabilisticRoller_TwoInstances_DoNotShareAStream()
        {
            // The pre-#193 roller held a `static readonly Random`, so interleaved use of two rollers
            // consumed one shared sequence: this assertion failed, and concurrent games polluted each other.
            var a = new ProbabilisticDiceRoller(seed: 99);
            var b = new ProbabilisticDiceRoller(seed: 99);

            for (int i = 0; i < 25; i++)
                Assert.That(b.RollDecisiveFace(), Is.EqualTo(a.RollDecisiveFace()), $"diverged at roll {i}");
        }

        [Test]
        public void ProbabilisticRoller_SixteenConcurrentInstances_EachReproducesTheSoloSequence()
        {
            int[] solo = DecisiveFaces(new ProbabilisticDiceRoller(seed: 4242), count: 200);

            var results = new ConcurrentBag<int[]>();
            Parallel.For(0, ConcurrentGames, _ =>
                results.Add(DecisiveFaces(new ProbabilisticDiceRoller(seed: 4242), count: 200)));

            Assert.That(results, Has.Count.EqualTo(ConcurrentGames));
            foreach (int[] run in results)
                Assert.That(run, Is.EqualTo(solo), "a concurrent roller diverged from the solo sequence.");
        }

        [Test]
        public void RealisticRoller_SameSeed_ProducesSameDecisiveSequence()
        {
            int[] first = DecisiveFaces(new RealisticDiceRoller(seed: 11), count: 50);
            int[] second = DecisiveFaces(new RealisticDiceRoller(seed: 11), count: 50);

            Assert.That(second, Is.EqualTo(first));
        }

        // --- Seed derivation ----------------------------------------------------------------------

        [Test]
        public void DeriveForSlot_IsStable_AndDistinctPerSlot()
        {
            Assert.That(GameRandom.DeriveForSlot(5, 0), Is.EqualTo(GameRandom.DeriveForSlot(5, 0)),
                "the same (seed, slot) must always derive the same sub-seed, run after run.");
            Assert.That(GameRandom.DeriveForSlot(5, 0), Is.Not.EqualTo(GameRandom.DeriveForSlot(5, 1)),
                "two AI players in one game must not share a random stream.");
            Assert.That(GameRandom.DeriveForSlot(null, 0), Is.Null, "unseeded in, unseeded out.");
        }

        [Test]
        public void GameRandom_DifferentSalts_GiveDifferentStreams()
        {
            Random dice = GameRandom.Create(seed: 3, salt: GameRandom.SALT_GAME_CONTEXT);
            Random ai = GameRandom.Create(seed: 3, salt: GameRandom.SALT_AI_PLAYER);

            int[] a = Enumerable.Range(0, 20).Select(_ => dice.Next(1000)).ToArray();
            int[] b = Enumerable.Range(0, 20).Select(_ => ai.Next(1000)).ToArray();

            Assert.That(b, Is.Not.EqualTo(a), "consumers sharing one seed must not march in lockstep.");
        }

        // --- Roll-offs ----------------------------------------------------------------------------

        [Test]
        public async Task RollOff_WithSameSeededRoller_ProducesSameOrder()
        {
            List<string> first = await RollOffOrder(new RealisticDiceRoller(seed: 2026));
            List<string> second = await RollOffOrder(new RealisticDiceRoller(seed: 2026));

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public async Task RollOff_InProbabilisticMode_StillResolvesToARealWinner()
        {
            // Roll-offs used to sidestep the context roller ("that may not be random") with a private
            // Random. They now use RollDecisive, which yields one concrete face even in probabilistic
            // mode — so the tie-break still terminates rather than reading every competitor as 3.5.
            List<string> order = await RollOffOrder(new ProbabilisticDiceRoller(seed: 1));

            Assert.That(order, Has.Count.EqualTo(4));
            Assert.That(order, Is.Unique);
        }

        // --- AI placement resolvers ---------------------------------------------------------------
        // Pinned directly, not just through a whole game: the solo-rules bot ignores objectives, so an
        // unseeded objective placer moves no models and a game-level fingerprint of model positions never
        // notices. (Confirmed by mutation: un-seeding the placer left the fresh-game test green until its
        // fingerprint learned to include objective positions.)

        [Test]
        public async Task AiObjectivePlacement_SameSeed_PlacesInTheSameSpot()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var tableState = new TableState(store);
            var playerID = new PlayerID(Guid.NewGuid());
            var band = new RectangularZone(left: 0f, right: 60f, bottom: 0f, top: 40f);

            Position first = await new AiPlaceObjectiveResolver(tableState, new Random(4321))
                .Resolve(new PlaceObjectiveRequest(playerID, "Place", 0, 3, band, 6f));
            Position second = await new AiPlaceObjectiveResolver(tableState, new Random(4321))
                .Resolve(new PlaceObjectiveRequest(playerID, "Place", 0, 3, band, 6f));

            Assert.That(second.x, Is.EqualTo(first.x).Within(0.0001f));
            Assert.That(second.z, Is.EqualTo(first.z).Within(0.0001f));
        }

        [Test]
        public async Task AiObjectivePlacement_DifferentSeeds_PlaceDifferently()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var tableState = new TableState(store);
            var playerID = new PlayerID(Guid.NewGuid());
            var band = new RectangularZone(left: 0f, right: 60f, bottom: 0f, top: 40f);

            Position a = await new AiPlaceObjectiveResolver(tableState, new Random(1))
                .Resolve(new PlaceObjectiveRequest(playerID, "Place", 0, 3, band, 6f));
            Position b = await new AiPlaceObjectiveResolver(tableState, new Random(2))
                .Resolve(new PlaceObjectiveRequest(playerID, "Place", 0, 3, band, 6f));

            Assert.That(a.x, Is.Not.EqualTo(b.x).Within(0.0001f), "the seed is not reaching the placer.");
        }

        [Test]
        public async Task AiTerrainPlacement_SameSeed_PlacesTheSamePiece()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var tableState = new TableState(store);
            var playerID = new PlayerID(Guid.NewGuid());
            var pool = new List<TerrainPieceEntry>
            {
                new TerrainPieceEntry { TerrainType = ETerrainType.Cover, Shape = new RectangularZone(0f, 6f, 0f, 6f), HeightInches = 2f },
                new TerrainPieceEntry { TerrainType = ETerrainType.Impassible, Shape = new RectangularZone(0f, 4f, 0f, 8f), HeightInches = 4f },
            };

            TerrainPlacementResult first = await new AiPlaceOneTerrainResolver(tableState, new Random(909))
                .Resolve(new PlaceOneTerrainRequest(playerID, "Terrain", 0, 4, pool, 72f, 48f));
            TerrainPlacementResult second = await new AiPlaceOneTerrainResolver(tableState, new Random(909))
                .Resolve(new PlaceOneTerrainRequest(playerID, "Terrain", 0, 4, pool, 72f, 48f));

            Assert.That(second.TemplateIndex, Is.EqualTo(first.TemplateIndex));
            Assert.That(second.RotationDegrees, Is.EqualTo(first.RotationDegrees).Within(0.0001f));
            Assert.That(second.Center.X, Is.EqualTo(first.Center.X).Within(0.0001f));
            Assert.That(second.Center.Y, Is.EqualTo(first.Center.Y).Within(0.0001f));
        }

        // --- Whole games --------------------------------------------------------------------------

        [Test]
        [CancelAfter(180_000)]
        public async Task SameSeed_WholeGame_ProducesIdenticalResultAndFinalState()
        {
            GameFingerprint first = await PlaySeededGame(seed: 12345);
            GameFingerprint second = await PlaySeededGame(seed: 12345);

            Assert.That(second.Summary, Is.EqualTo(first.Summary), "the same seed produced a different result.");
            Assert.That(second.FinalState, Is.EqualTo(first.FinalState),
                "the same seed left the models in different places / at different wounds.");
        }

        [Test]
        [CancelAfter(180_000)]
        public async Task DifferentSeeds_WholeGame_DivergeSomewhere()
        {
            GameFingerprint a = await PlaySeededGame(seed: 12345);
            GameFingerprint b = await PlaySeededGame(seed: 54321);

            Assert.That(a.Summary + a.FinalState, Is.Not.EqualTo(b.Summary + b.FinalState),
                "two seeds producing a byte-identical game means the seed is not reaching the game.");
        }

        // The scenario tests above RESUME at round 2, which skips map setup and deployment — so they never
        // touch the roll-offs, the objective placer, or the AI's placement RNG. These play a FRESH game,
        // in probabilistic mode, so every path #193 seeded is exercised: decisive rolls, four roll-offs,
        // and the AI objective-placement stream.

        [Test]
        [CancelAfter(180_000)]
        public async Task SameSeed_FreshGameThroughSetupAndDeployment_IsIdentical()
        {
            GameFingerprint first = await PlaySeededFreshGame(seed: 31337);
            GameFingerprint second = await PlaySeededFreshGame(seed: 31337);

            Assert.That(second.Summary, Is.EqualTo(first.Summary));
            Assert.That(second.FinalState, Is.EqualTo(first.FinalState),
                "map setup, deployment, or an AI placement drew from randomness the seed cannot reach.");
        }

        [Test]
        [CancelAfter(180_000)]
        public async Task DifferentSeeds_FreshGame_DivergeSomewhere()
        {
            GameFingerprint a = await PlaySeededFreshGame(seed: 31337);
            GameFingerprint b = await PlaySeededFreshGame(seed: 90210);

            Assert.That(a.FinalState, Is.Not.EqualTo(b.FinalState),
                "if two seeds yield an identical fresh game, the same-seed test above proves nothing.");
        }

        [Test]
        [CancelAfter(300_000)]
        public async Task SameSeed_FreshGameAmidConcurrentGames_MatchesTheSoloRun()
        {
            GameFingerprint solo = await PlaySeededFreshGame(seed: 8675309);

            GameFingerprint[] results = await Task.WhenAll(Enumerable.Range(0, ConcurrentGames)
                .Select(_ => Task.Run(() => PlaySeededFreshGame(seed: 8675309))));

            foreach (GameFingerprint run in results)
            {
                Assert.That(run.Summary, Is.EqualTo(solo.Summary));
                Assert.That(run.FinalState, Is.EqualTo(solo.FinalState),
                    "a fresh game run alongside others diverged - randomness is shared across games.");
            }
        }

        [Test]
        [CancelAfter(300_000)]
        public async Task SameSeed_GameRunAmidSixteenConcurrent_MatchesTheSoloRun()
        {
            // The cross-talk detector, and the one that would have caught the old static Random: if any
            // randomness is shared between games, a game run alongside 15 others diverges from itself.
            GameFingerprint solo = await PlaySeededGame(seed: 777);

            Task<GameFingerprint>[] concurrent = Enumerable.Range(0, ConcurrentGames)
                .Select(_ => Task.Run(() => PlaySeededGame(seed: 777)))
                .ToArray();
            GameFingerprint[] results = await Task.WhenAll(concurrent);

            foreach (GameFingerprint run in results)
            {
                Assert.That(run.Summary, Is.EqualTo(solo.Summary), "a concurrent game reached a different result.");
                Assert.That(run.FinalState, Is.EqualTo(solo.FinalState), "a concurrent game reached a different final state.");
            }
        }

        // --- Helpers ------------------------------------------------------------------------------

        private static int[] DecisiveFaces(IDiceRoller roller, int count) =>
            Enumerable.Range(0, count).Select(_ => roller.RollDecisiveFace()).ToArray();

        /// <summary>
        /// Guards against the fixture's central failure mode: two games that FAULTED identically satisfy
        /// every equality assertion here and "prove" determinism while proving nothing.
        /// <para>
        /// <paramref name="expectedRounds"/> is null for resumed games. A resumed game currently plays four
        /// MORE rounds instead of finishing the four-round game (a game resumed at round 2 runs 2..5) —
        /// pre-existing resume bug, filed as #195. Pinning the buggy 5 here would cement it, so resumed
        /// games only assert they did not fault.
        /// </para>
        /// </summary>
        private static void AssertReallyPlayed(GameResult result, int? expectedRounds)
        {
            Assert.That(result.Outcome, Is.Not.EqualTo(EGameOutcome.Fault),
                $"the game faulted instead of playing: {result.Message}");
            Assert.That(result.RoundsPlayed, Is.GreaterThan(0), "the game never reached the main phase.");

            if (expectedRounds.HasValue)
                Assert.That(result.RoundsPlayed, Is.EqualTo(expectedRounds.Value), "the game ended early.");
        }

        private static Task<List<string>> RollOffOrder(IDiceRoller roller)
        {
            var competitors = new List<string> { "A", "B", "C", "D" };
            return DiceUtilities.RollOff_Ordered(competitors, new List<string>(competitors),
                new EmptyTextOutput(), roller);
        }

        /// <summary>A game's observable outcome: the structured result (#192) plus the final board.</summary>
        private readonly record struct GameFingerprint(string Summary, string FinalState);

        /// <summary>
        /// Plays a compiled scenario to completion, AI on every slot, entirely in-process. The scenario
        /// resumes at round 2, so it exercises dice, decisive rolls (morale), and AI decisions — the paths
        /// a seed has to reach. Slot IDs (not the freshly-minted PlayerID GUIDs) key the AI seeds.
        /// </summary>
        private static async Task<GameFingerprint> PlaySeededGame(int seed)
        {
            int? expectedRounds = null; // resumed game: see AssertReallyPlayed / #195
            ScenarioFile scenario = MakeScenario(seed);
            GameDataStore store = ScenarioCompiler.Compile(scenario, new[] { MakeShooterArmy(), MakeDefenderArmy() });

            List<PlayerSlotInfo> savedInfos = store.GetAllValues<PlayerSlotInfo>()
                .OrderBy(info => info.SlotID).ToList();
            foreach (DataReference oldInfo in store.GetAllDataReferences<PlayerSlotInfo>().ToList())
                store.Destroy(oldInfo);

            var bus = new InProcessBus();
            var slots = new PlayerSlot[savedInfos.Count];
            for (int i = 0; i < savedInfos.Count; i++)
            {
                slots[i] = new PlayerSlot(i, savedInfos[i].TeamNumber, savedInfos[i].PlayerID, new ArmyListFile(), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                    $"AI {i}", savedInfos[i].PlayerID, aiGame, seed, slots[i].SlotID));
            }

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the seeded game must play to completion without hanging.");

            GameResult gameResult = await completed.Task;
            AssertReallyPlayed(gameResult, expectedRounds);

            return new GameFingerprint(gameResult.ToSummaryLine(), FingerprintFinalState(store));
        }

        /// <summary>
        /// Plays a FRESH game (map setup -> deployment -> 4 rounds), AI on both slots, in probabilistic
        /// mode. Unlike the scenario resume, this runs the roll-offs, objective placement and deployment,
        /// so it covers every randomness site #193 seeded.
        /// </summary>
        private static async Task<GameFingerprint> PlaySeededFreshGame(int seed)
        {
            int? expectedRounds = GameWideConstants.NUMBER_OF_ROUNDS; // a fresh game plays exactly the full game
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();

            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()),
                    i == 0 ? MakeShooterArmy() : MakeDefenderArmy(), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                    $"AI {i}", slots[i].PlayerID, aiGame, seed, slots[i].SlotID));
            }

            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic; // exercises the seeded decisive rolls
            settings.DiceSeed = seed;
            settings.AutoPlaceObjectivesDebug = false;               // exercises the AI objective placer

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the seeded fresh game must play to completion without hanging.");

            GameResult gameResult = await completed.Task;
            AssertReallyPlayed(gameResult, expectedRounds);

            return new GameFingerprint(gameResult.ToSummaryLine(), FingerprintFinalState(store));
        }

        /// <summary>
        /// Every model's position and damage, AND every objective's position and owning slot, in store
        /// (creation) order — stable across runs, unlike the PlayerID GUIDs. Rounded, because float noise
        /// is not what this test is about.
        /// <para>
        /// The objectives matter: the solo-rules bot ignores them entirely, so nondeterministic objective
        /// PLACEMENT never moves a single model. A model-only fingerprint is blind to it — verified by
        /// mutating the placer to ignore its seed and watching the fresh-game test stay green.
        /// </para>
        /// </summary>
        private static string FingerprintFinalState(IReadableGameDataStore store)
        {
            Dictionary<PlayerID, int> slotByPlayer = store.GetAllValues<PlayerSlotInfo>()
                .ToDictionary(info => info.PlayerID, info => info.SlotID);

            var sb = new StringBuilder();
            foreach (ModelData model in store.GetAllValues<ModelData>())
            {
                sb.Append(model.Position.x.ToString("F3")).Append(',')
                  .Append(model.Position.z.ToString("F3")).Append(',')
                  .Append(model.WoundsDealt.ToString("F3")).Append(';');
            }

            sb.Append('|');
            foreach (ObjectiveData objective in store.GetAllValues<ObjectiveData>())
            {
                int ownerSlot = objective.OwnerID.HasValue && slotByPlayer.TryGetValue(objective.OwnerID.Value, out int slot)
                    ? slot : -1;
                sb.Append(objective.Position.x.ToString("F3")).Append(',')
                  .Append(objective.Position.z.ToString("F3")).Append(',')
                  .Append(ownerSlot).Append(';');
            }
            return sb.ToString();
        }

        private static ScenarioFile MakeScenario(int seed) => new ScenarioFile
        {
            Name = "Determinism scenario",
            Round = 2,
            ActivePlayer = 1,
            Settings = new ScenarioSettings { Randomness = "Realistic", DiceSeed = seed },
            Players = new()
            {
                new ScenarioPlayer
                {
                    Army = "shooters.fdgarmy",
                    Units = new()
                    {
                        new ScenarioUnit
                        {
                            Unit = "Warriors",
                            Models = new() { new[] { 30f, 20f }, new[] { 31f, 20f }, new[] { 32f, 20f } },
                        },
                    },
                },
                new ScenarioPlayer
                {
                    Army = "defenders.fdgarmy",
                    Units = new()
                    {
                        new ScenarioUnit
                        {
                            Unit = "Guards",
                            Models = new() { new[] { 40f, 26f }, new[] { 41f, 26f }, new[] { 42f, 26f } },
                        },
                    },
                },
            },
        };

        private static ArmyListFile MakeShooterArmy() => new ArmyListFile
        {
            Name = "Shooters",
            Units = new()
            {
                new UnitFileEntry
                {
                    Name = "Warriors", ModelCount = 3, Quality = 4, Defense = 4,
                    Weapons = new() { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                },
            },
        };

        private static ArmyListFile MakeDefenderArmy() => new ArmyListFile
        {
            Name = "Defenders",
            Units = new()
            {
                new UnitFileEntry
                {
                    Name = "Guards", ModelCount = 3, Quality = 4, Defense = 4,
                    Weapons = new() { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                },
            },
        };
    }
}
