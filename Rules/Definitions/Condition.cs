using System.Linq;
using System.Text.Json.Serialization;
using FDG.Rules.Dispatch;
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
[JsonDerivedType(typeof(MostModelsHaveThisRule), "mostModelsHaveThisRule")]
// #428: the pre-3.6 kind still parses, as the most-models gate (see LegacyAllModelsHaveThisRule).
[JsonDerivedType(typeof(LegacyAllModelsHaveThisRule), "allModelsHaveThisRule")]
[JsonDerivedType(typeof(EveryModelHasThisRule), "everyModelHasThisRule")]
[JsonDerivedType(typeof(TargetHasRule), "targetHasRule")]
[JsonDerivedType(typeof(WeaponHasRule), "weaponHasRule")]
[JsonDerivedType(typeof(ActionTypeIs), "actionTypeIs")]
[JsonDerivedType(typeof(UnmodifiedRollEquals), "unmodifiedRollEquals")]
// #433: the OPR 3.6 "Enhanced X" family's "extra attacks on SUCCESSFUL unmodified hit results of 5-6".
[JsonDerivedType(typeof(UnmodifiedRollHits), "unmodifiedRollHits")]
[JsonDerivedType(typeof(DistanceGreaterThan), "distanceGreaterThan")]
[JsonDerivedType(typeof(AttackedFromOverInches), "attackedFromOverInches")]
[JsonDerivedType(typeof(StatGreaterOrEqualTo), "statGreaterOrEqualTo")]
[JsonDerivedType(typeof(TargetMajorityHasTough), "targetMajorityHasTough")]
[JsonDerivedType(typeof(TokenPresent), "tokenPresent")]
[JsonDerivedType(typeof(CountsAsFatiguedInMelee), "countsAsFatiguedInMelee")]
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
// #431: the OPR 3.6 weapon-rule family's target gates (Smash, Pierce, Fragment, Puncture, Decimate, Destructive,
// Demolish, Purge, Reap). Named for the mechanism so #432-#435 can reuse them.
[JsonDerivedType(typeof(TargetOverHalfWeaponRange), "targetOverHalfWeaponRange")]
[JsonDerivedType(typeof(TargetMostModelsDefenseInRange), "targetMostModelsDefenseInRange")]
[JsonDerivedType(typeof(TargetAboveHalfStrength), "targetAboveHalfStrength")]
[JsonDerivedType(typeof(TargetShakenOrFatigued), "targetShakenOrFatigued")]
[JsonDerivedType(typeof(TargetMostModelsWithinInchesOfTerrain), "targetMostModelsWithinInchesOfTerrain")]
// #432: the OPR 3.6 range-conditional army-rule family ("when this model shoots/charges enemies over 9"/6" away",
// "when shot/charged from over 9"/6" away"). The thresholds are data, one per attack kind.
[JsonDerivedType(typeof(AttackingFromOver), "attackingFromOver")]
[JsonDerivedType(typeof(AttackedFromOver), "attackedFromOver")]
// #435: OPR 3.6 AoF Banner's "+1 to Morale Test rolls triggered by shooting/melee".
[JsonDerivedType(typeof(MoraleTestFromShootingOrMelee), "moraleTestFromShootingOrMelee")]
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
    /// True if the bearer unit has another named rule - the unit-composition gate of every "If this model has X,
    /// ..." rule (OPR 3.6's Enhanced X family, #433; the catalog's Harassing Boost before it).
    /// <para>
    /// "Has" is read everywhere the unit can carry a rule, because the army-load paths scatter them: a unit-level
    /// rule sits on <see cref="IUnit.RuleDefinitions"/>; a Weapon-scope rule named at unit level (Infected, Mischievous
    /// - "on its weapons") is re-homed onto the unit's weapons and is NOT on the unit list; a joined hero's rules are
    /// relocated onto the hero's model; an aura or "gains rule X" grant is a <see cref="TokenType.RuleGrant"/> token
    /// (Lustbound Aura, the Versatile Warrior pick's one-activation helpers). So the test is: the unit's own list, any
    /// grant token, any living model's list, or any living model's weapon. Before #433 only the unit list was read,
    /// which made "Enhanced Lustbound" inert beside a Lustbound that arrived by aura or sat on a joined hero.
    /// </para>
    /// Case-insensitive, matching the resolver: "has Bane when Shooting" is true for a unit carrying the rule
    /// registered as "Bane when shooting"; an alias matches by its requested name too.
    /// </summary>
    public sealed record UnitHasRule(string RuleName) : Condition
    {
        public override bool Evaluate(RuleInvocation invocation) => Holds(invocation.Bearer, RuleName);

        /// <summary> THE predicate - see the type summary for what counts as having a rule. </summary>
        public static bool Holds(IUnit unit, string ruleName)
        {
            if (Carries(unit.RuleDefinitions, ruleName) || RuleGrantQueries.UnitHasGrantedRule(unit, ruleName))
            {
                return true;
            }

            bool anyAlive = unit.Models.Any(m => m.GetIsAlive());
            foreach (IModel model in unit.Models)
            {
                if (anyAlive && !model.GetIsAlive())
                {
                    continue;
                }

                if (Carries(model.RuleDefinitions, ruleName))
                {
                    return true;
                }

                foreach (IWeapon weapon in model.Weapons)
                {
                    if (Carries(weapon.RuleDefinitions, ruleName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool Carries(IReadOnlyList<ResolvedRule> rules, string ruleName)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                ResolvedRule r = rules[i];
                if (string.Equals(r.Definition.Name, ruleName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.RequestedName, ruleName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The ownership count both rule-ownership gates (<see cref="MostModelsHaveThisRule"/>,
    /// <see cref="EveryModelHasThisRule"/>) read: how many models of the bearer unit are counted, and how many
    /// of those carry the rule being evaluated (<see cref="RuleInvocation.Definition"/>).
    /// <para>
    /// A model "has" the rule if it's on the model's own <see cref="IModel.RuleDefinitions"/>, or - for a
    /// native (non-joined) model - STATICALLY on the unit's <see cref="IUnit.RuleDefinitions"/>; a joined hero
    /// (<see cref="IUnit.JoinedHeroModelId"/>) doesn't count the host's static rules, since the merge relocated
    /// the hero's own rules onto the hero model and OPR heroes don't inherit the host unit's rules. The hero
    /// still COUNTS as a model of the unit, which is what OPR 3.6's "most models" wording is for (#428, closing
    /// #303): a 10-model unit with a joined hero lacking the rule is 10 of 11 and keeps it. Unit-held GRANTS
    /// (aura / "gains rule X" tokens) count for EVERY model, hero included (#183): every grant in the
    /// vocabulary targets the whole current unit. Matches by rule definition (these rules are argument-less);
    /// self-referential - reads the firing rule's own identity, so one condition serves every gated rule.
    /// </para>
    /// <para>
    /// Counted models are the LIVING ones, so casualties shift the count mid-game. A unit with none alive is
    /// counted as it stood (all its models): Reinforcement's "when a unit where most models have this rule is
    /// ... fully destroyed" is evaluated on a wiped-out unit, and the models that just died are the unit the
    /// text describes (#428 decision; the old all-models gate passed this vacuously).
    /// </para>
    /// </summary>
    private static (int Counted, int Carriers) CountRuleCarriers(IUnit unit, SpecialRuleDefinition definition)
    {
        ModelID? heroModelId = unit.JoinedHeroModelId;
        // A static unit rule covers every NATIVE model; a grant covers every model including a joined
        // hero (grants target the current combined unit, static rules predate the hero's arrival).
        bool unitStatic = unit.RuleDefinitions.Any(r => r.Definition == definition);
        bool unitGranted = RuleGrantQueries.UnitHasGrantedRule(unit, definition);
        bool anyAlive = unit.Models.Any(m => m.GetIsAlive());

        int counted = 0;
        int carriers = 0;
        foreach (IModel model in unit.Models)
        {
            if (anyAlive && !model.GetIsAlive())
            {
                continue;
            }

            counted++;
            bool isJoinedHero = heroModelId is ModelID hid && model.ID == hid;
            if (model.RuleDefinitions.Any(r => r.Definition == definition)
                || unitGranted
                || (!isJoinedHero && unitStatic))
            {
                carriers++;
            }
        }

        return (counted, carriers);
    }

    /// <summary>
    /// True if MOST living models in the bearer's unit carry the firing rule: OPR 3.6's "when a unit where
    /// most models have this rule ..." gate, which replaced 3.5's "where all models" on every unit-wide rule
    /// (#428: Fearless, Regeneration, Stealth, Artillery, the defensive Subject-seat family, the whole-unit
    /// reposition/reactivation family, ...). Ownership semantics are <see cref="CountRuleCarriers"/>'s.
    /// <para>
    /// "Most" is a strict majority (more than half) - the same shape <see cref="TargetMajorityHasTough"/> uses.
    /// 5 of 9 qualifies; 4 of 9 and 2 of 4 do not.
    /// </para>
    /// <para>
    /// Not sealed only so <see cref="LegacyAllModelsHaveThisRule"/> can be this condition under its old JSON
    /// kind; nothing else should derive from it.
    /// </para>
    /// </summary>
    public record MostModelsHaveThisRule : Condition
    {
        public override bool Evaluate(RuleInvocation invocation)
        {
            SpecialRuleDefinition? definition = invocation.Definition;
            if (definition == null)
            {
                return true; // no rule identity to check - not reached on the passive path, which sets it
            }

            (int counted, int carriers) = CountRuleCarriers(invocation.Bearer, definition);
            return counted > 0 && carriers * 2 > counted;
        }
    }

    /// <summary>
    /// #428 legacy alias: the pre-3.6 <c>"allModelsHaveThisRule"</c> JSON kind, which now means
    /// <see cref="MostModelsHaveThisRule"/>. OPR 3.6 rewrote every "where all models have this rule" gate as
    /// "where most models", and old <c>.fdgarmy</c> files (plus <c>.fdgsave</c> snapshots, and the bundled
    /// <c>.fdgbook</c> files until #438 re-bakes them) embed their definitions with this kind. By the upgrade
    /// plan's rule 6 old files keep loading but get 3.6 semantics, so this IS the most-models condition: it
    /// inherits the evaluation and every <c>is MostModelsHaveThisRule</c> check (the validator's gate
    /// requirement included) accepts it.
    /// <para>
    /// It exists as its own subtype so that (a) the old kind still deserializes and round-trips unchanged,
    /// (b) <c>--trace-rules</c> names it (<c>LegacyAllModelsHaveThisRule</c>), so a trace shows which
    /// definitions are still on the old kind, and (c) <see cref="Dispatch.RuleValidator.ValidateAuthoring"/> can
    /// refuse it in newly authored data - write <c>mostModelsHaveThisRule</c>, or
    /// <c>everyModelHasThisRule</c> for the two 3.6 rules that keep "all models".
    /// </para>
    /// <para>
    /// Known consequence: an old army file that embeds one of those two "all models" rules (Inquisitorial
    /// Agent; Martial Prowess is catalog-only) under this kind gets "most" for it, since the kind alone can't
    /// tell the two apart.
    /// </para>
    /// </summary>
    public sealed record LegacyAllModelsHaveThisRule : MostModelsHaveThisRule;

    /// <summary>
    /// True only when EVERY living model in the bearer unit carries the firing rule - the "if all models in
    /// this unit have this rule" wording OPR 3.6 keeps for exactly two rules, Inquisitorial Agent and Martial
    /// Prowess (#428). Everything else gates on <see cref="MostModelsHaveThisRule"/>; reach for this only when
    /// the 3.6 text still says "all". Ownership semantics are <see cref="CountRuleCarriers"/>'s, so a joined
    /// hero without the rule breaks it for the whole unit.
    /// <para>
    /// JSON kind <c>everyModelHasThisRule</c>, deliberately not the old <c>allModelsHaveThisRule</c>, which is
    /// the legacy alias of the most-models gate (<see cref="LegacyAllModelsHaveThisRule"/>).
    /// </para>
    /// </summary>
    public sealed record EveryModelHasThisRule : Condition
    {
        public override bool Evaluate(RuleInvocation invocation)
        {
            SpecialRuleDefinition? definition = invocation.Definition;
            if (definition == null)
            {
                return true; // no rule identity to check - mirrors MostModelsHaveThisRule
            }

            (int counted, int carriers) = CountRuleCarriers(invocation.Bearer, definition);
            return carriers == counted;
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
    /// True if some hit die came up exactly this value before any modifiers were applied AND counts as
    /// that value for special rules. Typically <see cref="DieValue"/> is 6 (Furious / Rending / Surge fire
    /// on a natural 6) or 1 (Hazardous fires on a natural 1 to hit).
    /// <para>
    /// Reads <see cref="IHasUnmodifiedHitRolls.SpecialRuleHitRolls"/>, not the raw histogram: under
    /// Core Rules v3.6.0 Fatigue (#418) a fatigued unit's melee 6s "don't count as rolls of 6 for special
    /// rules", so this is false for them even though the dice show 6.
    /// </para>
    /// </summary>
    public sealed record UnmodifiedRollEquals(int DieValue) : CapabilityCondition<IHasUnmodifiedHitRolls>
    {
        protected override bool EvaluateCore(IHasUnmodifiedHitRolls context)
        {
            return context.SpecialRuleHitRolls.At(DieValue) > 0;
        }
    }

    /// <summary>
    /// #433: <see cref="UnmodifiedRollEquals"/> for a face that must also have HIT - true if some hit die came up
    /// exactly <see cref="DieValue"/> (as special rules see it, <see cref="IHasUnmodifiedHitRolls.SpecialRuleHitRolls"/>)
    /// AND that face is at or above the roll the attack needed (<see cref="IHasHitRollNeeded.HitRollNeeded"/>). OPR
    /// 3.6's Enhanced Bloodborn / Clan Warrior / Primal / Predator / Royal Warrior: "extra attacks on successful
    /// unmodified hit results of 5-6 (instead of only on 6)" - the 5s are the new face, and a 5 only counts when it
    /// hit: Quality 5+ or better after modifiers, never under a -1 that needs 6s, never for a Fatigued melee swing
    /// (which hits only on 6s - and #418 strips its 6s from the special-rule view, so such a unit gets nothing from 5s
    /// or 6s). A needed roll of 0 means every rolled die is a hit (synthetic hits, forecasts). The base rules' 6s need
    /// no such gate: a natural 6 always hits. JSON <c>{"kind": "unmodifiedRollHits", "dieValue": 5}</c>.
    /// </summary>
    public sealed record UnmodifiedRollHits(int DieValue) : Condition
    {
        public override IReadOnlyCollection<Type> RequiredCapabilities =>
            [typeof(IHasUnmodifiedHitRolls), typeof(IHasHitRollNeeded)];

        public override bool Evaluate(RuleInvocation invocation)
        {
            if (invocation.Hook is not IHasUnmodifiedHitRolls rolls || invocation.Hook is not IHasHitRollNeeded needed)
            {
                throw new InvalidOperationException(
                    $"{nameof(UnmodifiedRollHits)} requires {nameof(IHasUnmodifiedHitRolls)} and " +
                    $"{nameof(IHasHitRollNeeded)}, but the firing context " +
                    $"({invocation.Hook?.GetType().Name ?? "null"}) does not provide both.");
            }

            return rolls.SpecialRuleHitRolls.At(DieValue) > 0
                && (needed.HitRollNeeded <= 0 || DieValue >= needed.HitRollNeeded);
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
    /// <para>
    /// #432: 3.5 vocabulary. OPR 3.6 gives shooting and charging separate thresholds (9"/6"), so new data uses
    /// <see cref="AttackingFromOver"/> / <see cref="AttackedFromOver"/>; this kind stays because old army files and
    /// the not-yet-rebaked bundled books embed it (the Boost family and its 3.5 base rules).
    /// </para>
    /// </summary>
    public sealed record AttackedFromOverInches(float DistanceInches) : CapabilityCondition<IHasAttackOriginDistance>
    {
        protected override bool EvaluateCore(IHasAttackOriginDistance context)
        {
            return context.AttackOriginDistanceInches > DistanceInches;
        }
    }

    /// <summary>
    /// #432: the shared predicate of <see cref="AttackingFromOver"/> and <see cref="AttackedFromOver"/> - the attack
    /// was a shot from over <paramref name="shootInches"/> or a charge from over <paramref name="chargeInches"/>
    /// (both strictly over; an absent kind of attack is never over anything).
    /// </summary>
    private static bool ShotOrChargedFromOver(IHasShootChargeDistance context, float shootInches, float chargeInches) =>
        (context.ShootingDistanceInches is float shot && shot > shootInches)
        || (context.ChargeDistanceInches is float charge && charge > chargeInches);

    /// <summary>
    /// #432 (Actor seat): true when the BEARER is attacking from over the given distance - shooting enemies over
    /// <see cref="ShootInches"/> away, or charging enemies over <see cref="ChargeInches"/> away. OPR 3.6's offensive
    /// army rules: "When this model shoots/charges enemies over 9"/6" away, ..." (Devout, Ferocious, Brutal, Lucky,
    /// Havocbound, Piercing Warrior, Infected, Violent, Warbound, Destroyer, and the Versatile Warrior / Watchborn /
    /// Vinci Tech picks). JSON <c>{"kind": "attackingFromOver", "shootInches": 9, "chargeInches": 6}</c>; the pair is
    /// each rule's data, never a constant.
    /// <para>
    /// Distances come from <see cref="IHasShootChargeDistance"/>: the shooting distance is the closest-models,
    /// base-to-base attacker-to-target distance (the measurement #431's <see cref="TargetOverHalfWeaponRange"/>
    /// reads); the charge distance is the pre-charge gap snapshotted at the charger's activation start. False for a
    /// strike-back (not a charge) and for spell damage (not shooting). Seat-agnostic as a predicate - the attack
    /// context is the same for both seats - so <see cref="AttackedFromOver"/> is the same test under the name the
    /// defender's rules read; author this one on Actor-seat entries.
    /// </para>
    /// </summary>
    public sealed record AttackingFromOver(float ShootInches, float ChargeInches)
        : CapabilityCondition<IHasShootChargeDistance>
    {
        protected override bool EvaluateCore(IHasShootChargeDistance context) =>
            ShotOrChargedFromOver(context, ShootInches, ChargeInches);
    }

    /// <summary>
    /// #432 (Subject seat, and the tested unit at <see cref="EHookID.Morale_OnPreMoraleTest"/>): true when the
    /// BEARER was shot from over <see cref="ShootInches"/> away or charged from over <see cref="ChargeInches"/>
    /// away - the defender's mirror of <see cref="AttackingFromOver"/>. OPR 3.6's defensive and morale army rules:
    /// "If most models in this unit have this rule, enemy units shooting/charging them from over 9"/6" away get -1
    /// to hit rolls" (Changebound, Machine-Fog, Screened, Empyrean Spirit), "+1 to Defense rolls when shot/charged
    /// from over 9"/6" away" (Guarded, Sturdy), Knightborn / Warding / Angelic Blessing's 6+ wound ignore, and "+1
    /// to Morale Test rolls when shot/charged from over 9"/6" away" (Hive Bond, Hold the Line, Valiant). JSON
    /// <c>{"kind": "attackedFromOver", "shootInches": 9, "chargeInches": 6}</c>.
    /// <para>
    /// On an attack hook it reads the attack in flight (only the charger's swings carry a charge distance, so a
    /// charging unit's own rules never see itself as "charged"); at the morale hook it reads the attack that
    /// triggered the test (<see cref="Dispatch.Contexts.PreMoraleTestContext"/>). Pair it with
    /// <see cref="MostModelsHaveThisRule"/> - the validator requires the gate on Subject-seat attack hooks.
    /// </para>
    /// </summary>
    public sealed record AttackedFromOver(float ShootInches, float ChargeInches)
        : CapabilityCondition<IHasShootChargeDistance>
    {
        protected override bool EvaluateCore(IHasShootChargeDistance context) =>
            ShotOrChargedFromOver(context, ShootInches, ChargeInches);
    }

    /// <summary>
    /// #435 (at <see cref="EHookID.Morale_OnPreMoraleTest"/>): true when the Morale Test about to be rolled was
    /// triggered by shooting (the wound-driven half-strength test, strafe / Crossing Strike included - "as if it was
    /// shooting") or by melee (the loser's test); false for a test a spell or a special rule demanded (Mind Control,
    /// Fatigue Debuff, Spell Drain, a spell's "must take a Morale Test"). OPR 3.6 AoF Banner: "This model and its unit
    /// get +1 to Morale Test rolls triggered by shooting/melee." JSON <c>{"kind": "moraleTestFromShootingOrMelee"}</c>.
    /// Reads <see cref="IHasMoraleTestCause"/> (<see cref="Dispatch.Contexts.PreMoraleTestContext.Cause"/>).
    /// </summary>
    public sealed record MoraleTestFromShootingOrMelee : CapabilityCondition<IHasMoraleTestCause>
    {
        protected override bool EvaluateCore(IHasMoraleTestCause context) =>
            context.MoraleTestCause is EMoraleTestCause.Shooting or EMoraleTestCause.Melee;
    }

    /// <summary>
    /// True when a strict majority of the BEARER unit's living models are within <see cref="DistanceInches"/>
    /// of any terrain piece. The Grounded family - "if a unit where most models have this rule has most of
    /// them within 1in of terrain, they get +1 to defense / +1 to hit / enemies get -1 to hit" - pairs this
    /// with <see cref="MostModelsHaveThisRule"/> under an <see cref="And"/>.
    ///
    /// Reads <see cref="RuleInvocation.Bearer"/> directly (like <see cref="MostModelsHaveThisRule"/>) rather
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
    /// #431: true when the target is more than half of the FIRING WEAPON's range away - "when attacking units
    /// over half of this weapon's range away" (OPR 3.6 Smash, Pierce). Reads the context's attacker-to-target
    /// distance (<see cref="IHasDistance"/>, the closest-models measurement the shooting stages take) against
    /// <see cref="RuleInvocation.Weapon"/>'s printed <see cref="IWeapon.RangeInches"/>.
    /// <para>
    /// The printed range, not the effective one: the text says "this weapon's range", and range modifiers
    /// (Increased Shooting Range, Aircraft's -12") change what may be targeted, not the weapon's profile (#431
    /// decision). False - never throws - when the firing rule has no weapon (a unit-scoped copy), when the
    /// weapon has no range (a melee weapon), and in melee (an Assault weapon swung in base contact is never
    /// "over half its range away"), so the gate cannot leak onto attacks the text does not describe.
    /// </para>
    /// </summary>
    public sealed record TargetOverHalfWeaponRange : Condition
    {
        public override IReadOnlyCollection<Type> RequiredCapabilities => [typeof(IHasDistance)];

        public override bool Evaluate(RuleInvocation invocation)
        {
            if (invocation.Hook is not IHasDistance distance)
            {
                throw new InvalidOperationException(
                    $"{nameof(TargetOverHalfWeaponRange)} requires {nameof(IHasDistance)}, but the firing " +
                    $"context ({invocation.Hook?.GetType().Name ?? "null"}) does not provide it.");
            }

            if (invocation.Hook is IHasCombatKind { IsMelee: true })
            {
                return false;
            }

            float range = invocation.Weapon?.RangeInches ?? 0f;
            return range > 0f && distance.DistanceInches > range / 2f;
        }
    }

    /// <summary>
    /// #431: true when a strict majority of the target's LIVING models have a Defense between
    /// <see cref="MinDefense"/>+ and <see cref="MaxDefense"/>+ inclusive - "when attacking units where most models
    /// have Defense 5+ to Defense 6+" (Fragment: 5..6) / "Defense 2+ to Defense 3+" (Puncture: 2..3). The values
    /// are the save numbers, so 2 is the best Defense; the order of the two fields does not matter.
    /// <para>
    /// Per model: the rank and file have the unit's Defense, a joined hero its own
    /// (<see cref="Dispatch.HeroStatRules.GetModelDefense"/>), so a hero is one model of the majority, the #428
    /// reading of "most models". The stat as printed (after Armor's set), not this roll's modifiers - cover and
    /// buffs change the roll, not the model's Defense. No living models: false.
    /// </para>
    /// </summary>
    public sealed record TargetMostModelsDefenseInRange(int MinDefense, int MaxDefense) : CapabilityCondition<IHasTarget>
    {
        protected override bool EvaluateCore(IHasTarget context)
        {
            int low = Math.Min(MinDefense, MaxDefense);
            int high = Math.Max(MinDefense, MaxDefense);
            int living = 0;
            int inRange = 0;
            foreach (IModel model in context.Target.Models)
            {
                if (!model.GetIsAlive())
                {
                    continue;
                }

                living++;
                int defense = Dispatch.HeroStatRules.GetModelDefense(context.Target, model);
                if (defense >= low && defense <= high)
                {
                    inRange++;
                }
            }

            return living > 0 && inRange * 2 > living;
        }
    }

    /// <summary>
    /// #431: true when the target has MORE than half of its total size, or of its Tough value for a single-model
    /// unit - "when attacking units with more than half of their total size or Tough value (for single-model
    /// units)" (OPR 3.6 Decimate, Destructive). Exactly the complement of the morale rules' "half or less"
    /// (<see cref="IUnitExtensions.GetIsAtHalfStrength"/>), so the two can never disagree about a unit: a
    /// multi-model unit counts living models against its starting models (a joined hero is one of them), a
    /// single model counts remaining wounds against its Tough. Read when the attack resolves, before this
    /// attack's own wounds land.
    /// </summary>
    public sealed record TargetAboveHalfStrength : CapabilityCondition<IHasTarget>
    {
        protected override bool EvaluateCore(IHasTarget context) =>
            context.Target.GetIsAlive() && !context.Target.GetIsAtHalfStrength();
    }

    /// <summary>
    /// #431: true when the target is Shaken or Fatigued - "when attacking units that are Shaken or Fatigued"
    /// (OPR 3.6 Purge, Reap). Reads the two status tokens directly. The same pair
    /// <see cref="CountsAsFatiguedInMelee"/> reads on the BEARER, but written out rather than delegated: that
    /// predicate means "counts as Fatigued in melee" and may grow, while this text names exactly two states.
    /// </summary>
    public sealed record TargetShakenOrFatigued : CapabilityCondition<IHasTarget>
    {
        protected override bool EvaluateCore(IHasTarget context) =>
            context.Target.Tokens.HasToken(TokenType.Shaken) || context.Target.Tokens.HasToken(TokenType.Fatigued);
    }

    /// <summary>
    /// #431: true when a strict majority of the TARGET's living models are within <see cref="DistanceInches"/> of
    /// any terrain piece - "when attacking units with most models within 1\" of terrain" (OPR 3.6 Demolish). The
    /// target-side twin of <see cref="MostModelsWithinInchesOfTerrain"/> (which asks about the bearer), on the
    /// same geometry (<see cref="TerrainProximityQueries.MostModelsWithinInches"/>: base edge, living models
    /// only). A context that cannot supply the table's terrain carries an empty list, so the gate
    /// conservatively stays shut there (the AI's volley valuation).
    /// </summary>
    public sealed record TargetMostModelsWithinInchesOfTerrain(float DistanceInches) : Condition
    {
        public override IReadOnlyCollection<Type> RequiredCapabilities => [typeof(IHasTerrain), typeof(IHasTarget)];

        public override bool Evaluate(RuleInvocation invocation)
        {
            if (invocation.Hook is not IHasTerrain terrainContext || invocation.Hook is not IHasTarget target)
            {
                throw new InvalidOperationException(
                    $"{nameof(TargetMostModelsWithinInchesOfTerrain)} requires {nameof(IHasTerrain)} and " +
                    $"{nameof(IHasTarget)}, but the firing context " +
                    $"({invocation.Hook?.GetType().Name ?? "null"}) does not provide both.");
            }

            return TerrainProximityQueries.MostModelsWithinInches(
                target.Target, terrainContext.Terrain, DistanceInches);
        }
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
    /// #422: true when the bearer counts as Fatigued in melee - it carries the Fatigued token, or it is Shaken,
    /// which always counts as Fatigued in melee (#089). Core Rules v3.6.0 gives Impact ("..., unless this unit is
    /// Fatigued"), Counter and Brace the same clause, so each of those entries gates on
    /// <c>Not(CountsAsFatiguedInMelee)</c>; the supplement copies that "count as" them (Heavy Impact, Counter in
    /// Melee, Brace in Melee) carry the same gate. Reads the bearer only, so a Subject-seat entry asks about the
    /// charged unit and an Actor-seat entry about the charger.
    /// <para>
    /// <see cref="Holds"/> is THE predicate: <c>FatigueUtilities.CountsAsFatiguedInMelee</c> (the 6+ threshold,
    /// #418's special-rule sixes, the AI mirrors) delegates to it, so rule data and stage code cannot disagree
    /// about what Fatigued means. It lives here because the Rules layer does not reference the stage layer.
    /// </para>
    /// </summary>
    public sealed record CountsAsFatiguedInMelee : Condition
    {
        public override bool Evaluate(RuleInvocation invocation) => Holds(invocation.Bearer);

        /// <summary> Whether <paramref name="unit"/> counts as Fatigued in melee (Fatigued, or Shaken). </summary>
        public static bool Holds(IUnit unit) =>
            unit.Tokens.HasToken(TokenType.Fatigued) || unit.Tokens.HasToken(TokenType.Shaken);
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
    /// True if the attacking unit moved before this attack (any move action this activation, a 0" Advance
    /// included). Not(AfterMoving) is "using Hold actions" when shooting: Indirect's two facets and Artillery's
    /// +1 (#425) gate on it.
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
