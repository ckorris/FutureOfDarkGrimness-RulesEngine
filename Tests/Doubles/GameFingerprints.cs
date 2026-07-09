using System.Text;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>A game's observable outcome: the structured result (#192) plus the final board.</summary>
    internal readonly record struct GameFingerprint(string Summary, string FinalState);

    /// <summary>
    /// Whole-game fingerprinting shared by the determinism-style fixtures (DeterminismTests,
    /// TacticianScaffoldTests): two games are "the same game" iff their fingerprints match.
    /// </summary>
    internal static class GameFingerprints
    {
        /// <summary>
        /// Guards against the fixtures' central failure mode: two games that FAULTED identically satisfy
        /// every equality assertion here and "prove" determinism while proving nothing. Every game here —
        /// fresh or resumed — must complete the full four rounds (#195 made that true of resumes too).
        /// </summary>
        public static void AssertReallyPlayed(GameResult result)
        {
            Assert.That(result.Outcome, Is.Not.EqualTo(EGameOutcome.Fault),
                $"the game faulted instead of playing: {result.Message}");
            Assert.That(result.RoundsPlayed, Is.EqualTo(GameWideConstants.NUMBER_OF_ROUNDS),
                "the game did not play the full four rounds.");
        }

        /// <summary>
        /// Every model's position and damage, AND every objective's position and owning slot, in store
        /// (creation) order — stable across runs, unlike the PlayerID GUIDs. Rounded, because float noise
        /// is not what these tests are about.
        /// <para>
        /// The objectives matter: the solo-rules bot ignores them entirely, so nondeterministic objective
        /// PLACEMENT never moves a single model. A model-only fingerprint is blind to it — verified by
        /// mutating the placer to ignore its seed and watching the fresh-game test stay green.
        /// </para>
        /// </summary>
        public static string FingerprintFinalState(IReadableGameDataStore store)
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
    }
}
