using System.Linq;
using System.Text.Json.Serialization;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Definitions;

/// <summary>
/// Predicate gating whether an <see cref="Effect"/> fires at a hook. Stored on
/// a <c>HookEntry</c>; evaluated by the engine against an <c>IHookContext</c>
/// when the relevant <see cref="EHookID"/> dispatches.
///
/// Implemented as an abstract record with sealed nested record subtypes — same
/// closed-sum-type pattern as <see cref="TokenClearTrigger"/> and <see cref="Cost"/>.
/// Pattern-match in a switch expression to evaluate:
///
/// <code>
/// bool ok = condition switch
/// {
///     Condition.Always                              => true,
///     Condition.UnmodifiedRollEquals(var v)         => ctx.UnmodifiedRoll == v,
///     Condition.DistanceGreaterThan(var n)          => ctx.Distance > n,
///     Condition.And(var l, var r)                   => Evaluate(l, ctx) && Evaluate(r, ctx),
///     // ...
/// };
/// </code>
///
/// Vocabulary grows on demand as the rule corpus surfaces new patterns. Start
/// with the half-dozen subtypes the first tests exercise; add the rest when a
/// real rule needs them.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Always), "always")]
[JsonDerivedType(typeof(UnitHasRule), "unitHasRule")]
[JsonDerivedType(typeof(AllModelsHaveThisRule), "allModelsHaveThisRule")]
[JsonDerivedType(typeof(MostModelsHaveThisRule), "mostModelsHaveThisRule")]
[JsonDerivedType(typeof(TargetHasRule), "targetHasRule")]
[JsonDerivedType(typeof(WeaponHasRule), "weaponHasRule")]
[JsonDerivedType(typeof(ActionTypeIs), "actionTypeIs")]
[JsonDerivedType(typeof(UnmodifiedRollEquals), "unmodifiedRollEquals")]
[JsonDerivedType(typeof(DistanceGreaterThan), "distanceGreaterThan")]
[JsonDerivedType(typeof(AttackedFromOverInches), "attackedFromOverInches")]
[JsonDerivedType(typeof(StatGreaterOrEqualTo), "statGreaterOrEqualTo")]
[JsonDerivedType(typeof(TargetMajorityHasTough), "targetMajorityHasTough")]
[JsonDerivedType(typeof(TokenPresent), "tokenPresent")]
[JsonDerivedType(typeof(And), "and")]
[JsonDerivedType(typeof(Or), "or")]
[JsonDerivedType(typeof(Not), "not")]
[JsonDerivedType(typeof(AfterMoving), "afterMoving")]
[JsonDerivedType(typeof(MostModelsWithinInchesOfTerrain), "mostModelsWithinInchesOfTerrain")]
[JsonDerivedType(typeof(IsMelee), "isMelee")]
[JsonDerivedType(typeof(IsCharging), "isCharging")]
[JsonDerivedType(typeof(IsNotSpell), "isNotSpell")]
[JsonDerivedType(typeof(IsSpell), "isSpell")]
[JsonDerivedType(typeof(UnpredictableBranchIs), "unpredictableBranchIs")]
public abstract record Condition
{

    public virtual bool Evaluate(RuleInvocation invocation)
    {
        throw new NotImplementedException("Condition is not yet evaluable.");
    }
    
    public virtual IReadOnlyCollection<Type> RequiredCapabilities => Array.Empty<Type>();

    /// <summary>
    /// Always matches. Used for unconditional effects (e.g. passive aura
    /// modifiers that don't gate on any runtime state).
    /// </summary>
    public sealed record Always : Condition
    {
        public override bool Evaluate(RuleInvocation invocation) => true;
    }

