using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #195 — a resumed game must FINISH the four-round game, not play four more rounds.
    //
    // ReconcileObjectivesStage used to decide "is the game over?" from `_timesEntered`, a counter on the
    // stage instance. A resume builds a fresh instance, so the counter restarted at zero: a save resumed
    // at round 2 played rounds 2..5, reconciled objectives an extra time, and reported RoundsPlayed = 5.
    // The round number is the authoritative signal, and it survives the save.
    [TestFixture]
    public class ResumeRoundCountTests
    {
        [Test]
        [CancelAfter(120_000)]
        public async Task ResumedAtRoundTwo_FinishesTheFourRoundGame()
        {
            GameResult result = await ResumeAndPlay(fromRound: 2);

            Assert.That(result.Outcome, Is.Not.EqualTo(EGameOutcome.Fault), result.Message);
            Assert.That(result.RoundsPlayed, Is.EqualTo(GameWideConstants.NUMBER_OF_ROUNDS),
                "a game resumed at round 2 must end after round 4, not play four more rounds.");
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task ResumedAtTheFinalRound_PlaysExactlyThatRound()
        {
            GameResult result = await ResumeAndPlay(fromRound: GameWideConstants.NUMBER_OF_ROUNDS);

            Assert.That(result.Outcome, Is.Not.EqualTo(EGameOutcome.Fault), result.Message);
            Assert.That(result.RoundsPlayed, Is.EqualTo(GameWideConstants.NUMBER_OF_ROUNDS),
                "resuming at the last round must play that round and stop, not start four more.");
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task ResumedAtRoundOne_MatchesAFreshGamesRoundCount()
        {
            GameResult result = await ResumeAndPlay(fromRound: 1);

            Assert.That(result.RoundsPlayed, Is.EqualTo(GameWideConstants.NUMBER_OF_ROUNDS),
                "the round-1 resume path (the only one the shipped example scenario covered) must be unchanged.");
        }

        private static async Task<GameResult> ResumeAndPlay(int fromRound)
        {
            GameDataStore store = ScenarioCompiler.Compile(MakeScenario(fromRound),
                new[] { MakeArmy("Reds"), MakeArmy("Blues") });

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
                    $"AI {i}", savedInfos[i].PlayerID, aiGame, seed: 5150, slotID: slots[i].SlotID));
            }

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(90)));
            Assert.That(finished, Is.SameAs(completed.Task), "the resumed game must play to completion without hanging.");
            return await completed.Task;
        }

        private static ScenarioFile MakeScenario(int round) => new ScenarioFile
        {
            Name = $"Resume at round {round}",
            Round = round,
            ActivePlayer = 0,
            Settings = new ScenarioSettings { Randomness = "Realistic", DiceSeed = 5150 },
            Players = new()
            {
                new ScenarioPlayer
                {
                    Army = "reds.fdgarmy",
                    Units = new()
                    {
                        new ScenarioUnit
                        {
                            Unit = "Troops",
                            Models = new() { new[] { 20f, 20f }, new[] { 21f, 20f }, new[] { 22f, 20f } },
                        },
                    },
                },
                new ScenarioPlayer
                {
                    Army = "blues.fdgarmy",
                    Units = new()
                    {
                        new ScenarioUnit
                        {
                            Unit = "Troops",
                            Models = new() { new[] { 40f, 26f }, new[] { 41f, 26f }, new[] { 42f, 26f } },
                        },
                    },
                },
            },
        };

        private static ArmyListFile MakeArmy(string name) => new ArmyListFile
        {
            Name = name,
            Units = new()
            {
                new UnitFileEntry
                {
                    Name = "Troops", ModelCount = 3, Quality = 4, Defense = 4,
                    Weapons = new() { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                },
            },
        };
    }
}
