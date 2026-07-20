using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// A <see cref="SpellDefinition"/> after army-load resolution (#033). For a damage spell
    /// (<see cref="Effect.DealHits"/>) it additionally carries the <see cref="ResolvedRule"/>s that the
    /// effect's <c>WithRules</c> names resolve to — resolved once at load, so the cast stage can attach them
    /// to the synthetic spell weapon without re-resolving per cast. Non-damage spells carry an empty
    /// <see cref="WeaponRules"/>.
    /// <para>A spell has an army-load site, so it pre-resolves here. An ABILITY's <c>WithRules</c> does not
    /// (it may be conferred at runtime by an aura or grant) and resolves at dispatch instead, via
    /// <see cref="RuleEvaluator.RuleResolver"/> — see #164. An earlier note here claimed the resolver was
    /// unreachable from a stage; that stopped being true when #100 slice 1 threaded it into the evaluator.</para>
    /// </summary>
    public sealed class RuntimeSpell
    {
        public SpellDefinition Definition { get; }

        /// <summary> Pre-resolved weapon rules for a damage spell's hits (Blast, Bane, …); empty otherwise. </summary>
        public IReadOnlyList<ResolvedRule> WeaponRules { get; }

        public RuntimeSpell(SpellDefinition definition, IReadOnlyList<ResolvedRule> weaponRules)
        {
            Definition = definition;
            WeaponRules = weaponRules;
        }

        public string Name => Definition.Name;
        public int Threshold => Definition.Threshold;
        public TargetSelector Target => Definition.Target;
        public Effect Effect => Definition.Effect;
    }
}