    /// <summary>
    /// True if the unit the rule is attached to has another named rule.
    /// Used for unit-composition gates like Harassing Boost ("if this unit has
    /// Harassing, apply X").
    /// </summary>
    public sealed record UnitHasRule(string RuleName) : Condition
    {
        // Case-insensitive, matching the resolver: "has Bane when Shooting" is true for a unit carrying
        // the rule registered as "Bane when shooting".
        public override bool Evaluate(RuleInvocation invocation) =>
            invocation.Bearer.RuleDefinitions.Any(
                r => string.Equals(r.Definition.Name, RuleName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.RequestedName, RuleName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True only when EVERY living model in the bearer unit carries the rule being evaluated
    /// (<see cref="RuleInvocation.Definition"/>). Gates the unit-wide defensive/morale rules the rulebook
    /// says apply only if the whole unit has them (Stealth, Regeneration, Fearless): a joined hero that
    /// doesn't natively carry the rule breaks it for the unit, and a lone hero carrying it doesn't grant it
    /// to the unit. A model "has" the rule if it's on the model's own <see cref="IModel.RuleDefinitions"/>,
    /// or — for a native (non-joined) model — STATICALLY on the unit's <see cref="IUnit.RuleDefinitions"/>;
    /// a joined hero (<see cref="IUnit.JoinedHeroModelId"/>) doesn't count the host's static rules, since
    /// the merge relocated the hero's own rules onto the hero model and OPR heroes don't inherit the host
    /// unit's rules. Unit-held GRANTS (aura / "gains rule X" tokens) count for EVERY living model, hero
    /// included (#183): every grant in the vocabulary targets the whole current unit — auras say "this
    /// model and its unit", buff spells pick a unit — so a hero that brings its own aura, or a buff cast on
    /// the combined unit, must not break the gate. Matches by rule definition (these rules are
    /// argument-less); a future (X) variant needing arg-awareness would extend this. Self-referential —
    /// reads the firing rule's own identity, so one condition serves every all-models rule.
    /// </summary>
    public sealed record AllModelsHaveThisRule : Condition
    {
        public override bool Evaluate(RuleInvocation invocation)
        {
            SpecialRuleDefinition? definition = invocation.Definition;
            if (definition == null)
            {
                return true; // no rule identity to check — not reached on the passive path, which sets it
            }

            IUnit unit = invocation.Bearer;
            ModelID? heroModelId = unit.JoinedHeroModelId;
            // A static unit rule covers every NATIVE model; a grant covers every model including a joined
            // hero (grants target the current combined unit, static rules predate the hero's arrival).
            bool unitStatic = unit.RuleDefinitions.Any(r => r.Definition == definition);
            bool unitGranted = RuleGrantQueries.UnitHasGrantedRule(unit, definition);

            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive())
                {
                    continue;
                }

                bool isJoinedHero = heroModelId is ModelID hid && model.ID == hid;
                bool hasRule = model.RuleDefinitions.Any(r => r.Definition == definition)
                    || unitGranted
                    || (!isJoinedHero && unitStatic);

                if (!hasRule)
                {
                    return false;
                }
            }

            return true;
        }

    }

    /// <summary>
    /// True if MOST living models in the bearer's unit carry the firing rule - the "when a unit where most
    /// models have this rule ..." wording (#197 P7 No Retreat), as distinct from
    /// <see cref="AllModelsHaveThisRule"/>'s "where all models". Same ownership semantics in every other
    /// respect (per-model rules, a joined hero's exclusion from the host's static rules, unit-held grants
    /// counting for everyone), so the two answer the same question at different thresholds.
    /// <para>
    /// "Most" is a strict majority of LIVING models - the same shape <see cref="TargetMajorityHasTough"/>
    /// uses. A unit with none alive is not a majority of anything, so it answers false rather than
    /// vacuously true; that matters here because the rules using it fire on morale, which a wiped-out unit
    /// never takes anyway.
    /// </para>
    /// </summary>
    public sealed record MostModelsHaveThisRule : Condition
    {
        public override bool Evaluate(RuleInvocation invocation)
        {
            SpecialRuleDefinition? definition = invocation.Definition;
            if (definition == null)
            {
                return true; // no rule identity to check — mirrors AllModelsHaveThisRule
            }

            IUnit unit = invocation.Bearer;
            ModelID? heroModelId = unit.JoinedHeroModelId;
            bool unitStatic = unit.RuleDefinitions.Any(r => r.Definition == definition);
            bool unitGranted = RuleGrantQueries.UnitHasGrantedRule(unit, definition);

            int living = 0;
            int carriers = 0;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive())
                {
                    continue;
                }

                living++;
                bool isJoinedHero = heroModelId is ModelID hid && model.ID == hid;
                if (model.RuleDefinitions.Any(r => r.Definition == definition)
                    || unitGranted
                    || (!isJoinedHero && unitStatic))
                {
                    carriers++;
                }
            }

