using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #265 — what a resume lobby is allowed to change about a saved game.
    //
    // The resume path launches from the SAVE's settings, so a host who re-picked something in the resume
    // lobby was ignored. That is right for everything already spent (points, terrain, objectives) or that
    // would change a game in progress (randomness, dice seed, turn style, cover house rules) - and wrong
    // for the purely cosmetic table background. GameSettings.WithResumeOverridesFrom is the one place
    // that policy lives; these tests pin both halves of it, and that the pick reaches the save record
    // immediately rather than waiting for the first rolling save point.
    [TestFixture]
    public class ResumeSettingsOverrideTests
    {
        [Test]
        public void WithResumeOverridesFrom_TakesTheBackgroundAndNothingElse()
        {
            GameSettings saved = new GameSettings
            {
                ArmyPoints = 1500,
                TerrainPieceCount = 12,
                TerrainPointsTotal = 24,
                TerrainPointsPerTurn = 4,
                TerrainPlacementMode = ETerrainPlacementMode.LoadFromFile,
                TerrainLayoutPath = "layouts/ruins.json",
                ObjectivePlacementMode = EObjectivePlacementMode.PlayerPlaced,
                RandomnessType = ERandomnessType.Realistic,
                DiceSeed = 5150,
                TurnStyle = ETurnStyle.BoltAction,
                CoverProximityExceptions = false,
                TableBackground = ETableBackground.Forest,
            };

            // A lobby that disagrees about literally everything.
            GameSettings lobby = new GameSettings
            {
                ArmyPoints = 3000,
                TerrainPieceCount = 20,
                TerrainPointsTotal = 30,
                TerrainPointsPerTurn = 3,
                TerrainPlacementMode = ETerrainPlacementMode.Alternating,
                TerrainLayoutPath = "layouts/other.json",
                ObjectivePlacementMode = EObjectivePlacementMode.AutoPlaced,
                RandomnessType = ERandomnessType.Probabilistic,
                DiceSeed = 99,
                TurnStyle = ETurnStyle.Standard,
                CoverProximityExceptions = true,
                TableBackground = ETableBackground.Urban,
            };

            GameSettings merged = saved.WithResumeOverridesFrom(lobby);

            Assert.That(merged.TableBackground, Is.EqualTo(ETableBackground.Urban), "the cosmetic pick applies");

            Assert.That(merged.ArmyPoints, Is.EqualTo(1500));
            Assert.That(merged.TerrainPieceCount, Is.EqualTo(12));
            Assert.That(merged.TerrainPointsTotal, Is.EqualTo(24));
            Assert.That(merged.TerrainPointsPerTurn, Is.EqualTo(4));
            Assert.That(merged.TerrainPlacementMode, Is.EqualTo(ETerrainPlacementMode.LoadFromFile));
            Assert.That(merged.TerrainLayoutPath, Is.EqualTo("layouts/ruins.json"));
            Assert.That(merged.ObjectivePlacementMode, Is.EqualTo(EObjectivePlacementMode.PlayerPlaced));
            Assert.That(merged.RandomnessType, Is.EqualTo(ERandomnessType.Realistic));
            Assert.That(merged.DiceSeed, Is.EqualTo(5150));
            Assert.That(merged.TurnStyle, Is.EqualTo(ETurnStyle.BoltAction));
            Assert.That(merged.CoverProximityExceptionsEnabled, Is.False);
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task ResumeWithLobbySettings_PersistsTheBackgroundPickFromTheFirstMoment()
        {
            // The save was put down on Forest; the resume lobby re-picks Urban and disagrees about the
            // rules settings too.
            GameSettings lobby = GameSettings.GetDefault();
            lobby.TableBackground = ETableBackground.Urban;
            lobby.TurnStyle = ETurnStyle.BoltAction;
            lobby.RandomnessType = ERandomnessType.Probabilistic;
            lobby.CoverProximityExceptions = false;
            lobby.ArmyPoints = 9999;

            (GameSettings atLaunch, GameSettings atEnd, GameResult result) = await ResumeAndPlay(lobby);

            // Read the moment the resume constructor returns: a player who saves as soon as the game comes
            // up must get the board they picked. (The constructor's own write-back is what guarantees this
            // when no activation boundary has been reached yet; this assertion can't tell that write from
            // the first rolling save point, since both write the same merged settings.)
            Assert.That(atLaunch.TableBackground, Is.EqualTo(ETableBackground.Urban),
                "the pick must be in the progress record from the moment the resumed game comes up");
            Assert.That(atEnd.TableBackground, Is.EqualTo(ETableBackground.Urban),
                "and the rolling save point must keep writing it, not revert to the saved value");

            // The saved game's rules are untouched by the lobby, at launch and after playing.
            foreach (GameSettings settings in new[] { atLaunch, atEnd })
            {
                Assert.That(settings.TurnStyle, Is.EqualTo(ETurnStyle.Standard), "turn style stays as saved");
                Assert.That(settings.RandomnessType, Is.EqualTo(ERandomnessType.Realistic), "randomness stays as saved");
                Assert.That(settings.DiceSeed, Is.EqualTo(5150), "the saved dice seed survives");
                Assert.That(settings.CoverProximityExceptionsEnabled, Is.True, "cover house rules stay as saved");
                Assert.That(settings.ArmyPoints, Is.EqualTo(GameSettings.GetDefault().ArmyPoints),
                    "the lobby's points limit cannot rewrite a game in progress");
            }

            Assert.That(result.Outcome, Is.Not.EqualTo(EGameOutcome.Fault), result.Message);
        }

        [Test]
        [CancelAfter(120_000)]
        public async Task ResumeWithoutLobbySettings_LeavesTheSaveVerbatim()
        {
            (GameSettings atLaunch, GameSettings atEnd, GameResult result) = await ResumeAndPlay(lobbySettings: null);

            // The headless resume and the --scenario launch take this path.
            Assert.That(atLaunch.TableBackground, Is.EqualTo(ETableBackground.Ice));
            Assert.That(atEnd.TableBackground, Is.EqualTo(ETableBackground.Ice));
            Assert.That(atLaunch.TurnStyle, Is.EqualTo(ETurnStyle.Standard));
            Assert.That(result.Outcome, Is.Not.EqualTo(EGameOutcome.Fault), result.Message);
        }

        // #270 — the reported shape: resume a save, save again, and reopen it. Every resume path re-crews
        // the slots (destroy + recreate PlayerSlotInfo, which advances that slot's generation), so before
        // #270 the second file could not be opened at all: "Store replay stalled ... FutureGeneration".
        [Test]
        public void SaveTakenFromAResumedGame_LoadsAgain()
        {
            GameDataStore store = ScenarioCompiler.Compile(MakeScenario(),
                new[] { MakeArmy("Reds"), MakeArmy("Blues") });
            store = GameSaveSerializer.Load(GameSaveSerializer.Save(store));

            // Exactly what LobbyViewModel_Host.LaunchResume and ScenarioLauncher do before resuming.
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

            GameSettings lobby = GameSettings.GetDefault();
            lobby.TableBackground = ETableBackground.Barren;
            _ = new FDGServer(store, bus, slots, presentationClock: null, lobbySettings: lobby);

            GameDataStore reopened = null!;
            Assert.DoesNotThrow(() => reopened = GameSaveSerializer.Load(GameSaveSerializer.Save(store)),
                "a save taken from a resumed game must reopen");

            // And it is the resumed game, not a wreck: the slots and the re-picked background survive.
            Assert.That(reopened.GetAllValues<PlayerSlotInfo>().Count(), Is.EqualTo(savedInfos.Count),
                "one slot per player, no duplicates from the re-crew");
            Assert.That(GameProgressUtilities.TryGetProgress(reopened)!.Settings.TableBackground,
                Is.EqualTo(ETableBackground.Barren));
        }

        // Compiles a round-1 save (background Ice, Realistic dice seeded 5150), resumes it with the given
        // lobby settings, and reports the progress record's settings both immediately after the resume
        // constructor returns and once the game has played out. Mirrors ResumeRoundCountTests.ResumeAndPlay.
        private static async Task<(GameSettings AtLaunch, GameSettings AtEnd, GameResult Result)> ResumeAndPlay(
            GameSettings? lobbySettings)
        {
            GameDataStore store = ScenarioCompiler.Compile(MakeScenario(),
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
            var server = new FDGServer(store, bus, slots, presentationClock: null, lobbySettings: lobbySettings);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            GameSettings atLaunch = GameProgressUtilities.TryGetProgress(store)!.Settings;

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(90)));
            Assert.That(finished, Is.SameAs(completed.Task), "the resumed game must play to completion without hanging.");

            GameSettings atEnd = GameProgressUtilities.TryGetProgress(store)!.Settings;
            return (atLaunch, atEnd, await completed.Task);
        }

        private static ScenarioFile MakeScenario() => new ScenarioFile
        {
            Name = "Resume settings override",
            Round = 1,
            ActivePlayer = 0,
            Settings = new ScenarioSettings
            {
                Randomness = "Realistic",
                DiceSeed = 5150,
                Background = "Ice",
            },
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
