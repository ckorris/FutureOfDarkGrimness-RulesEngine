using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// Derives a unit's CAPABILITIES from its #042 rules. Mirrors <see cref="RangeRuleQueries"/> /
    /// <see cref="SightRuleQueries"/> / <see cref="MovementRuleQueries"/>: a single non-logging read of the
    /// rule dispatch, shared by every stage that needs the answer, so they cannot drift apart.
    ///
    /// <para>Capabilities are asked for, not tested for. The engine used to decide "is this a caster / a
    /// transport / re-deployable?" by comparing against the core definition — an identity check standing in
    /// for a capability. That silently excludes any OTHER rule conferring the same thing (<c>Caster
    /// Group</c> is not <c>Caster</c>, and could never be granted as one: its X is a live model count and
    /// granted rules carry no arguments), and it cannot express a capability that depends on live state.
    /// Asking the rule graph fixes both: a rule opts in by authoring the matching <c>Effect.Enable*</c> at
    /// <see cref="EHookID.Lifecycle_OnCapabilityQuery"/>, its <see cref="Condition"/> gates the answer, and
    /// suppression applies as it does anywhere else.</para>
    ///
    /// <para>Every query names the unit's MODELS as participants alongside the unit (#093), so a rule the
    /// #006 hero-merge relocated onto a joined hero's model still counts for the host unit — the same
    /// accommodation <c>StartOfRoundExtraActionStage.GrantRoundStartTokens</c> makes when funding a spell pool.
    /// All are non-logging and side-effect-free: safe to call per-frame while building UI, and safe to call
    /// repeatedly while scanning every unit on the table.</para>
    /// </summary>
    public static class CapabilityRuleQueries
    {
        /// <summary>
        /// Whether <paramref name="unit"/> can cast spells (core <c>Caster(X)</c>, <c>Caster Group</c>).
        /// Gates the Cast action in <c>ChooseActionStage</c> and finds the Casters that may sway someone
        /// else's cast in <c>CastSpellStage</c> (#103).
        /// </summary>
        public static bool CanCast(IUnit unit, RuleEvaluator evaluator) =>
            Answers<RuleOperation.EnableCasting>(unit, evaluator);

        /// <summary>
        /// Whether <paramref name="unit"/> can carry friendly units (core <c>Transport(X)</c>).
        /// </summary>
        public static bool CanTransport(IUnit unit, RuleEvaluator evaluator) =>
            Answers<RuleOperation.EnableTransport>(unit, evaluator);

        /// <summary>
        /// How many spaces <paramref name="unit"/> can carry — 0 for a unit that is not a transport, so
        /// this doubles as the capability test where a caller wants both. Several conferring rules take the
        /// LARGEST capacity rather than the sum: two transport rules on one unit describe the same hold
        /// twice, they do not add a second hold. (No corpus unit carries two; stated so the behaviour is a
        /// choice rather than an accident.)
        /// </summary>
        public static int TransportCapacity(IUnit unit, RuleEvaluator evaluator)
        {
            int capacity = 0;
            foreach (RuleOperation op in Ask(unit, evaluator))
            {
                if (op is RuleOperation.EnableTransport transport && transport.Capacity > capacity)
                {
                    capacity = transport.Capacity;
                }
            }

            return capacity;
        }

        /// <summary>
        /// Whether <paramref name="unit"/> may be re-deployed in the post-deployment pass
        /// (core <c>Re-Deployment</c>).
        /// </summary>
        public static bool CanReDeploy(IUnit unit, RuleEvaluator evaluator) =>
            Answers<RuleOperation.EnableReDeployment>(unit, evaluator);

        /// <summary>
        /// The pools <paramref name="unit"/> is currently lending to other friendly casters, each with the
        /// range it reaches (core <c>Spell Accumulator(X)</c>). Empty for almost every unit. Read by
        /// <c>SpellPurse</c>, which is the only thing that knows what a borrower may then do with them.
        ///
        /// <para>A list rather than a single answer because two lending rules on one unit are two distinct
        /// offers - unlike two <c>Transport</c> rules, which describe the same hold twice.</para>
        /// </summary>
        public static IReadOnlyList<RuleOperation.EnableSpellLending> LentPools(IUnit unit,
            RuleEvaluator evaluator) => Collect<RuleOperation.EnableSpellLending>(unit, evaluator);

        /// <summary>
        /// The relays <paramref name="unit"/> is currently offering to other friendly casters - each with
        /// the range it reaches and the cast-roll bonus it confers (core <c>Spell Conduit</c>). Empty for
        /// almost every unit. Read by <c>SpellRelay</c>.
        /// </summary>
        public static IReadOnlyList<RuleOperation.EnableSpellRelay> RelayOffers(IUnit unit,
            RuleEvaluator evaluator) => Collect<RuleOperation.EnableSpellRelay>(unit, evaluator);

        private static IReadOnlyList<TOperation> Collect<TOperation>(IUnit unit, RuleEvaluator evaluator)
            where TOperation : RuleOperation
        {
            List<TOperation>? found = null;
            foreach (RuleOperation op in Ask(unit, evaluator))
            {
                if (op is TOperation match)
                {
                    (found ??= new List<TOperation>()).Add(match);
                }
            }

            return (IReadOnlyList<TOperation>?)found ?? System.Array.Empty<TOperation>();
        }

        private static bool Answers<TOperation>(IUnit unit, RuleEvaluator evaluator)
            where TOperation : RuleOperation
        {
            foreach (RuleOperation op in Ask(unit, evaluator))
            {
                if (op is TOperation)
                {
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<RuleOperation> Ask(IUnit unit, RuleEvaluator evaluator) =>
            evaluator.Evaluate(unit, ERuleSeat.Actor, new CapabilityQueryContext(unit),
                weapon: null, models: unit.Models);
    }
}
