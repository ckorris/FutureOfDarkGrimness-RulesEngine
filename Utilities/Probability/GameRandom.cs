using System;

namespace FDG
{
    /// <summary>
    /// Builds the seeded <see cref="Random"/> instances a game uses for NON-dice randomness (#193).
    /// <para>
    /// Determinism rule: same <see cref="GameSettings.DiceSeed"/> + same build => identical game, even
    /// when many games run concurrently in one process. That requires every random draw on the game path
    /// to come from a per-game, seeded source. There is no static mutable RNG anywhere in the engine —
    /// a shared <c>Random</c> would let concurrent games consume each other's numbers, which is both
    /// nondeterministic and a data race.
    /// </para>
    /// <para>
    /// Each consumer gets its own stream via a salt, so the dice roller and (say) the objective shuffler
    /// don't march in lockstep off the same seed. An unseeded game (<c>DiceSeed == null</c>, the default)
    /// keeps today's unpredictable behavior.
    /// </para>
    /// </summary>
    public static class GameRandom
    {
        // Distinct, arbitrary salts. Never reuse one across consumers; never renumber an existing one
        // (a saved seed would stop reproducing its game).
        public const int SALT_GAME_CONTEXT = 0x5F37;
        public const int SALT_AI_PLAYER = 0x1CE7;

        /// <summary>
        /// A fresh <see cref="Random"/> for one consumer of one game. Null <paramref name="seed"/> yields
        /// an unseeded (time-based) instance, preserving pre-#193 behavior.
        /// </summary>
        public static Random Create(int? seed, int salt) =>
            seed.HasValue ? new Random(Mix(seed.Value, salt)) : new Random();

        /// <summary>
        /// Derives a per-player sub-seed, so each AI player draws its own reproducible stream and two AI
        /// players in one game never share a sequence. Null in, null out (unseeded stays unseeded).
        /// <para>
        /// Keyed on the SLOT ID, deliberately, not the <see cref="PlayerID"/>: player GUIDs are minted
        /// fresh with <c>Guid.NewGuid()</c> on every run of a non-resumed game, so seeding off them would
        /// hand each run a different stream and quietly defeat the whole point of the seed. Slot IDs are
        /// stable across runs and across a save/resume.
        /// </para>
        /// </summary>
        public static int? DeriveForSlot(int? seed, int slotID) =>
            seed.HasValue ? Mix(seed.Value, Mix(SALT_AI_PLAYER, slotID)) : null;

        // Cheap, order-dependent integer mix. Not cryptographic — it only needs to decorrelate streams.
        private static int Mix(int seed, int salt) => unchecked(seed * 486187739 + salt);
    }
}
