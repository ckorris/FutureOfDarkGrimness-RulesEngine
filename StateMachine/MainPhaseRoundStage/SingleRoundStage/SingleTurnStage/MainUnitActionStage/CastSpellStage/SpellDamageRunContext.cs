using System.Collections.Generic;
using FDG.Data;

namespace FDG.Stages
{
    /// <summary>
    /// #034 — the per-cast damage run shared by <see cref="CastSpellStage"/> and its looped child
    /// <see cref="ResolveSpellDamageStage"/>. Holds the resolved damage spell's synthetic weapon, its base
    /// hit count, and the chosen target units, plus a cursor so each target is resolved with its OWN fresh
    /// <see cref="CombatMetadata"/> — the parent-context-holds-the-queue pattern that
    /// <see cref="ShootStage"/>/<see cref="FireStage"/> use (<c>ConsumeAttackIntoContext</c>). The cast
    /// itself (spell pick, target pick, token spend, 4+ roll) already happened in
    /// <see cref="CastSpellStage.Enter"/>; this carries only what the looped damage resolution needs.
    /// </summary>
    public sealed class SpellDamageRunContext
    {
        /// <summary> The casting unit — the synthetic attack's attacker. </summary>
        public DataBinding<UnitData> Caster { get; }

        /// <summary> The synthetic spell weapon (the spell's AP + its pre-resolved weapon rules). Shared
        /// across targets — the same spell. </summary>
        public Weapon Weapon { get; }

        /// <summary> The spell's authored base hit count (before per-target Blast/on-6 folds). </summary>
        public int BaseHits { get; }

        /// <summary>
        /// Single-model targeting (#034): when set, wounds are confined to this one model ("a unit of [1]").
        /// Single-model spells use <c>MaxCount = 1</c>, so there is exactly one target and this model belongs
        /// to it. Null for the normal whole-unit (and multi-unit) cases.
        /// </summary>
        public DataBinding<ModelData>? IndividualModel { get; }

        private readonly IReadOnlyList<DataBinding<UnitData>> _targets;
        private int _cursor;

        public SpellDamageRunContext(DataBinding<UnitData> caster, Weapon weapon, int baseHits,
            IReadOnlyList<DataBinding<UnitData>> targets, DataBinding<ModelData>? individualModel)
        {
            Caster = caster;
            Weapon = weapon;
            BaseHits = baseHits;
            _targets = targets;
            IndividualModel = individualModel;
        }

        /// <summary> True while at least one chosen target still needs its damage resolved. </summary>
        public bool HasMoreTargets => _cursor < _targets.Count;

        /// <summary> Advances to and returns the next target to resolve; callers check
        /// <see cref="HasMoreTargets"/> first. </summary>
        public DataBinding<UnitData> ConsumeNextTarget() => _targets[_cursor++];
    }
}
