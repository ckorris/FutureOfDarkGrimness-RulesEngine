using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #167 T1: the scenario compiler turns a compact ScenarioFile into a resumable .fdgsave. These
    // tests pin (a) the compiled store's contents through a real GameSaveSerializer round-trip —
    // progress cursor, placements, wounds-after-Tough, tokens, objectives, rehydrated rules — and
    // (b) the whole point of the tool: a compiled save RESUMES through the real FDGServer resume
    // constructor and plays to completion with AI on every slot.
    [TestFixture]
    public class ScenarioCompilerTests
    {
        private static ArmyListFile MakeShooterArmy() => new ArmyListFile
        {
            Name = "Shooters",
            Units = new()
            {
                new UnitFileEntry
                {
                    Name = "Warriors", ModelCount = 3, Quality = 4, Defense = 4,
                    Weapons = new()
                    {
                        new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 },
                    },
                },
                new UnitFileEntry
                {
                    Name = "Brute", ModelCount = 1, Quality = 4, Defense = 4,
                    SpecialRules = new() { new SpecialRuleEntry_CoreNumeric("Tough", 3) },
                    Weapons = new()
                    {
                        new WeaponFileEntry { Name = "Claws", RangeInches = 0, Attacks = 2 },
                    },
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
                    Weapons = new()
                    {
                        new WeaponFileEntry { Name = "Pistol", RangeInches = 12, Attacks = 1 },
                    },
                },
            },
        };

        private static ScenarioFile MakeScenario() => new ScenarioFile
        {
            Name = "Test scenario",
            Round = 2,
            ActivePlayer = 1,
            Settings = new ScenarioSettings { Randomness = "Probabilistic" },
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
                        new ScenarioUnit
                        {
                            Unit = "Brute",
                            Models = new() { new[] { 40f, 20f } },
                            WoundsDealt = new() { 2f },
                            Activated = true,
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
                            Tokens = new() { new ScenarioToken { Type = "shaken", Count = 1 } },
                        },
                    },
                },
            },
        };

        private static GameDataStore CompileAndRoundTrip(ScenarioFile scenario)
        {
            GameDataStore compiled = ScenarioCompiler.Compile(scenario,
                new[] { MakeShooterArmy(), MakeDefenderArmy() });
            // Everything is asserted through a save/load round-trip: the deliverable is the .fdgsave,
            // not the in-memory store.
            return GameSaveSerializer.Load(GameSaveSerializer.Save(compiled));
        }

        private static UnitData UnitByName(GameDataStore store, string name)
            => store.GetAllValues<UnitData>().Single(u => u.Name == name);

        [Test]
        public void Compile_RoundTrip_RestoresProgressCursorAndSettings()
        {
            GameDataStore store = CompileAndRoundTrip(MakeScenario());

            GameProgressData? progress = GameProgressUtilities.TryGetProgress(store);
            Assert.That(progress, Is.Not.Null, "Compiled save must carry a GameProgressData.");

            Assert.That(progress!.Stage, Is.EqualTo(EResumeStage.MainPhase));
            Assert.That(progress.RoundCount, Is.EqualTo(2));
            Assert.That(progress.Settings.RandomnessType, Is.EqualTo(ERandomnessType.Probabilistic));

            // ActivePlayer = 1 (team 1): active team first in the order, cursor parked at the end so
            // the resume's first TryAdvance (which starts at index + 1, wrapping) lands on it.
            Assert.That(progress.TeamActivateOrder, Is.EqualTo(new List<int> { 1, 0 }));
            Assert.That(progress.CurrentTeamIndex, Is.EqualTo(1));

            // The Brute was pre-activated: Warriors + Guards remain.
            Assert.That(progress.UnactivatedUnits, Has.Count.EqualTo(2));
            List<string> unactivatedNames = progress.UnactivatedUnits
                .Select(u => u.GetValue().Name).OrderBy(n => n).ToList();
            Assert.That(unactivatedNames, Is.EqualTo(new List<string> { "Guards", "Warriors" }));
        }

        [Test]
        public void Compile_RoundTrip_AppliesPlacementsWoundsAndTokens()
        {
            GameDataStore store = CompileAndRoundTrip(MakeScenario());

            UnitData warriors = UnitByName(store, "Warriors");
            Assert.That(warriors.ModelBindings[0].GetValue().Position.x, Is.EqualTo(30f).Within(0.001f));
            Assert.That(warriors.ModelBindings[2].GetValue().Position.x, Is.EqualTo(32f).Within(0.001f));
            Assert.That(warriors.ModelBindings[1].GetValue().Position.z, Is.EqualTo(20f).Within(0.001f));

            // Wounds landed AFTER Tough(3) set max wounds: 3 - 2 = 1 remaining.
            ModelData brute = UnitByName(store, "Brute").ModelBindings[0].GetValue();
            Assert.That(brute.TotalWounds, Is.EqualTo(3f), "Tough(3) creation rule must run at compile.");
            Assert.That(brute.WoundsDealt, Is.EqualTo(2f), "Pre-applied wounds must survive the round-trip.");

            // 'shaken' (lowercase in the scenario) normalized to the canonical engine ID.
            UnitData guards = UnitByName(store, "Guards");
            Assert.That(guards.Tokens.GetTokenCount(TokenType.Shaken), Is.EqualTo(1));
        }

        [Test]
        public void Compile_RoundTrip_AutoPlacesUnlistedUnitsInTheirBand()
        {
            GameDataStore store = CompileAndRoundTrip(MakeScenario());

            // Guards had no placements: rowed in team 1's (far) deployment band, on the table.
            UnitData guards = UnitByName(store, "Guards");
            foreach (DataBinding<ModelData> modelBinding in guards.ModelBindings)
            {
                Position pos = modelBinding.GetValue().Position;
                Assert.That(pos.x != 0f || pos.z != 0f, "Auto-placed models must be on the table.");
                Assert.That(pos.z, Is.EqualTo(39f).Within(0.001f),
                    "Team 1 rows at tableH - deploymentDistance = 48 - 9.");
            }

            // Default objectives: three across the midline.
            Assert.That(store.GetAllValues<ObjectiveData>().Count(), Is.EqualTo(3));
        }

        [Test]
        public void Compile_RoundTrip_RehydratesUnitRules()
        {
            GameDataStore store = CompileAndRoundTrip(MakeScenario());

            // Tough attached from the army list, plus the universal Disembark/Embark pair.
            UnitData brute = UnitByName(store, "Brute");
            Assert.That(brute.RuleDefinitions.Select(r => r.RequestedName),
                Does.Contain("Tough(3)"), "Army-list rules must survive into the loaded save.");
        }

        [Test]
        public void Compile_InvalidScenarios_FailWithClearMessages()
        {
            ArmyListFile[] armies = { MakeShooterArmy(), MakeDefenderArmy() };

            ScenarioFile badActive = MakeScenario();
            badActive.ActivePlayer = 5;
            Assert.Throws<ScenarioCompileException>(() => ScenarioCompiler.Compile(badActive, armies));

            ScenarioFile badUnit = MakeScenario();
            badUnit.Players[0].Units[0].Unit = "Nonexistent";
            Assert.That(Assert.Throws<ScenarioCompileException>(
                    () => ScenarioCompiler.Compile(badUnit, armies))!.Message,
                Does.Contain("Available"), "Unknown unit errors must list the army's units.");

            ScenarioFile badCount = MakeScenario();
            badCount.Players[0].Units[0].Models = new() { new[] { 1f, 1f } }; // Warriors has 3 models.
            Assert.Throws<ScenarioCompileException>(() => ScenarioCompiler.Compile(badCount, armies));

            ScenarioFile badOrigin = MakeScenario();
            badOrigin.Players[0].Units[0].Models![0] = new[] { 0f, 0f };
            Assert.That(Assert.Throws<ScenarioCompileException>(
                    () => ScenarioCompiler.Compile(badOrigin, armies))!.Message,
                Does.Contain("reserved"), "(0,0) means off-table and must be rejected.");
        }

        [Test]
        public void SeededRealisticDice_ProduceIdenticalSequences()
        {
            var first = new RealisticDiceRoller(seed: 42);
            var second = new RealisticDiceRoller(seed: 42);

            for (int i = 0; i < 10; i++)
            {
                IDiceResults rollA = first.Roll(6, 8);
                IDiceResults rollB = second.Roll(6, 8);
                float[] a = Enumerable.Range(1, 6).Select(v => rollA.At(v)).ToArray();
                float[] b = Enumerable.Range(1, 6).Select(v => rollB.At(v)).ToArray();
                Assert.That(a, Is.EqualTo(b), $"Seeded rollers diverged on roll {i}.");
            }
        }

        // The end-to-end proof: a compiled save resumes through the REAL FDGServer resume constructor
        // (the same one the lobby's RESUME uses) and plays to completion with AI crewing every saved
        // slot — exactly what --scenario does headless.
        [Test, Timeout(120000)]
        public async Task CompiledScenario_ResumesAndPlaysToCompletion()
        {
            GameDataStore compiled = ScenarioCompiler.Compile(MakeScenario(),
                new[] { MakeShooterArmy(), MakeDefenderArmy() });
            GameDataStore loaded = GameSaveSerializer.Load(GameSaveSerializer.Save(compiled));

            // Mirror LobbyViewModel_Host.LaunchResume: capture the saved slot infos, drop the old
            // records so the rebuilt slots don't duplicate, rebuild PlayerSlots on the saved IDs.
            List<PlayerSlotInfo> savedInfos = loaded.GetAllValues<PlayerSlotInfo>()
                .OrderBy(info => info.SlotID).ToList();
            Assert.That(savedInfos, Has.Count.EqualTo(2), "Compiled save must carry the slot infos.");
            foreach (DataReference oldInfo in loaded.GetAllDataReferences<PlayerSlotInfo>().ToList())
                loaded.Destroy(oldInfo);

            var bus = new InProcessBus();
            PlayerSlot[] slots = new PlayerSlot[savedInfos.Count];
            for (int i = 0; i < savedInfos.Count; i++)
            {
                slots[i] = new PlayerSlot(i, savedInfos[i].TeamNumber, savedInfos[i].PlayerID,
                    new ArmyListFile(), loaded);
                var aiGame = new FDGGame_AsLocal(loaded, bus);
                slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                    $"AI {i}", savedInfos[i].PlayerID, aiGame));
            }

            var gameEnded = new TaskCompletionSource<string>();
            var server = new FDGServer(loaded, bus, slots);
            server.OnGameEnded += result => gameEnded.TrySetResult(result);

            Task finished = await Task.WhenAny(gameEnded.Task, Task.Delay(TimeSpan.FromSeconds(90)));
            Assert.That(finished, Is.SameAs(gameEnded.Task),
                "The resumed scenario must play to completion (rounds 2..4) without hanging.");
        }

    }
}
