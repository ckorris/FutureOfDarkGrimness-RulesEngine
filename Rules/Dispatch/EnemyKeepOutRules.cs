using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// #426 (Core Rules v3.6.0): the enemy clause a deployment-time placement carries when it exempts SOME
    /// enemies - Scout's "over 9" away from non-Scout enemy units" and Vanguard's (and Fanatic's, Drakesworn's)
    /// "over 9" away from non-Vanguard enemy units". The flat
    /// <see cref="PlaceObjectsRequest{T}.MinDistanceFromEnemiesInches"/> cannot say "except these", so the
    /// clause travels as keep-out discs snapshotted at request build (nothing on the table moves while the
    /// placement is being resolved), judged by every resolver through
    /// <see cref="StageResolution.PlacementDistanceRules"/> exactly like Repel Ambushers' discs.
    ///
    /// <para>Distances are centre-to-centre, the convention every placement keep-out already uses
    /// (<see cref="PlacementDisc"/>, Ambush's "over 9" from enemies"), so Scout and Ambush measure the same
    /// way. Discs radiate from every LIVING model of each enemy unit that is on the battlefield; units in
    /// reserve, inside a transport or off the table project nothing.</para>
    /// </summary>
    public static class EnemyKeepOutRules
    {
        /// <summary>
        /// One disc of <paramref name="clearanceInches"/> per living model of every on-table enemy unit of
        /// <paramref name="placing"/> (side-aware: a teammate is not an enemy) that
        /// <paramref name="isExempt"/> does not excuse. Empty when the clearance is not positive.
        /// </summary>
        public static IReadOnlyList<PlacementDisc> Discs(IUnit placing, ITableState tableState,
            float clearanceInches, Func<IUnit, bool> isExempt)
        {
            if (clearanceInches <= 0f) return Array.Empty<PlacementDisc>();

            List<PlacementDisc>? discs = null;
            foreach (IUnit unit in tableState.Units.Objects)
            {
                if (ReferenceEquals(unit, placing)) continue;
                if (!unit.GetIsAlive() || !unit.GetIsOnBattlefield()) continue;
                if (ITeamExtensions.AreAllied(tableState.Teams.Objects, placing.PlayerID, unit.PlayerID)) continue;
                if (isExempt(unit)) continue;

                foreach (IModel model in unit.Models)
                {
                    if (!model.GetIsAlive()) continue;
                    Position pos = model.Position;
                    // Default-constructed Position is the unplaced sentinel; never radiate from it.
                    if (pos.x == 0f && pos.z == 0f) continue;
                    (discs ??= new List<PlacementDisc>()).Add(new PlacementDisc(pos, clearanceInches));
                }
            }

            return (IReadOnlyList<PlacementDisc>?)discs ?? Array.Empty<PlacementDisc>();
        }

        /// <summary>
        /// Whether <paramref name="unit"/> carries a rule named <paramref name="ruleName"/> - on the unit, on
        /// any of its models (a joined hero's relocated rules), or as an aura / "gains rule X" grant. Matches
        /// the canonical name or the name the book asked for, case-insensitively (the pair the rule resolver
        /// compares). The "non-Vanguard enemy units" test: an enemy unit with Vanguard does not push back.
        /// </summary>
        public static bool CarriesRuleNamed(IUnit unit, string ruleName)
        {
            bool Matches(IEnumerable<ResolvedRule> rules) => rules.Any(r =>
                string.Equals(r.Definition.Name, ruleName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.RequestedName, ruleName, StringComparison.OrdinalIgnoreCase));

            if (Matches(unit.RuleDefinitions)) return true;
            if (unit.Models.Any(model => Matches(model.RuleDefinitions))) return true;

            foreach (Token token in unit.Tokens.GetAllTokens(TokenType.RuleGrant))
            {
                if (token.Payload is TokenPayload.RuleGrant grant
                    && string.Equals(grant.RuleName, ruleName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
