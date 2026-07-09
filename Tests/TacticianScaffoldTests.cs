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
    // #191 A0 — the Tactician scaffold delegates every request to the solo-rules resolvers, so a
    // seeded Tactician game must be THE SAME GAME as the solo-rules game under the same seed. This
    // pin proves the whole selection path (profile -> registry -> controller) is wired without
    // touching behavior.
    //
    // When a Phase A slice replaces a resolver, this pin is EXPECTED to break: replace it then with
    // the slice's own behavioral tests + benchmark evidence (ledger entry) - never by weakening the
    // assertion while claiming the scaffold is still a pure delegate.
    [TestFixture]
    public class TacticianScaffoldTests
    {
        [Test]
        [CancelAfter(300_000)]
        public async Task TacticianScaffold_SameSeed_IsTheSameGameAsSoloRules()
        {
            // Rich armies: the full path (auto-layout thinning, Ambush/Scout/Vanguard, melee
            // reactions, Blast) - identical simple games could hide a profile that only diverges
            // on rule-heavy requests.
            GameFingerprint solo = await PlayFreshGame(seed: 24601, EAiProfile.SoloRules);
            GameFingerprint tactician = await PlayFreshGame(seed: 24601, EAiProfile.Tactician);

            Assert.That(tactician.Summary, Is.EqualTo(solo.Summary),
                "the Tactician scaffold reached a different result than solo-rules under the same seed.");
            Assert.That(tactician.FinalState, Is.EqualTo(solo.FinalState),
                "the Tactician scaffold played a different game than solo-rules under the same seed - " +
                "the A0 delegation is not transparent.");
        }

        [Test]
        [CancelAfter(300_000)]
        public async Task TacticianScaffold_SameSeed_ReproducesItself()
        {
            // The G5 contract holds for the new profile in its own right, not just via equality
            // with solo-rules. (Seed note: 31415 deterministically trips a pre-existing engine
            // fault - AutoFill can't assign a ~0.0555 fractional wound - filed as #199; any seed
            // works here as long as the game actually completes.)
            GameFingerprint first = await PlayFreshGame(seed: 424242, EAiProfile.Tactician);
            GameFingerprint second = await PlayFreshGame(seed: 424242, EAiProfile.Tactician);

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
