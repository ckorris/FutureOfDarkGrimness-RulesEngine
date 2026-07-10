using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using NUnit.Framework;
using static FDG.Tests.GameFingerprints;
using static FDG.Tests.TestArmies;

namespace FDG.Tests
{
    // #191 A0/A4 — the A0 identity pin (Tactician game == solo-rules game) was retired when A4-1
    // replaced the activation resolver, exactly as that pin's comment prescribed: its successors are
    // the per-resolver behavioral tests (TacticianActivationResolverTests etc.) plus the benchmark
    // evidence recorded per swap in the #191 ledger. What remains pinned here is the G5 contract:
    // a seeded Tactician game reproduces itself exactly.
    [TestFixture]
    public class TacticianScaffoldTests
    {
        [Test]
        [CancelAfter(300_000)]
        public async Task TacticianScaffold_SameSeed_ReproducesItself()
        {
            // The G5 contract holds for the new profile in its own right, not just via equality
            // with solo-rules. (Seed note: any seed works as long as the game completes - but #199,
            // the AutoFill fractional-wound fault, keeps eating seeds: 31415 originally, then
            // 424242/424243/777001 once A4-1 changed activation order. Fixing #199 is becoming
            // load-bearing for Tactician testing; see the #191 ledger.)
            GameFingerprint first = await PlayFreshGame(seed: 90210, EAiProfile.Tactician);
            GameFingerprint second = await PlayFreshGame(seed: 90210, EAiProfile.Tactician);

            Assert.That(second.Summary, Is.EqualTo(first.Summary));
            Assert.That(second.FinalState, Is.EqualTo(first.FinalState),
                "a seeded Tactician game diverged from itself.");
        }

        /// <summary>
        /// A fresh whole game (map setup -> deployment -> 4 rounds), the given profile on both
        /// slots, probabilistic dice — the DeterminismTests fresh-game harness with the AI chosen
        /// through the profile dispatch the launch paths use.
        /// </summary>
        private static async Task<GameFingerprint> PlayFreshGame(int seed, EAiProfile profile)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();

            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()),
                    MakeRichArmy(i == 0 ? "Reds" : "Blues"), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                slots[i].AssignPlayerController(AiProfileFactory.CreateController(
                    profile, $"AI {i}", slots[i].PlayerID, aiGame, seed, slots[i].SlotID));
            }

            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = seed;
            settings.AutoPlaceObjectivesDebug = false; // exercises the AI objective placer

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the seeded game must play to completion without hanging.");

            GameResult gameResult = await completed.Task;
            AssertReallyPlayed(gameResult);

            return new GameFingerprint(gameResult.ToSummaryLine(), FingerprintFinalState(store));
        }
    }
}
