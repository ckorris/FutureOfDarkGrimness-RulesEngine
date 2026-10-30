using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// #442: rule definitions by name for the Tactician - the core catalog plus every army's embedded definitions,
    /// read from the persisted army rule data (<see cref="ArmyData.RestoreRuleData"/>), which is exactly what the
    /// game's own army-load resolver was built from (<c>GameBootstrap.BuildRuleResolver</c> /
    /// <c>RestoreArmyRuleData</c>). Built lazily, once: army rule data never changes mid-game.
    /// <para>
    /// The Tactician's evaluator is still the bare one (no resolver: granted-rule read-back stays the recorded #191
    /// gap). This lookup only answers "what does the rule named X do" for a policy that has to price a choice
    /// BEFORE it is granted - an activation pick's helper rule (<see cref="Resolvers.TacticianAbilityEffectResolver"/>).
    /// </para>
    /// </summary>
    public sealed class TacticianRuleLookup
    {
        private readonly ITableState _tableState;
        private RuleResolver? _resolver;

        public TacticianRuleLookup(ITableState tableState)
        {
            _tableState = tableState;
        }

        public bool TryResolve(string ruleName, out ResolvedRule rule)
        {
            _resolver ??= Build(_tableState);
            return _resolver.TryResolve(ruleName, out rule);
        }

        private static RuleResolver Build(ITableState tableState)
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            foreach (IArmy army in tableState.Armies.Objects)
            {
                if (army is not ArmyData data) continue;
                Rules.Serialization.ArmyRuleDataPersistence.PersistedArmyRuleData? persisted = data.RestoreRuleData();
                if (persisted == null) continue;
                foreach (Rules.Definitions.SpecialRuleDefinition definition in persisted.RuleDefinitions)
                    resolver.RegisterOrReplace(definition);
            }
            return resolver;
        }
    }
}