            return living > 0 && carriers * 2 > living;
        }
    }

    /// <summary>
    /// True if the FIRING weapon carries the named rule - read off <see cref="RuleInvocation.Weapon"/>, so
    /// it is meaningful only for a weapon-scoped rule (a unit-scoped rule's invocation has no weapon, and
    /// this returns false). Lets a weapon rule gate on a companion weapon rule: Quick Readjustment ("this
    /// model ignores the shoot-after-move penalty when using Indirect weapons") is routed onto every weapon
    /// and fires its +1 only on the weapon that also carries Indirect, cancelling that rule's -1.
    /// </summary>
    public sealed record WeaponHasRule(string RuleName) : Condition
    {
        // Case-insensitive, matching the resolver (see UnitHasRule).
        public override bool Evaluate(RuleInvocation invocation) =>
            invocation.Weapon?.RuleDefinitions.Any(
                r => string.Equals(r.Definition.Name, RuleName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.RequestedName, RuleName, StringComparison.OrdinalIgnoreCase)) ?? false;
    }

    /// <summary>
    /// True if the target (defender, charged unit, spell target, etc.) has the
    /// named rule. Used for "vs. units with rule X" effects.
    /// </summary>
    public sealed record TargetHasRule(string RuleName) : CapabilityCondition<IHasTarget>
    {
        // Case-insensitive, matching the resolver (see UnitHasRule).
        protected override bool EvaluateCore(IHasTarget context) =>
            context.Target.RuleDefinitions.Any(
                r => string.Equals(r.Definition.Name, RuleName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.RequestedName, RuleName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True if the action the bearer just declared matches the given type.
    /// E.g. Rapid Rush applies only when the action is <see cref="EActionType.Rush"/>.
    /// </summary>
    public sealed record ActionTypeIs(EActionType ActionType) : CapabilityCondition<IHasActionType>
    {
        protected override bool EvaluateCore(IHasActionType context) => context.ActionType == ActionType;
    }

    /// <summary>
    /// True if the relevant die came up exactly this value before any modifiers
    /// were applied. Typically <see cref="DieValue"/> is 6 (Furious / Rending /
    /// Surge fire on natural 6) or 1 (Shred fires on natural 1 to block).
    /// </summary>
    public sealed record UnmodifiedRollEquals(int DieValue) : CapabilityCondition<IHasUnmodifiedHitRolls>
    {
        protected override bool EvaluateCore(IHasUnmodifiedHitRolls context)
        {
            return context.UnmodifiedHitRolls.At(DieValue) > 0;
        }
    }

    /// <summary>
    /// True if the source-to-target distance exceeds the given inches.
    /// E.g. Stealth applies when shot from &gt; 9" away; Piercing Hunter
    /// at &gt; 9".
    /// </summary>
    public sealed record DistanceGreaterThan(float DistanceInches) : CapabilityCondition<IHasDistance>
    {
        protected override bool EvaluateCore(IHasDistance context)
        {
            return context.DistanceInches > DistanceInches;
        }
    }

    /// <summary>
    /// True if the attack was <i>launched</i> from further than the given inches — the live distance when
    /// shooting, the activation-start distance to the defender when charging. See
    /// <see cref="IHasAttackOriginDistance"/> for why this is not just <see cref="DistanceGreaterThan"/>:
    /// a melee attack resolves in base contact, so a live-distance gate of 9" can never pass there.
    ///
    /// Exists because six corpus rules (Devout/Ferocious/Warbound/Infected/Mischievous/Scrapper Boost)
    /// share the wording "when it shoots or charges enemies over 9\" away". Each expresses that as one
    /// condition; a non-charging melee swing reports 0 and is excluded, as the rule text intends.
    /// </summary>
    public sealed record AttackedFromOverInches(float DistanceInches) : CapabilityCondition<IHasAttackOriginDistance>
    {
        protected override bool EvaluateCore(IHasAttackOriginDistance context)
        {
            return context.AttackOriginDistanceInches > DistanceInches;
        }
    }

    /// <summary>
    /// True when a strict majority of the BEARER unit's living models are within <see cref="DistanceInches"/>
    /// of any terrain piece. The Grounded family - "if a unit where all models have this rule has most of
    /// them within 1in of terrain, they get +1 to defense / +1 to hit / enemies get -1 to hit" - pairs this
    /// with <see cref="AllModelsHaveThisRule"/> under an <see cref="And"/>.
    ///
    /// Reads <see cref="RuleInvocation.Bearer"/> directly (like <see cref="AllModelsHaveThisRule"/>) rather
    /// than a capability context field, because the fact is about the FIRING unit and the same hook context
    /// serves both the attacker and the defender seat. The terrain LIST comes from the context
    /// (<see cref="IHasTerrain"/>, table-level and seat-independent); a context that cannot supply it carries
    /// an empty list, so the rule conservatively does not fire there.
    /// </summary>
    public sealed record MostModelsWithinInchesOfTerrain(float DistanceInches) : Condition
    {
        public override IReadOnlyCollection<Type> RequiredCapabilities => [typeof(IHasTerrain)];

        public override bool Evaluate(RuleInvocation invocation)
        {
            if (invocation.Hook is not IHasTerrain terrainContext)
            {
                throw new InvalidOperationException(
                    $"{nameof(MostModelsWithinInchesOfTerrain)} requires {nameof(IHasTerrain)}, but the " +
                    $"firing context ({invocation.Hook?.GetType().Name ?? "null"}) does not provide it.");
            }

            return TerrainProximityQueries.MostModelsWithinInches(
                invocation.Bearer, terrainContext.Terrain, DistanceInches);
        }
    }

    /// <summary>
    /// True if the target's value for the given stat is at least the threshold.
    /// Two parameters because both <i>which stat</i> (Quality / Defense / Tough)
    /// and <i>the threshold</i> vary per rule — e.g. Melee Slayer keys on
    /// <see cref="EStatKind.Tough"/> &gt;= 3.
    /// </summary>
    public sealed record StatGreaterOrEqualTo(EStatKind Stat, int StatValue) : CapabilityCondition<IHasTarget>
    {
        protected override bool EvaluateCore(IHasTarget context) => Stat switch
        {
            EStatKind.Quality => context.Target.Quality >= StatValue,
            EStatKind.Defense => context.Target.Defense >= StatValue,
            // Tough is per-model and uniform across a unit in the corpus, so read it as
            // "most living models have Tough >= value" — the same majority TargetMajorityHasTough uses.
            EStatKind.Tough => MajorityToughAtLeast(context.Target, StatValue),
            _ => false,
        };
    }

    /// <summary>
    /// True when more than half of the target's living models have Tough (max wounds) at least
    /// <paramref name="min"/>. Shared by <see cref="TargetMajorityHasTough"/> and the Tough arm of
    /// <see cref="StatGreaterOrEqualTo"/>. Dead models are excluded — a unit reduced to a single Tough
    /// survivor still counts as a Tough target.
    /// </summary>
    private static bool MajorityToughAtLeast(IUnit target, int min)
    {
        var living = target.Models.Where(m => m.GetIsAlive()).ToList();
        return living.Count > 0 && living.Count(m => m.TotalWounds >= min) * 2 > living.Count;
    }

    /// <summary>
    /// True if a majority of models in the target have Tough at least
    /// <see cref="MinToughValue"/>. Separate subtype because the "majority"
    /// computation isn't expressible as a simple stat comparison —
    /// it's a per-target structural query.
    /// </summary>
    public sealed record TargetMajorityHasTough(int MinToughValue) : CapabilityCondition<IHasTarget>
    {
        protected override bool EvaluateCore(IHasTarget context) =>
            MajorityToughAtLeast(context.Target, MinToughValue);
    }

    /// <summary>
    /// True if the bearer's token container holds at least <see cref="MinCount"/>
    /// tokens of <see cref="TType"/>. Default <see cref="MinCount"/> = 1 makes
    /// the "do they have any?" case ergonomic; values &gt; 1 support stacking-
    /// marker thresholds (e.g. "do they have 2+ Piercing Frenzy markers?").
    /// </summary>
    public sealed record TokenPresent(TokenType TType, int MinCount = 1) : Condition
    {
        public override bool Evaluate(RuleInvocation invocation)
        {
            return invocation.Bearer.Tokens.GetTokenCount(TType) >= MinCount;
        }
    }

    /// <summary>
    /// Logical AND of two conditions. Both must match.
    /// </summary>
    public sealed record And(Condition Left, Condition Right) : Condition
    {
        public override bool Evaluate(RuleInvocation invocation) =>
            Left.Evaluate(invocation) && Right.Evaluate(invocation);

        public override IReadOnlyCollection<Type> RequiredCapabilities =>
            Left.RequiredCapabilities.Concat(Right.RequiredCapabilities).Distinct().ToArray();
    }

    /// <summary>
    /// Logical OR of two conditions. Either matching is sufficient.
    /// </summary>
    public sealed record Or(Condition Left, Condition Right) : Condition
    {
        public override bool Evaluate(RuleInvocation invocation) =>
            Left.Evaluate(invocation) || Right.Evaluate(invocation);

        public override IReadOnlyCollection<Type> RequiredCapabilities =>
            Left.RequiredCapabilities.Concat(Right.RequiredCapabilities).Distinct().ToArray();
    }

    /// <summary>
    /// Logical NOT — true when the inner condition is false. Used for
    /// "not fatigued," "not in cover," etc.
    /// </summary>
    public sealed record Not(Condition Inner) : Condition
    {
        public override bool Evaluate(RuleInvocation invocation) => !Inner.Evaluate(invocation);

        public override IReadOnlyCollection<Type> RequiredCapabilities => Inner.RequiredCapabilities;
    }

    /// <summary>
    /// True if the attacking unit moved before this attack. Used by Indirect
    /// (-1 to hit when shooting after moving).
    /// </summary>
    public sealed record AfterMoving : CapabilityCondition<IHasAttackerMoved>
    {
        protected override bool EvaluateCore(IHasAttackerMoved context) => context.AttackerMoved;
    }

    /// <summary>
    /// True when the attack being resolved is melee (vs. shooting). Gates melee-only
    /// rules (Furious) on hooks that fire in both combat kinds. Shooting-only rules
    /// rely on a different condition (e.g. distance) rather than an explicit IsShooting.
    /// </summary>
    public sealed record IsMelee : CapabilityCondition<IHasCombatKind>
    {
        protected override bool EvaluateCore(IHasCombatKind context) => context.IsMelee;
    }

    /// <summary>
    /// True when the attacking unit is charging (resolving the melee it initiated this
    /// activation, not striking back). Gates charge-only rules (Thrust) on hit/save hooks
    /// shared by the charger's swing and the defender's strike-back.
    /// </summary>
    public sealed record IsCharging : CapabilityCondition<IHasCharging>
    {
        protected override bool EvaluateCore(IHasCharging context) => context.IsCharging;
    }

    /// <summary>
    /// #197 (P15): true when this attack's Unpredictable die landed on <see cref="Branch"/>. The die is
    /// rolled once per attack action and carried on both the hit-roll-modifier hook (the +1-to-hit arm gates
    /// on <see cref="EUnpredictableBranch.HitBonus"/>) and the hit-roll-complete hook (the AP arm gates on
    /// <see cref="EUnpredictableBranch.ApBonus"/>), so the two arms read the SAME roll and exactly one fires.
    /// </summary>
    public sealed record UnpredictableBranchIs(EUnpredictableBranch Branch)
        : CapabilityCondition<IHasUnpredictableBranch>
    {
        protected override bool EvaluateCore(IHasUnpredictableBranch context) =>
            context.UnpredictableBranch == Branch;
    }

    /// <summary>
    /// True when the hits being resolved do NOT come from a spell. Gates rules whose corpus text
    /// excludes spell damage (Shielded's "against hits that are not from spells") on the hit-complete
    /// hook, which the spell-damage pipeline fires with <c>IsSpell: true</c> and the weapon pipelines
    /// fire with the default false.
    /// </summary>
    public sealed record IsNotSpell : CapabilityCondition<IHasIsSpell>
    {
        protected override bool EvaluateCore(IHasIsSpell context) => !context.IsSpell;
    }

    /// <summary>
    /// True when the hits being resolved DO come from a spell — <see cref="IsNotSpell"/>'s positive
    /// twin. Gates rules with a spells-only facet (Resistance's "if the wounds were from a spell, they
    /// are ignored on a 2+ instead").
    /// </summary>
    public sealed record IsSpell : CapabilityCondition<IHasIsSpell>
    {
        protected override bool EvaluateCore(IHasIsSpell context) => context.IsSpell;
    }
}
