using System;
using System.Linq;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// #032 Limited — a weapon that "may only be used once per game." Limited is a weapon-scoped MARKER rule
    /// (no hooks); this helper supplies the name-based detector plus the spent-state query/mutator the shooting
    /// flow uses (the <see cref="AircraftRules"/>/<see cref="TransportUtilities"/> shape — gates that run in
    /// stage code with no <see cref="RuleEvaluator"/> in scope).
    ///
    /// The spent-state is a per-MODEL <see cref="TokenType.LimitedSpent"/> token (weapons have no token
    /// container; models do), keyed by weapon name via a <see cref="TokenPayload.WeaponName"/> payload. Binding
    /// to the model — rather than the unit — keeps it correct under casualties and ready for <c>Limited(X&gt;1)</c>
    /// (each model tracks its own uses). Same-name weapons in a unit fire together (a game rule), so in practice
    /// a unit's copies all spend in one firing; the model-level data captures that without special-casing.
    /// </summary>
    public static class LimitedRules
    {
        /// <summary>True if <paramref name="weapon"/> carries the Limited rule (case-insensitive, name-based —
        /// so an army's own embedded "Limited" rule is recognised too, like the rule resolver).</summary>
        public static bool IsLimited(IWeapon weapon) => LimitedRuleName(weapon) != null;

        /// <summary>
        /// #315: the DISPLAY name of the Limited rule on <paramref name="weapon"/> (its requested name, so an
        /// alias or an argument form like <c>Limited(2)</c> reads as the army wrote it), or null when the
        /// weapon is not Limited. The <c>CoverIgnoreSource</c> shape: a once-per-game weapon is worth naming in
        /// the UI *before* it is fired, and the resolvers cannot ask the rule list themselves — weapon
        /// <see cref="IWeapon.RuleDefinitions"/> is <c>[JsonIgnore]</c>, so a request that crossed the network
        /// arrives with an empty list until it is rehydrated.
        /// </summary>
        public static string? LimitedRuleName(IWeapon weapon)
        {
            foreach (ResolvedRule rule in weapon.RuleDefinitions)
            {
                if (string.Equals(rule.Definition.Name, "Limited", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rule.RequestedName, "Limited", StringComparison.OrdinalIgnoreCase))
                {
                    return rule.RequestedName;
                }
            }
            return null;
        }

        /// <summary>
        /// True if <paramref name="weapon"/> is Limited and every living model in <paramref name="unit"/> that
        /// carries it has already fired it this game — so it can no longer be offered. False for non-Limited
        /// weapons, and while any carrier still has a shot left.
        /// </summary>
        public static bool IsSpent(IUnit unit, IWeapon weapon)
        {
            if (!IsLimited(weapon)) return false;

            bool anyCarrier = false;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                if (!CarriesWeapon(model, weapon.Name)) continue;
                anyCarrier = true;
                if (!HasSpentToken(model, weapon.Name)) return false; // this carrier can still fire
            }
            return anyCarrier;
        }

        /// <summary>
        /// Records that <paramref name="weapon"/> has been fired this game on every living model in
        /// <paramref name="unit"/> that carries it (idempotent per model). No-op for non-Limited weapons. Called
        /// once the weapon is committed to fire — same-name weapons fire together, so all carriers are spent.
        /// </summary>
        public static void MarkFired(IUnit unit, IWeapon weapon)
        {
            if (!IsLimited(weapon)) return;

            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                if (!CarriesWeapon(model, weapon.Name)) continue;
                if (HasSpentToken(model, weapon.Name)) continue;

                model.Tokens.AddToken(new Token(TokenType.LimitedSpent, 1,
                    new TokenClearTrigger.ManualOnly(), new TokenPayload.WeaponName(weapon.Name)));
            }
        }

        private static bool CarriesWeapon(IModel model, string weaponName) =>
            model.Weapons.Any(w => w.Name == weaponName);

        private static bool HasSpentToken(IModel model, string weaponName) =>
            model.Tokens.GetAllTokens(TokenType.LimitedSpent)
                .Any(token => token.Payload is TokenPayload.WeaponName named && named.Name == weaponName);
    }
}
