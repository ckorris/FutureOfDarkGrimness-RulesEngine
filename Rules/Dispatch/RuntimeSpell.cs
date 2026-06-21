using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// A <see cref="SpellDefinition"/> after army-load resolution (#033). For a damage spell
    /// (<see cref="Effect.DealHits"/>) it additionally carries the <see cref="ResolvedRule"/>s that the
    /// effect's <c>WithRules</c> names resolve to — resolved at load, where the rule resolver is live, so
    /// the cast stage can attach them to the synthetic spell weapon without holding a resolver of its own
    /// (the resolver isn't reachable from a stage). Non-damage spells carry an empty <see cref="WeaponRules"/>.
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
