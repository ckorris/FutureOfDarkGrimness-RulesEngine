using System.Linq;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FDG.Rules.Definitions;

/// <summary>
/// What a rule does when its <see cref="Condition"/> matches at a hook.
/// Stored on a <c>HookEntry</c> (passive rules) or on an <c>ActivatedAbility</c>
/// (player-triggered abilities and spells). The engine translates a matched
/// effect into one or more <c>RuleOperation</c> items in the operation queue,
/// which it then applies to game state.
///
/// Closed sum type — abstract record with sealed nested record subtypes, same
/// pattern as <see cref="Condition"/>, <see cref="TokenClearTrigger"/>, and
/// <see cref="Cost"/>. Pattern-match in the effect dispatcher to translate
/// each subtype into the operations it queues.
///
/// Vocabulary grows on demand. Subtypes not exercised by current tests can be
/// left out of the initial commit and added when a rule first needs them.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(StatModifier), "statModifier")]
[JsonDerivedType(typeof(RollModifier), "rollModifier")]
[JsonDerivedType(typeof(Reroll), "reroll")]
[JsonDerivedType(typeof(PassFailedMoraleTest), "passFailedMoraleTest")]
[JsonDerivedType(typeof(AddExtraHit), "addExtraHit")]
[JsonDerivedType(typeof(AddExtraWound), "addExtraWound")]
[JsonDerivedType(typeof(AddBonusAttack), "addBonusAttack")]
[JsonDerivedType(typeof(SelfWoundOnUnmodifiedRoll), "selfWoundOnUnmodifiedRoll")]
[JsonDerivedType(typeof(MovementBonus), "movementBonus")]
[JsonDerivedType(typeof(IgnoreRule), "ignoreRule")]
[JsonDerivedType(typeof(AddRule), "addRule")]
[JsonDerivedType(typeof(Aura), "aura")]
[JsonDerivedType(typeof(DealHits), "dealHits")]
[JsonDerivedType(typeof(AttackWithThisWeapon), "attackWithThisWeapon")]
[JsonDerivedType(typeof(DealAutoWounds), "dealAutoWounds")]
[JsonDerivedType(typeof(StormOfHits), "stormOfHits")]
[JsonDerivedType(typeof(DealPooledHits), "dealPooledHits")]
[JsonDerivedType(typeof(Heal), "heal")]
[JsonDerivedType(typeof(GrantToken), "grantToken")]
[JsonDerivedType(typeof(GrantTokenToKiller), "grantTokenToKiller")]
[JsonDerivedType(typeof(GrantIgnoredWoundMarker), "grantIgnoredWoundMarker")]
[JsonDerivedType(typeof(ConsumeToken), "consumeToken")]
[JsonDerivedType(typeof(ClearTokenOnRoll), "clearTokenOnRoll")]
[JsonDerivedType(typeof(GrantTokenOnRoll), "grantTokenOnRoll")]
[JsonDerivedType(typeof(RepositionAtActivation), "repositionAtActivation")]
[JsonDerivedType(typeof(RepositionOnDeploy), "repositionOnDeploy")]
[JsonDerivedType(typeof(TriggeredMove), "triggeredMove")]
[JsonDerivedType(typeof(Reactivate), "reactivate")]
[JsonDerivedType(typeof(MultiplyWounds), "multiplyWounds")]
[JsonDerivedType(typeof(QualityFloor), "qualityFloor")]
[JsonDerivedType(typeof(IgnoreWoundOnRoll), "ignoreWoundOnRoll")]
[JsonDerivedType(typeof(SetMaxWounds), "setMaxWounds")]
[JsonDerivedType(typeof(SetDefense), "setDefense")]
[JsonDerivedType(typeof(MultiplyHits), "multiplyHits")]
[JsonDerivedType(typeof(ChargeImpactHits), "chargeImpactHits")]
// #422: 3.5 Counter's impact facet. The kind still parses (old army files and not-yet-rebaked books embed it)
// and does nothing - see LegacyReduceImpactDicePerModel.
[JsonDerivedType(typeof(LegacyReduceImpactDicePerModel), "reduceImpactDicePerModel")]
[JsonDerivedType(typeof(CancelImpactDice), "cancelImpactDice")]
// #423: Parry(X) (Core Rules v3.6.0) - the defender's hit-ignore dice when it takes hits in melee.
[JsonDerivedType(typeof(ParryHits), "parryHits")]
[JsonDerivedType(typeof(ExtraMeleeWoundCount), "extraMeleeWoundCount")]
[JsonDerivedType(typeof(StrikeFirst), "strikeFirst")]
[JsonDerivedType(typeof(StrikeLast), "strikeLast")]
[JsonDerivedType(typeof(ShootAfterRush), "shootAfterRush")]
// #425: Cumbersome's "can't shoot when moving" (Core Rules v3.6.0).
[JsonDerivedType(typeof(NoShootingAfterMoving), "noShootingAfterMoving")]
[JsonDerivedType(typeof(EnableCasting), "enableCasting")]
[JsonDerivedType(typeof(EnableTransport), "enableTransport")]
[JsonDerivedType(typeof(EnableReDeployment), "enableReDeployment")]
// #424: weapon roles (Assault) - the weapon-scoped members of the capability family.
[JsonDerivedType(typeof(EnableMeleeUse), "enableMeleeUse")]
[JsonDerivedType(typeof(EnableRangedUse), "enableRangedUse")]
[JsonDerivedType(typeof(EnableSpellLending), "enableSpellLending")]
[JsonDerivedType(typeof(EnableSpellRelay), "enableSpellRelay")]
[JsonDerivedType(typeof(EnableBuffRelay), "enableBuffRelay")]
[JsonDerivedType(typeof(CompelClosestTarget), "compelClosestTarget")]
[JsonDerivedType(typeof(RepelAmbushers), "repelAmbushers")]
[JsonDerivedType(typeof(AmbushBeacon), "ambushBeacon")]
[JsonDerivedType(typeof(AmbushRedeploy), "ambushRedeploy")]
[JsonDerivedType(typeof(SpawnUnit), "spawnUnit")]
[JsonDerivedType(typeof(ReinforceUnit), "reinforceUnit")]
[JsonDerivedType(typeof(RestoreWounds), "restoreWounds")]
[JsonDerivedType(typeof(TargetIndividualModel), "targetIndividualModel")]
[JsonDerivedType(typeof(RestrictActions), "restrictActions")]
[JsonDerivedType(typeof(RangeModifier), "rangeModifier")]
[JsonDerivedType(typeof(IgnoreTerrainEffects), "ignoreTerrainEffects")]
[JsonDerivedType(typeof(IgnoreEnemyMovementBlock), "ignoreEnemyMovementBlock")]
[JsonDerivedType(typeof(IgnoreCover), "ignoreCover")]
[JsonDerivedType(typeof(IgnoreLineOfSight), "ignoreLineOfSight")]
// #425: Indirect 3.6 "ignores Cover from Sight Blockers" (Hold only) - not all cover, unlike IgnoreCover.
[JsonDerivedType(typeof(IgnoreSightBlockerCover), "ignoreSightBlockerCover")]
[JsonDerivedType(typeof(DeferDeployment), "deferDeployment")]
[JsonDerivedType(typeof(Disembark), "disembark")]
[JsonDerivedType(typeof(Embark), "embark")]
[JsonDerivedType(typeof(Teleport), "teleport")]
[JsonDerivedType(typeof(MoraleTestThen), "moraleTestThen")]
[JsonDerivedType(typeof(ApplyFatigue), "applyFatigue")]
[JsonDerivedType(typeof(MarkTarget), "markTarget")]
[JsonDerivedType(typeof(ReduceArmorPenetration), "reduceArmorPenetration")]
[JsonDerivedType(typeof(TokenScaledRollModifier), "tokenScaledRollModifier")]
[JsonDerivedType(typeof(TokenScaledReduceArmorPenetration), "tokenScaledReduceArmorPenetration")]
[JsonDerivedType(typeof(CountAsInTerrain), "countAsInTerrain")]
[JsonDerivedType(typeof(DangerousTerrainTest), "dangerousTerrainTest")]
[JsonDerivedType(typeof(PerHitSaveModifier), "perHitSaveModifier")]
[JsonDerivedType(typeof(ExtraAttack), "extraAttack")]
[JsonDerivedType(typeof(ActivateUnitNext), "activateUnitNext")]
// #431: OPR 3.6 Pierce's "ignores all positive Defense modifiers" and Scourge's counted (not dealt) wounds.
[JsonDerivedType(typeof(IgnorePositiveDefenseModifiers), "ignorePositiveDefenseModifiers")]
[JsonDerivedType(typeof(CountsAsWoundsTaken), "countsAsWoundsTaken")]
// #427: OPR 3.6 Unstoppable's "ignores all negative modifiers to this weapon which are not from this model's special rules".
[JsonDerivedType(typeof(IgnoreNegativeWeaponModifiers), "ignoreNegativeWeaponModifiers")]

public abstract record Effect
{
    public virtual void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
    {
        throw new NotImplementedException("Effect is not yet applyable.");
    }
    
    public virtual IReadOnlyCollection<Type> RequiredCapabilities => Array.Empty<Type>();
    
    /// <summary>
    /// Grants the target a numeric modifier of <see cref="Delta"/> to a specific roll
    /// (<see cref="Roll"/>) for the duration given by <see cref="LifetimeScope"/> — the spell/ability
    /// "+1 to hit" / "-1 to defense rolls" / "-1 to morale" / "-1 to casting rolls" buff or debuff
    /// (#033; the cast kind is #197 P6's Casting Debuff / Casting Buff). Unlike
    /// <see cref="Effect.RollModifier"/> (a passive, bearer-side modifier on one roll the bearer is
    /// currently making), this is GRANTED to the target as a token the relevant roll stage reads later,
    /// persisting until its lifetime expires (NextTrigger → consumed on the next such roll; ThisRound →
    /// swept at round end). The roll lives in the token type, so different rolls' grants never merge.
    /// </summary>
    public sealed record StatModifier(ERollKind Roll, int Delta, ELifetime LifetimeScope) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.GrantTokenToUnit(
                ruleInvocation.EffectiveTarget,
                new Token(RollModifierTokens.TypeFor(Roll), 1, ClearTriggerFor(LifetimeScope),
                    Payload: new TokenPayload.StatModifier(Delta),
                    OwnerUnitID: ruleInvocation.OwnerForEffectiveTarget)));
        }
    }

    /// <summary>
    /// Adds or subtracts <see cref="Delta"/> to a single dice roll of the given
    /// <see cref="RollKind"/>. The most common effect: Stealth -1 to hit,
    /// Courage +1 to morale, AP(X) -X to enemy defense, etc. Applies only to
    /// the one roll in flight at the firing hook.
    /// </summary>
    public sealed record RollModifier(ERollKind RollKind, int Delta) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.ApplyRollModifier(RollKind, Delta));
        }
    }

    /// <summary>
    /// AP that lands only on the hits that rolled an unmodified <see cref="OnRollValue"/> — Rending /
    /// Crack promoting AP on a natural 6, modelled as <see cref="Delta"/> to the defender's save. Unlike
    /// <see cref="RollModifier"/> (which carries a whole-attack save modifier applied to every hit),
    /// this tags only the matching-face hits: the hit-roll stage peels those hits into their own group
    /// carrying <see cref="Delta"/> while the rest save at base AP. The stage reads the roll histogram,
    /// so the matching-face count stays correct — and fractional — under the probabilistic roller.
    /// <para>
    /// #418: when the firing context carries hit rolls and none of them count as
    /// <see cref="OnRollValue"/> for special rules (<see cref="IHasUnmodifiedHitRolls.SpecialRuleHitRolls"/>
    /// - a fatigued melee attack's 6s), nothing is emitted, so no AP group, no "Rending AP+4" proc chip.
    /// Kept a plain <c>Effect</c> rather than a <c>CapabilityEffect</c>, so the validator's requirements for
    /// it are unchanged; off a hit-roll context it emits exactly as before.
    /// </para>
    /// </summary>
    public sealed record PerHitSaveModifier(int OnRollValue, int Delta) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            if (ruleInvocation.Hook is IHasUnmodifiedHitRolls rolls
                && OnRollValue >= rolls.SpecialRuleHitRolls.SideMin
                && OnRollValue <= rolls.SpecialRuleHitRolls.SideMax
                && rolls.SpecialRuleHitRolls.At(OnRollValue) <= 0f)
            {
                return;
            }

            operations.Add(new RuleOperation.ApplyPerHitSaveModifier(OnRollValue, Delta));
        }
    }

    /// <summary>
    /// A failed morale test counts as passed instead, and the unit then rolls as many dice as the wounds
    /// it would take to destroy it, taking one UNIGNORABLE wound per result at or below
    /// <see cref="SelfWoundOnRollAtMost"/>. Covers #197 P7's No Retreat family.
    /// <para>
    /// Authored at <see cref="EHookID.Morale_OnMoraleTestComplete"/>. The wounds bypass Regeneration and
    /// every other wound-ignore by construction: they are applied straight through the casualty seam
    /// rather than through the save/ignore pipeline, exactly as dangerous terrain's are.
    /// </para>
    /// </summary>
    public sealed record PassFailedMoraleTest(int SelfWoundOnRollAtMost) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.PassMoraleTest(SelfWoundOnRollAtMost));
        }
    }

    /// <summary>
    /// Forces a reroll of dice in the current roll matching
    /// <see cref="Condition"/>. Covers Bane (reroll unmodified Defense 6s),
    /// Lacerate (reroll same on attacker side), Fearless (reroll failed morale
    /// on 4+).
    /// </summary>
    public sealed record Reroll(ERollKind Roll, RerollCondition Condition) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.ApplyReroll(Roll, Condition));
        }
    }

    /// <summary>
    /// Each die that came up unmodified <see cref="OnRollValue"/> generates
    /// <see cref="Count"/> additional hits (defaults to one). Covers Furious,
    /// Surge, Relentless — all the "natural 6 → bonus hit" rules. Counts the dice in
    /// <see cref="IHasUnmodifiedHitRolls.SpecialRuleHitRolls"/>, so a fatigued melee attack's 6s add
    /// nothing (#418). The generated hits are not rolls at all, so they never trigger anything themselves
    /// ("only the original hit counts as a 6 for special rules").
    /// </summary>
    public sealed record AddExtraHit(int OnRollValue, int Count = 1) : CapabilityEffect<IHasUnmodifiedHitRolls>
    {
        protected override void ApplyCore(IHasUnmodifiedHitRolls context, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.InsertExtraHits(context.SpecialRuleHitRolls.At(OnRollValue) * Count));
        }
    }

    /// <summary>
    /// The attacker takes <see cref="Count"/> wounds for each of its own dice that came up an
    /// unmodified <see cref="OnRollValue"/> — the punitive mirror of <see cref="Effect.AddExtraHit"/>,
    /// reading the same hit histogram. Covers Hazardous: #197's 3.5 "this weapon's unit takes one wound on
    /// unmodified rolls of 1 to hit", and #434's 3.6 "on unmodified results of 1 to hit this model takes one
    /// wound, or X wounds if this weapon has the Blast(X)/Deadly(X) rule".
    /// <para>
    /// The wounds land after the attack fully resolves and cannot be saved or ignored — see
    /// <c>CasualtyPresentation.ApplyUnitWounds</c> for both rulings. #434: they land on the MODELS that fired the
    /// weapon (<c>CasualtyPresentation.ApplyCarrierWounds</c>), each die's wounds on one model with any excess lost,
    /// and <see cref="CountFromBlastOrDeadly"/> makes a die worth the firing weapon's Blast(X) / Deadly(X) (the larger
    /// when both; <see cref="Count"/> when neither). False (the default, what old data parses to) keeps Count.
    /// </para>
    /// </summary>
    public sealed record SelfWoundOnUnmodifiedRoll(int OnRollValue, int Count = 1, bool CountFromBlastOrDeadly = false)
        : Effect
    {
        public override IReadOnlyCollection<Type> RequiredCapabilities => [typeof(IHasUnmodifiedHitRolls)];

        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            if (ruleInvocation.Hook is not IHasUnmodifiedHitRolls context)
            {
                throw new InvalidOperationException(
                    $"{nameof(SelfWoundOnUnmodifiedRoll)} requires {nameof(IHasUnmodifiedHitRolls)}, but the firing " +
                    $"context ({ruleInvocation.Hook?.GetType().Name ?? "none"}) does not provide it.");
            }

            int perDie = CountFromBlastOrDeadly ? BlastOrDeadlyValue(ruleInvocation.Weapon) ?? Count : Count;

            // The special-rule view, like every other natural-N trigger; it only differs from the raw roll
            // on a fatigued melee attack's 6s (#418), so Hazardous's 1s are unaffected.
            float dice = context.SpecialRuleHitRolls.At(OnRollValue);
            operations.Add(new RuleOperation.InflictSelfWounds(dice * perDie, WoundsPerDie: perDie));
        }

        /// <summary>The firing weapon's Blast(X) / Deadly(X) value (the larger when it carries both), or null.</summary>
        private static int? BlastOrDeadlyValue(IWeapon? weapon)
        {
            if (weapon == null) return null;
            int? best = null;
            foreach (Dispatch.ResolvedRule rule in weapon.RuleDefinitions)
            {
                string name = rule.Definition.Name;
                if (!string.Equals(name, "Blast", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(name, "Deadly", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (RuleArgument argument in rule.Arguments)
                {
                    if (argument is RuleArgument.Int x && (best == null || x.Value > best)) best = x.Value;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// Same shape as <see cref="Effect.AddExtraHit"/> but for wound generation, reading the SAVE-roll
    /// histogram instead of the hit-roll one. Each of the defender's unmodified <see cref="OnRollValue"/>
    /// save rolls generates <see cref="Count"/> extra wounds. Covers Shred (+1 wound on an unmodified 1
    /// to block) — the wound-side mirror of Surge.
    /// </summary>
    public sealed record AddExtraWound(int OnRollValue, int Count = 1)
        : CapabilityEffect<IHasUnmodifiedSaveRolls>
    {
        protected override void ApplyCore(IHasUnmodifiedSaveRolls context, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.InsertExtraWounds(context.UnmodifiedSaveRolls.At(OnRollValue) * Count));
        }
    }

    /// <summary>
    /// #431 Scourge: "For each unmodified result of 1 to block hits, enemies count as having taken 2 extra
    /// wounds when checking who won melee, or if shooting wounds would trigger Morale Tests." Each of the
    /// defender's unmodified <see cref="OnRollValue"/> block rolls makes it COUNT as having taken
    /// <see cref="WoundsPerDie"/> more wounds - the same histogram read as <see cref="AddExtraWound"/> (Shred),
    /// but nothing is dealt: no model loses a wound, nothing is saved or regenerated.
    /// <para>
    /// The emitted <see cref="RuleOperation.AddCountedWoundsTaken"/> is folded by <c>AssignWoundsStage</c> into
    /// one <see cref="Foundation.TokenType.CountedWoundsTaken"/> magnitude on the defender
    /// (<see cref="Dispatch.CountedWounds"/>), read and removed by the two consumers the text names:
    /// <c>DetermineMeleeWinnerStage</c> (added to the struck unit's taken total, so the striker's side wins
    /// ties it would otherwise lose - the same "enemies count as having taken" polarity as Fear(X)) and the
    /// wound-driven morale check of a shooting attack
    /// (<c>ResolveRangedMoraleStage</c>, and <c>ResolveStrafeMoraleStage</c> for "as if shooting" attacks),
    /// where the counted wounds are allocated virtually for the half-strength test and alone can trigger it.
    /// Defense rolls are not hit rolls, so #418's Fatigue view does not apply.
    /// </para>
    /// </summary>
    public sealed record CountsAsWoundsTaken(int OnRollValue = 1, int WoundsPerDie = 2)
        : CapabilityEffect<IHasUnmodifiedSaveRolls>
    {
        protected override void ApplyCore(IHasUnmodifiedSaveRolls context, List<RuleOperation> operations)
        {
            float counted = context.UnmodifiedSaveRolls.At(OnRollValue) * WoundsPerDie;
            if (counted > 0f)
            {
                operations.Add(new RuleOperation.AddCountedWoundsTaken(counted));
            }
        }
    }

    /// <summary>
    /// Each of the defender's unmodified <see cref="OnRollValue"/> BLOCK rolls grants the attacker
    /// <see cref="Count"/> follow-up attacks with the weapon whose hits were blocked (#376
    /// Bloodthirsty Fighter). Same histogram read as <see cref="AddExtraWound"/>, but the output is
    /// new ATTACK dice, not wounds: the melee swing chain runs them as a real child batch (hit ->
    /// save -> wound, the weapon's own rules included) via <c>ResolveBonusMeleeAttacksStage</c>, which
    /// refuses to trigger from a bonus batch's own blocks - "this rule doesn't apply to newly
    /// generated attacks". SUPPORTED PATHS (the <see cref="DealAutoWounds"/> doctrine): only the melee
    /// swing chain consumes the emitted op; gate the entry on <see cref="Condition.IsMelee"/> - at any
    /// other pipeline (shooting, impact hits, Ravage's synthetic attack) it is silently dropped.
    /// </summary>
    public sealed record AddBonusAttack(int OnRollValue = 1, int Count = 1)
        : CapabilityEffect<IHasUnmodifiedSaveRolls>
    {
        protected override void ApplyCore(IHasUnmodifiedSaveRolls context, List<RuleOperation> operations)
        {
            float total = context.UnmodifiedSaveRolls.At(OnRollValue) * Count;
            if (total > 0f)
            {
                operations.Add(new RuleOperation.AddBonusAttacks(total));
            }
        }
    }

    /// <summary>
    /// Adjusts the bearer's movement distance by <see cref="DistanceInches"/>
    /// (positive or negative) when the action being taken is
    /// <see cref="ActionType"/>. Covers Fast (+2"/Advance, +4"/Rush+Charge),
    /// Slow, Rapid Rush (+6"/Rush), and target-perspective movement penalties
    /// like Melee Shrouding (-3" on enemy Charge). <see cref="MinResultInches"/>
    /// floors the resulting distance — a reduction can't drop it below this (Melee
    /// Shrouding's "to a min. of 6\""); 0 means no floor. Read by the charge-penalty
    /// query (<see cref="Dispatch.MovementRuleQueries.EffectiveChargeDistanceAgainst"/>);
    /// the Actor-seat sink path (Fast/Slow) only sums <see cref="DistanceInches"/>.
    /// </summary>
    public sealed record MovementBonus(EActionType ActionType, float DistanceInches, float MinResultInches = 0f) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.ApplyMovementBonus(ActionType, DistanceInches, MinResultInches));
        }
    }

    /// <summary>
    /// Suppresses another rule named <see cref="RuleName"/> in the current
    /// evaluation chain — removes its effects from the operation queue before
    /// the engine applies them. Covers Bane / Smash / Unstoppable (ignore
    /// Regeneration). This is the rule-vs-rule primitive that lets Plan B
    /// express "X counters Y" without engine code.
    /// </summary>
    public sealed record IgnoreRule(string RuleName) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.SuppressRule(RuleName));
        }
    }

    /// <summary>
    /// Grants the bearer the rule named <see cref="RuleName"/> for the duration
    /// of <see cref="Scope"/>. Typical use: spell-applied "next time" buffs
    /// (Blessed Ammo, Fade in the Dark, Protective Dome) with
    /// <see cref="ELifetime.NextTrigger"/>; activation-scoped grants
    /// (Versatile Attack/Reach) with <see cref="ELifetime.ThisActivation"/>.
    /// Distinct from <see cref="Effect.Aura"/>, which grants to all unit-mates.
    /// </summary>
    public sealed record AddRule(string RuleName, ELifetime Scope) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.GrantTokenToUnit(
                ruleInvocation.EffectiveTarget,
                new Token(TokenType.RuleGrant, 1, ClearTriggerFor(Scope),
                    Payload: new TokenPayload.RuleGrant(RuleName, Scope),
                    OwnerUnitID: ruleInvocation.OwnerForEffectiveTarget)));
        }
    }

    /// <summary>
    /// Maps a rule-effect <see cref="ELifetime"/> onto the token clear trigger that
    /// realizes it, for effects (<see cref="AddRule"/>) that persist by granting a
    /// <see cref="TokenType.RuleGrant"/> token. Aura / until-end-of-game grants never
    /// auto-clear (<see cref="TokenClearTrigger.ManualOnly"/>).
    /// </summary>
    private static TokenClearTrigger ClearTriggerFor(ELifetime lifetime) => lifetime switch
    {
        ELifetime.NextTrigger => new TokenClearTrigger.FirstTrigger(),
        ELifetime.ThisActivation => new TokenClearTrigger.ActivationEnd(),
        ELifetime.ThisRound => new TokenClearTrigger.RoundEnd(),
        ELifetime.ThisAttack => new TokenClearTrigger.AttackEnd(),
        // No dedicated trigger variant: "until my next activation" is exactly "clears when
        // Activation_OnActivationStart fires for me", which CustomHook already expresses and
        // TokenClearService.ClearsAtHook already sweeps. ActivationStartStage does the sweeping.
        ELifetime.UntilNextActivation =>
            new TokenClearTrigger.CustomHook(EHookID.Activation_OnActivationStart),
        _ => new TokenClearTrigger.ManualOnly(),
    };

    /// <summary>
    /// Grants the rule named <see cref="RuleName"/> to every model in the
    /// bearer's unit for as long as the bearer remains alive. Distinct from
    /// <see cref="Effect.AddRule"/> because aura propagation is unit-wide and tied to
    /// bearer existence rather than a named lifetime. Covers Regeneration
    /// Aura, Furious Aura, Melee Shrouding Aura, etc.
    /// </summary>
    public sealed record Aura(string RuleName) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.GrantTokenToUnit(
                ruleInvocation.Bearer, new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.ManualOnly(),
                    Payload: new TokenPayload.RuleGrant(RuleName, ELifetime.Aura))));
        }
    }

    /// <summary>
    /// Inflicts <see cref="Count"/> hits on the target unit at the given <see cref="ArmorPenetration"/>,
    /// with each hit carrying the additional weapon rules named in <see cref="WithRules"/> (e.g.
    /// <c>Blast(3)</c>, <c>Bane</c>, <c>Lacerate</c>). The universal offensive-spell shape: Cerebral Trauma
    /// (1 hit with Blast and Lacerate), Lightning Fog (4 hits), Psychic Terror (9 hits with Bane), Total
    /// Seizure (4 hits with AP(1)). <see cref="ArmorPenetration"/> is a separate field rather than a
    /// <see cref="WithRules"/> entry because AP is a weapon stat, not a #042 rule — it sets the synthetic
    /// spell weapon's AP directly. <see cref="Count"/> is a fixed authored value because every
    /// offensive-spell hit-count in the corpus is fixed — no rule-data randomness on the count itself.
    ///
    /// SUPPORTED PATHS (not universal): the emitted <see cref="RuleOperation.InvokeDealHits"/> is only
    /// executed by the two stages wired for it — spells (CastSpellStage) and before-attack abilities
    /// (BeforeAttackActionStage). It is NOT an <see cref="ExecutableOperation"/>, so any other hook's
    /// generic op application silently drops it. <see cref="WithRules"/> is honored only on the spell path
    /// (pre-resolved at army load); before-attack warns and skips it. (StrafingStage was a third path until
    /// #197 re-authored Strafing onto <see cref="AttackWithThisWeapon"/> — a mid-move fixed-hit-count
    /// ability now has no stage to run it, and the fire-lint reports one as unhandled.)
    /// </summary>
    public sealed record DealHits(int Count, IReadOnlyList<string> WithRules, int ArmorPenetration = 0) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.InvokeDealHits(
                ruleInvocation.EffectiveTarget, Count, WithRules, ArmorPenetration));
        }
    }

    /// <summary>
    /// #197 Strafing — attacks the target with the WEAPON THAT CARRIES THE RULE, as if it were shooting:
    /// "pick one of them and attack it with this weapon as if it was shooting". Unlike
    /// <see cref="DealHits"/>, which delivers a fixed hit count on a synthetic weapon, this carries no
    /// payload at all — the weapon's own profile (Attacks, AP, Blast, Deadly, Shred, ...) and the bearer's
    /// Quality supply everything, because the corpus rule adds nothing of its own.
    ///
    /// Only meaningful on a WEAPON-scoped rule: the weapon travels on
    /// <see cref="RuleInvocation.Weapon"/>, which <c>RuleEvaluator.ResolveAbility</c> fills from the
    /// offering weapon. A unit-scoped rule using this effect has no weapon to attack with, so the emitted
    /// operation carries a null one and the resolving stage warns and skips rather than guessing which of
    /// the bearer's weapons was meant.
    ///
    /// SUPPORTED PATHS (not universal, like <see cref="DealHits"/>): the emitted
    /// <see cref="RuleOperation.InvokeWeaponAttack"/> is a plain <see cref="RuleOperation"/> enacted only by
    /// StrafingStage, whose child chain IS the shooting pipeline. It is not an
    /// <see cref="ExecutableOperation"/> for the same reason DealHits isn't: the hit/save/wound resolution
    /// is a child-stage chain, which only sequences correctly as a real child of the movement stage.
    /// </summary>
    public sealed record AttackWithThisWeapon : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.InvokeWeaponAttack(
                ruleInvocation.EffectiveTarget, ruleInvocation.Weapon));
        }
    }

    /// <summary>
    /// #197 P16 — injects ONE extra attack, made with a weapon profile the rule itself authors, into the
    /// action the bearer is already taking. Covers Takedown Strike ("once per game, when it's this model's
    /// turn to attack in melee, it may make one attack at Quality 2+ with AP(2), Deadly(3), and Takedown")
    /// and Takedown Shot (the same, "when this model shoots ... against the target").
    ///
    /// <para>The profile is the whole payload: <see cref="Attacks"/> dice at <see cref="ArmorPenetration"/>,
    /// carrying <see cref="WithRules"/> - and every rider those two rules state is ALREADY a weapon rule, so
    /// none of them needs vocabulary of its own. "At Quality 2+" is <c>Reliable</c>
    /// (<see cref="QualityFloor"/>, folded by minimum, so a worse-shooting bearer is lifted to 2+ and a 2+
    /// bearer is unchanged); "Deadly(3)" and "Takedown" are themselves. Names carry their arguments, parsed
    /// the way a spell's <see cref="DealHits.WithRules"/> are.</para>
    ///
    /// <para>Distinct from <see cref="AttackWithThisWeapon"/> (Strafing), which attacks with a weapon the
    /// bearer actually carries, and from <see cref="DealHits"/>, which delivers a fixed hit count with no
    /// roll to hit. This one rolls to hit, so the attack is a real attack: its hits fold through Rending /
    /// Surge, its wounds through Deadly and Tough, and its Takedown pick confines them to one model.</para>
    ///
    /// <para>SUPPORTED PATHS (not universal, like <see cref="DealHits"/>): the emitted
    /// <see cref="RuleOperation.InvokeExtraAttack"/> is a plain <see cref="RuleOperation"/>, enacted only by
    /// <c>ResolveExtraAttackStage</c> at <see cref="EHookID.Combat_OnAttackWindow"/>, because the
    /// hit/save/wound resolution is a child-stage chain. Any other hook's generic op application drops it,
    /// and <c>RuleFireLint</c> reports it as unhandled there.</para>
    /// </summary>
    public sealed record ExtraAttack(string WeaponName, int Attacks, int ArmorPenetration,
        IReadOnlyList<string> WithRules) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.InvokeExtraAttack(ruleInvocation.EffectiveTarget,
                WeaponName, Attacks, ArmorPenetration, WithRules));
        }
    }

    /// <summary>
    /// #197 P10 — rolls a pool of dice against the target and deals one DIRECT wound per success (each
    /// result >= <see cref="SuccessThreshold"/>). Covers Ravage(X) ("roll X dice; for each 6+ the target
    /// takes one wound") and Crossing Attack(X). Unlike <see cref="DealHits"/>, the wounds bypass the
    /// armor save entirely — the resolving stage skips the save roll — but remain subject to the defender's
    /// wound-ignore rolls (Regeneration) and Tough allocation, matching the rulebook "takes a wound".
    ///
    /// <see cref="DiceCountPerModel"/> is the rule's per-model X (an <see cref="ValueSource.Arg"/> for the
    /// numeric rating); the effect multiplies it by the number of living models in the bearer's unit that
    /// carry this rule (weapon-aware, like <see cref="CancelImpactDice"/>), because the source
    /// text is per model ("when it is THIS MODEL's turn to attack, roll X dice"). There is no output
    /// attribution to solve — the wounds land on the enemy, not a specific attacker die — so a single pool
    /// summed across carriers is exact.
    ///
    /// SUPPORTED PATHS (not universal, like <see cref="DealHits"/>): the emitted
    /// <see cref="RuleOperation.InvokeDealAutoWounds"/> is a plain <see cref="RuleOperation"/>, enacted only
    /// by the stages wired for it — <c>ResolveRavageWoundsStage</c> (melee charge-contact, this slice) and
    /// the Crossing Attack movement path. Any other hook's generic op application silently drops it.
    ///
    /// <para>#434: the carriers are counted per ATTACHMENT, by #447's <see cref="ChargeImpactHits.CountCarriers"/>:
    /// a unit-attached Ravage counts the living native models (a joined hero does not inherit it), a joined hero's
    /// own copy counts the hero, a token grant (an aura) counts every living model, and a weapon-carried copy
    /// counts the models wielding a weapon with THAT attachment (a resumed game's one attachment per weapon no
    /// longer multiplies it). <see cref="PerModel"/> false skips the multiplication for a pool the bearer already
    /// holds as a whole (3.6 Regenerative Strength: "roll as many dice as tokens on it").</para>
    /// </summary>
    public sealed record DealAutoWounds(ValueSource DiceCountPerModel, int SuccessThreshold = 6,
        bool PerModel = true) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            // Per-model X: multiply the rating by the living models that actually carry this copy (Brace's and
            // Impact's accounting), so a monster's unit-scoped Ravage scales by 1 and a squad's by its size.
            int carriers = PerModel ? ChargeImpactHits.CountCarriers(ruleInvocation) : 1;

            int diceCount = DiceCountPerModel.Resolve(ruleInvocation) * carriers;
            if (diceCount <= 0)
            {
                return;
            }

            // #436 (from #442): a passive Ravage has no explicit Target, so EffectiveTarget is the bearer and the
            // narration read "Tribe Master's Ravage(6) rolled 6 dice at Tribe Master". At the strike hook the
            // context names the unit being struck (ResolveRavageWoundsStage puts the striker in Attacker in both
            // its placements); the stage itself always wounded the right unit - this is the op's label only.
            IUnit target = ruleInvocation.Target
                ?? (ruleInvocation.Hook is Dispatch.Contexts.ChargeContactContext contact
                    && contact.Defender.ID != ruleInvocation.Bearer.ID
                        ? contact.Defender
                        : ruleInvocation.EffectiveTarget);
            operations.Add(new RuleOperation.InvokeDealAutoWounds(target, diceCount, SuccessThreshold));
        }
    }

    /// <summary>
    /// #197 P10 Storm of X — a rolled, multi-target hit burst. Once activated, roll <see cref="PoolDice"/>
    /// dice; for each result >= <see cref="SuccessThreshold"/> the player picks one enemy unit within
    /// <see cref="RangeInches"/> that takes <see cref="HitsPerSuccess"/> hits carrying <see cref="WithRules"/>
    /// (Shred / Surge / Bane) at <see cref="ArmorPenetration"/>. Covers Storm of Change/Lust/Plague/War.
    ///
    /// The effect carries only the CONFIG - it emits a single <see cref="RuleOperation.InvokeStorm"/>. The
    /// pool roll, the per-success target picks, and the per-target hit batches are enacted stage-side by
    /// StormStage, because (a) the pool must be rolled DECISIVELY so the number of target picks is a whole
    /// number even under the probabilistic roller (you cannot pick a fractional number of targets - the
    /// #100 dice invariant, resolved as for P15's branch die), and (b) each target's hits run the save/wound
    /// child-stage pipeline, which only sequences correctly as a real stage loop.
    /// </summary>
    public sealed record StormOfHits(int PoolDice, int SuccessThreshold, int HitsPerSuccess,
        IReadOnlyList<string> WithRules, int ArmorPenetration, float RangeInches) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.InvokeStorm(PoolDice, SuccessThreshold, HitsPerSuccess,
                WithRules, ArmorPenetration, RangeInches));
        }
    }

    /// <summary>
    /// #197 Surprise Attack — rolls a pool of dice against one chosen target and turns each success (a
    /// result >= <see cref="SuccessThreshold"/>) into one HIT at <see cref="ArmorPenetration"/>. Covers
    /// "pick one enemy unit within 6in in line of sight, and roll X dice. For each 2+ it takes one hit
    /// with AP(1)."
    ///
    /// The hit-pool sibling of <see cref="DealAutoWounds"/>: where that effect's successes are DIRECT
    /// wounds that skip the armor save, these are ordinary hits and run the full save -> wound pipeline.
    /// And unlike <see cref="StormOfHits"/> — whose pool must be rolled DECISIVELY because each success
    /// picks a target, and you cannot pick a fractional target — the successes here are a hit COUNT, so
    /// they stay fractional under the probabilistic roller (the #100 dice invariant).
    ///
    /// <see cref="DiceCount"/> is the rule's X (a <see cref="ValueSource.Arg"/> for the numeric rating).
    /// The range and the line-of-sight requirement ride the ability's own <c>TargetSelector</c> rather
    /// than this effect, which carries only the pool itself. There is deliberately no <c>WithRules</c>
    /// list: no corpus pool pairs its hits with Shred/Blast/Surge today — add one here, as
    /// <see cref="DealHits"/> has, when one does.
    ///
    /// SUPPORTED PATHS (not universal, like <see cref="DealHits"/>): the emitted
    /// <see cref="RuleOperation.InvokeDealPooledHits"/> is a plain <see cref="RuleOperation"/>, enacted
    /// only by <c>SurpriseAttackStage</c> at <see cref="EHookID.Activation_OnActivationStart"/>, because
    /// the hits resolve through a child-stage chain. Any other hook's generic op application drops it,
    /// and <c>RuleFireLint</c> reports it as unhandled there.
    /// </summary>
    public sealed record DealPooledHits(ValueSource DiceCount, int SuccessThreshold, int ArmorPenetration)
        : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            int diceCount = DiceCount.Resolve(ruleInvocation);
            if (diceCount <= 0)
            {
                return;
            }

            operations.Add(new RuleOperation.InvokeDealPooledHits(ruleInvocation.EffectiveTarget,
                diceCount, SuccessThreshold, ArmorPenetration));
        }
    }

    /// <summary>
    /// Removes <see cref="Amount"/> wounds from the target model. The only
    /// rule-data effect with a genuinely random amount — Mend uses D3 — hence
    /// the <see cref="DiceExpression"/> parameter rather than a fixed int.
    /// </summary>
    public sealed record Heal(DiceExpression Amount) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            // Roll the heal die now and reduce the per-face histogram to a scalar pip
            // total (Σ face·count) — fractional under the probabilistic roller, hence
            // InvokeHeal.Amount is a float. Model selection ("most-wounded first") is
            // Phase 8; queue-level just needs a model.
            IDiceResults results = ruleInvocation.DiceRoller!.Roll(Amount.Sides, 1f);
            float amount = 0f;
            for (int face = results.SideMin; face <= results.SideMax; face++)
            {
                amount += face * results.At(face);
            }

            operations.Add(new RuleOperation.InvokeHeal(ruleInvocation.EffectiveTarget.Models.First(), amount));
        }
    }

    /// <summary>
    /// Adds <see cref="Count"/> tokens of <see cref="TType"/> to the bearer's
    /// container with the specified <see cref="Clear"/> policy. Covers cost-
    /// gate setup ("used-this-activation" markers cleared at
    /// <see cref="EHookID.Activation_OnEndOfActivation"/>), spell-token
    /// replenishment (Caster(X) grants <c>Arg(0)</c> at round start), marker
    /// placement (Piercing Frenzy grants one marker on enemy-destroyed), and
    /// cross-unit target tagging when paired with a target selector. The count is
    /// a <see cref="ValueSource"/> so fixed grants use <c>Literal</c> while
    /// arg-driven grants (Caster) use <c>Arg</c>.
    ///
    /// <see cref="MaxTotal"/> (0 = uncapped) clamps the grant so the target's total of
    /// <see cref="TType"/> never exceeds it — the "up to a max. of X markers" clause on the
    /// growth/frenzy marker family (#100 #13), enforced at grant time exactly like the
    /// spell-token cap (tokens carry over, so a clear trigger can't express the cap).
    /// </summary>
    public sealed record GrantToken(TokenType TType, ValueSource Count, TokenClearTrigger Clear,
        int MaxTotal = 0) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            int count = Count.Resolve(ruleInvocation);
            if (MaxTotal > 0)
            {
                count = Math.Min(count,
                    MaxTotal - ruleInvocation.EffectiveTarget.Tokens.GetTokenCount(TType));
            }

            if (count <= 0)
            {
                return;
            }

            operations.Add(new RuleOperation.GrantTokenToUnit(
                ruleInvocation.EffectiveTarget,
                new Token(TType, count, Clear,
                    OwnerUnitID: ruleInvocation.OwnerForEffectiveTarget)));
        }
    }

    /// <summary>
    /// <see cref="GrantToken"/>'s cross-unit twin: the tokens land on the unit that KILLED the bearer,
    /// not on the bearer. #197 Vengeance ("place N markers on the unit that destroyed this one; friendly
    /// units get +N to hit against it"), authored at
    /// <see cref="EHookID.Shooting_OnUnitDestroyed"/> in the <see cref="ERuleSeat.Subject"/> seat.
    ///
    /// <para>A separate effect rather than a recipient flag on <see cref="GrantToken"/>: the passive path
    /// never sets <see cref="RuleInvocation.Target"/>, so "somewhere other than the bearer" has no general
    /// answer — it has to come from the firing context, and only this one context offers a killer. If a
    /// second "act on the other participant" passive rule ever appears, that is the point to generalize
    /// (a recipient <c>ValueSource</c>-style selector), not before.</para>
    ///
    /// <para>The token is stamped with the bearer as owner, and the clear trigger is the author's:
    /// Vengeance uses <see cref="TokenClearTrigger.ManualOnly"/>, since the marker must outlive the unit
    /// that placed it — an <see cref="TokenClearTrigger.OwnerDestroyed"/> marker here would be a
    /// contradiction, its owner being dead by construction.</para>
    /// </summary>
    public sealed record GrantTokenToKiller(TokenType TType, ValueSource Count, TokenClearTrigger Clear,
        int MaxTotal = 0) : Effect
    {
        public override IReadOnlyCollection<Type> RequiredCapabilities => [typeof(IHasKillerUnit)];

        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            // Not a CapabilityEffect<IHasKillerUnit> only because that base hands ApplyCore the context
            // alone: the count is a ValueSource (needs the whole invocation) and the owner stamp needs the
            // bearer. Same contract, same throw — an effect authored at a hook whose context can't answer
            // is a data bug, and the validator catches it before play (BookRuleSupplement.ValidateAll).
            if (ruleInvocation.Hook is not IHasKillerUnit killerContext)
            {
                throw new InvalidOperationException(
                    $"{nameof(GrantTokenToKiller)} requires {nameof(IHasKillerUnit)}, but the firing " +
                    $"context ({ruleInvocation.Hook?.GetType().Name ?? "none"}) does not provide it.");
            }

            IUnit killer = killerContext.KillerUnit;
            int count = Count.Resolve(ruleInvocation);
            if (MaxTotal > 0)
            {
                count = Math.Min(count, MaxTotal - killer.Tokens.GetTokenCount(TType));
            }

            if (count <= 0)
            {
                return;
            }

            operations.Add(new RuleOperation.GrantTokenToUnit(
                killer, new Token(TType, count, Clear, OwnerUnitID: ruleInvocation.Bearer.ID)));
        }
    }

    /// <summary>
    /// #197 P12: grants the bearer one token of <see cref="TType"/> whose
    /// <see cref="TokenPayload.Magnitude"/> is the number of wounds it just ignored — Regenerative
    /// Strength's "place one marker on this model when it ignores a wound". Authored at
    /// <see cref="EHookID.Lifecycle_OnWoundIgnored"/> in the <see cref="ERuleSeat.Subject"/> seat.
    ///
    /// <para>A separate effect rather than a <see cref="ValueSource"/> on <see cref="GrantToken"/>
    /// because the count is a FLOAT. <see cref="ValueSource.Resolve"/> returns an int by design (every
    /// other source is authored or counted), and widening it would put a rounding seam in front of every
    /// value-driven effect in the engine. Reading the fraction off the capability keeps the compromise
    /// where it belongs: in the one rule that actually has a fractional quantity. Repeat firings merge
    /// into a single running total — see <see cref="TokenContainer.AddToken"/>'s magnitude branch.</para>
    ///
    /// <para>No <c>MaxTotal</c>: the corpus text caps nothing ("+X attacks, where X is the number of
    /// markers on it"), and a cap on a fractional total would need its own rounding decision. Add one
    /// when a rule asks for it.</para>
    /// </summary>
    public sealed record GrantIgnoredWoundMarker(TokenType TType, TokenClearTrigger Clear) : Effect
    {
        public override IReadOnlyCollection<Type> RequiredCapabilities => [typeof(IHasIgnoredWoundCount)];

        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            // Same contract and same throw as GrantTokenToKiller: an effect authored at a hook whose
            // context can't answer is a data bug, caught before play by BookRuleSupplement.ValidateAll.
            if (ruleInvocation.Hook is not IHasIgnoredWoundCount ignoredContext)
            {
                throw new InvalidOperationException(
                    $"{nameof(GrantIgnoredWoundMarker)} requires {nameof(IHasIgnoredWoundCount)}, but the " +
                    $"firing context ({ruleInvocation.Hook?.GetType().Name ?? "none"}) does not provide it.");
            }

            float ignored = ignoredContext.IgnoredWoundCount;
            if (ignored <= 0f)
            {
                return;
            }

            operations.Add(new RuleOperation.GrantTokenToUnit(
                ruleInvocation.EffectiveTarget,
                new Token(TType, 1, Clear, Payload: new TokenPayload.Magnitude(ignored),
                    OwnerUnitID: ruleInvocation.OwnerForEffectiveTarget)));
        }
    }

    /// <summary>
    /// Removes <see cref="Count"/> tokens of <see cref="TType"/> from the
    /// bearer's container. Covers paying activated-ability costs — spending
    /// spell tokens, consuming once-per-game markers, etc. Pairs with
    /// <see cref="Cost.ConsumesToken"/> at the activated-ability layer.
    /// </summary>
    public sealed record ConsumeToken(TokenType TType, int Count) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.ConsumeTokensFromUnit(
                ruleInvocation.EffectiveTarget, TType, Count));
        }
    }

    /// <summary>
    /// Invokes the movement subsystem inline — the effect's target moves up to
    /// <see cref="MaxInches"/>. If <see cref="IsOptional"/>, the player may
    /// decline the move. Engine primitive (Phase 7h / item #042 engine refactor)
    /// that rules invoke without re-implementing movement logic. Covers
    /// Harassing (3" after shooting/melee, optional), Re-Position Artillery,
    /// Vanguard reposition — and #034's "reposition an enemy unit" spell, where
    /// the target is an enemy and the bearer is the caster.
    ///
    /// The move is directed by the rule's BEARER's controller (passed as the
    /// operation's <c>Controller</c>): for a self-move the bearer is the moving
    /// unit, so it's that unit's own owner (unchanged behaviour); for a cross-unit
    /// forced move the bearer is the caster, so the caster directs where the
    /// enemy is displaced rather than the victim choosing for itself.
    /// </summary>
    /// <para>#434: <see cref="OncePerRoundKey"/> - OPR 3.6 Mind Control's "Units may only be moved once per round in
    /// this way" (see <see cref="RuleOperation.InvokeTriggeredMove.OncePerRoundKey"/>). Null (the default, what old data
    /// parses to) is unrestricted.</para>
    public sealed record TriggeredMove(float MaxInches, bool IsOptional, string? OncePerRoundKey = null) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.InvokeTriggeredMove(
                ruleInvocation.EffectiveTarget, MaxInches, IsOptional, ruleInvocation.Bearer.PlayerID,
                OncePerRoundKey));
        }
    }

    /// <summary>
    /// Triggers a second activation of the bearer this round. The engine primitive behind Martial Prowess
    /// ("once per game, this unit may activate a second time") and #197's Inquisitorial Agent, which is the
    /// same activation plus two riders:
    /// <list type="bullet">
    /// <item><see cref="ClearsFatigue"/> — "stops being fatigued when activated for the second time".
    /// Martial Prowess says no such thing, so it stays off by default.</item>
    /// <item><see cref="ArmyRoundQuotaDivisor"/> — "only up to one third of the units in the army with this
    /// rule at the beginning of the game (rounding up) may use it in a single round". 0 means no quota.
    /// Enforced by <c>DeterminePlayerTurnStage</c>, the one site that offers reactivations and the only
    /// layer with the whole army in hand: neither the Cost seam nor a Condition can see past one unit.</item>
    /// </list>
    /// Could still grow a <c>UnitID Target</c> if a future rule reactivates a DIFFERENT unit (#197 P19).
    /// </summary>
    /// <summary>
    /// #197 P19 — the TARGET takes the next activation, ahead of the normal team alternation. The engine
    /// primitive behind Coordinate ("at the end of this unit's activation, another friendly unit within 12"
    /// that hasn't activated yet may be activated immediately"), and named for the mechanism rather than for
    /// that rule: nothing in the engine knows Coordinate exists.
    ///
    /// <para>Deliberately NOT <see cref="Reactivate"/>, which is a different mechanic wearing a similar
    /// name. Reactivate re-adds an ALREADY-ACTIVATED unit to the pool so it appears as a CHOICE later;
    /// this takes a unit that has NOT activated and makes it the next activation, now. Reactivate grants an
    /// extra activation; this one only reorders the ones already owed, so the acting side gains tempo, not
    /// activation count.</para>
    ///
    /// <para>The effect writes a flag rather than driving the flow itself: the emitted
    /// <see cref="RuleOperation.InvokeActivateUnitNext"/> is turned into a
    /// <see cref="Foundation.TokenType.ActivatesNext"/> marker on the target, which
    /// <c>DeterminePlayerTurnStage</c> and <c>ChooseUnitToActivateStage</c> read on their way past. That
    /// indirection is what lets the target belong to a DIFFERENT PLAYER (an ally in a team game): the
    /// alternation cursor is simply pointed at the flagged unit's owner, so the ally resolves that
    /// activation with their own controller rather than the granting player driving someone else's unit.
    /// It is also why the grant survives a save/load — the rolling save point is written at the top of
    /// DeterminePlayerTurnStage, i.e. AFTER the grant, and unit tokens persist while stage-local state
    /// would not.</para>
    ///
    /// <para>SUPPORTED PATHS (not universal): the operation is enacted only by
    /// <c>ReconcileEndOfActivationStage</c>, which owns the target pick as well; any other hook's generic
    /// op application drops it, and <c>RuleFireLint</c> reports it as unhandled there.</para>
    /// </summary>
    public sealed record ActivateUnitNext : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.InvokeActivateUnitNext(ruleInvocation.EffectiveTarget));
        }
    }

    public sealed record Reactivate(bool ClearsFatigue = false, int ArmyRoundQuotaDivisor = 0) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.InvokeReactivate(
                ruleInvocation.EffectiveTarget, ClearsFatigue));
        }
    }

    /// <summary>
    /// Multiplies each wound the attack deals by <see cref="Multiplier"/>. Covers
    /// Deadly(X) — the multiplier is the rule's argument, so it's a
    /// <see cref="ValueSource"/> (typically <c>Arg(0)</c>) rather than a literal.
    /// </summary>
    public sealed record MultiplyWounds(ValueSource Multiplier) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.MultiplyWounds(Multiplier.Resolve(ruleInvocation)));
        }
    }

    /// <summary>
    /// Reduces the incoming attack's WEAPON armor penetration by <see cref="Amount"/>, floored at AP(0)
    /// by the save stage — so it does nothing against an AP(0) hit, unlike a flat save bonus. A defender
    /// (Subject) effect read at <c>DetermineSaveRollsNeededStage</c> against the weapon's own AP only;
    /// rule-granted AP (Rending/Thrust) rides the save modifier and isn't reduced here. Covers Fortified.
    /// </summary>
    public sealed record ReduceArmorPenetration(int Amount) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.ReduceArmorPenetration(Amount));
        }
    }

    /// <summary>
    /// A roll modifier whose magnitude scales with the BEARER's count of <see cref="TType"/> tokens
    /// (#100 #13, the growth/frenzy marker family): delta = <see cref="Delta"/> x (count /
    /// <see cref="PerMarkers"/>, integer division). Emits nothing at zero steps, so author it behind a
    /// <see cref="Condition.TokenPresent"/> with <c>MinCount = PerMarkers</c> — that also lets
    /// <see cref="Dispatch.RuleFireLint"/> seed enough markers to prove the entry fires. Marker CAPS
    /// ("up to a max of two markers") belong on the granting entry (<see cref="GrantToken.MaxTotal"/>),
    /// not here. Covers Piercing/Precision/Defensive Frenzy (per marker) and the Growth family
    /// (per two markers).
    /// </summary>
    public sealed record TokenScaledRollModifier(TokenType TType, ERollKind RollKind, int Delta,
        int PerMarkers = 1) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            int steps = ruleInvocation.Bearer.Tokens.GetTokenCount(TType) / PerMarkers;
            if (steps > 0)
            {
                operations.Add(new RuleOperation.ApplyRollModifier(RollKind, Delta * steps));
            }
        }
    }

    /// <summary>
    /// <see cref="ReduceArmorPenetration"/> scaled by the BEARER's count of <see cref="TType"/> tokens
    /// (#100 #13): reduction = count / <see cref="PerMarkers"/>, capped at <see cref="MaxReduction"/>
    /// (0 = uncapped). The cap exists because Fortified Growth reads "for each marker ... up to a max
    /// of -2" while accumulating up to four markers — a read-side cap distinct from the grant-side one.
    /// Same consumption seam as <see cref="ReduceArmorPenetration"/> (Subject at the hit-complete hook,
    /// folded by <c>DetermineSaveRollsNeededStage</c>).
    /// </summary>
    public sealed record TokenScaledReduceArmorPenetration(TokenType TType, int PerMarkers = 1,
        int MaxReduction = 0) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            int steps = ruleInvocation.Bearer.Tokens.GetTokenCount(TType) / PerMarkers;
            if (MaxReduction > 0)
            {
                steps = Math.Min(steps, MaxReduction);
            }

            if (steps > 0)
            {
                operations.Add(new RuleOperation.ReduceArmorPenetration(steps));
            }
        }
    }

    /// <summary>
    /// Caps the bearer's hit-roll target at <see cref="Quality"/> (a floor on the
    /// roll needed): the attack hits on <see cref="Quality"/>+ regardless of the
    /// model's own Quality. Covers Reliable ("Attacks at Quality 2+"). A fixed
    /// authored value, not a rule argument.
    /// </summary>
    public sealed record QualityFloor(int Quality) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.QualityFloor(Quality));
        }
    }

    /// <summary>
    /// Each wound the bearer would take is ignored on an unmodified roll of
    /// <see cref="MinRoll"/> or higher. Covers Regeneration (5+). Fixed authored
    /// threshold, not a rule argument.
    /// </summary>
    public sealed record IgnoreWoundOnRoll(int MinRoll) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.IgnoreWound(MinRoll));
        }
    }

    /// <summary>
    /// Sets the bearer model's maximum wounds to <see cref="Amount"/> at creation.
    /// Covers Tough(X) — the value is the rule's argument (<c>Arg(0)</c>). Models
    /// default to 1 max wound; Tough raises it.
    /// </summary>
    public sealed record SetMaxWounds(ValueSource Amount) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.SetMaxWounds(Amount.Resolve(ruleInvocation)));
        }
    }

    /// <summary>
    /// The bearer unit counts as having Defense <see cref="Defense"/>+ in place of its own Defense
    /// stat. Covers Armor(X) — the value is the rule's argument (<c>Arg(0)</c>). A literal SET, not
    /// a floor (owner-ruled 2026-07-29): it replaces the base stat even where the base was better,
    /// though no current corpus site worsens. Per-roll modifiers (AP, cover, Shielded) still stack
    /// on the set base. Fires at <see cref="Foundation.EHookID.Lifecycle_OnUnitCreated"/>, Tough's
    /// seam, so every save path — volleys, melee, impact, reflect, synthetic hits, and the AI's
    /// CombatMath mirror — reads the set value through the unit's Defense stat.
    /// </summary>
    public sealed record SetDefense(ValueSource Defense) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.SetDefense(Defense.Resolve(ruleInvocation)));
        }
    }

    /// <summary>
    /// Multiplies each hit the attack scores by <see cref="Multiplier"/> (capped at
    /// the target's model count by the engine). Covers Blast(X) — the multiplier is
    /// the rule's argument.
    /// </summary>
    public sealed record MultiplyHits(ValueSource Multiplier) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.MultiplyHits(Multiplier.Resolve(ruleInvocation)));
        }
    }

    /// <summary>
    /// On a charge, rolls impact dice (each 2+ a hit) before either unit attacks. Covers Impact(X) - X is the
    /// rule's argument, <see cref="DiceCount"/> - and, with <see cref="ArmorPenetration"/> &gt; 0, Heavy Impact
    /// ("Counts as having Impact(X) with hits that have AP(1)").
    /// <para>
    /// #447: X is PER MODEL. Impact is a "this model" rule like every rule a model carries, so a 5-model unit with
    /// Impact(1) rolls 5 dice. The effect emits X x the living models carrying THIS attachment
    /// (<see cref="RuleInvocation.Attachment"/>, #422), so the dice sum across sources without double counting:
    /// </para>
    /// <list type="bullet">
    ///   <item>attached to the unit (its profile): every living NATIVE model - a joined hero does not inherit the
    ///         host's rules (the #006 merge relocated the hero's own rules onto its model);</item>
    ///   <item>attached to models (a joined hero's own Impact, a #407 per-model grant such as "replace one CCW
    ///         with Rotor Blade Attack (Impact(3))"): the living models carrying that attachment;</item>
    ///   <item>granted to the unit by a token (an aura or "gains Impact" buff, never statically attached): every
    ///         living model, hero included, as grants cover the current unit (#183's rule);</item>
    ///   <item>an argument-less copy (a literal X, the only shape a grant can carry) fires once per bearer whatever
    ///         the attachment count (the evaluator's dedup), so it counts every carrier of the definition.</item>
    /// </list>
    /// <para>
    /// One attachment shared by several models (a fresh army's model grant) fires once and counts them all; a
    /// resumed game rehydrates one attachment per model and fires once per model, counting one each - the same
    /// total. Only living models count, so casualties taken before the charge (a Dangerous terrain test at the
    /// start of the activation, #446) shrink the pool. Models out of melee range still count: the 3.6 text has no
    /// reach condition (#447 D1, owner review). The op carries the carrier count so the stage can show the sum.
    /// </para>
    /// </summary>
    public sealed record ChargeImpactHits(ValueSource DiceCount, int ArmorPenetration = 0) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            int perModel = DiceCount.Resolve(ruleInvocation);
            int carriers = CountCarriers(ruleInvocation);
            if (perModel <= 0 || carriers <= 0)
            {
                // Nobody carrying this copy is left alive (e.g. the host's Impact once only its joined hero
                // stands): it brings no dice. Tracing reports "condition passed but ... produced no operations".
                return;
            }

            operations.Add(new RuleOperation.ChargeImpactHits(perModel * carriers, ArmorPenetration, carriers));
        }

        /// <summary>
        /// The living models carrying THIS attachment (#447's per-model accounting, see the type doc) - shared with
        /// <see cref="DealAutoWounds"/> (#434: Ravage(X) is per model the same way, so a joined hero's own Ravage
        /// counts the hero and the host's counts the host's models).
        /// </summary>
        internal static int CountCarriers(RuleInvocation invocation)
        {
            IUnit unit = invocation.Bearer;
            Dispatch.ResolvedRule? attachment = invocation.Attachment;
            SpecialRuleDefinition? definition = invocation.Definition;
            if (attachment == null || definition == null)
            {
                // Off the passive dispatch path (a hand-built invocation): treat it as unit-wide.
                return unit.Models.Count(m => m.GetIsAlive());
            }

            ModelID? heroId = unit.JoinedHeroModelId;
            bool IsJoinedHero(IModel model) => heroId is ModelID hid && model.ID == hid;
            bool argless = attachment.Arguments.Count == 0;
            bool Matches(Dispatch.ResolvedRule rule) => argless ? rule.Definition == definition : rule.Equals(attachment);

            if (invocation.Weapon != null)
            {
                // No core Impact rides a weapon (the stage passes no charger weapons), but a charge-contact rule
                // that did would count the models wielding a weapon carrying it.
                return unit.Models.Count(m => m.GetIsAlive() && m.Weapons.Any(w => w.RuleDefinitions.Any(Matches)));
            }

            bool onUnit = unit.RuleDefinitions.Any(Matches);
            bool onAnyModel = unit.Models.Any(m => m.RuleDefinitions.Any(Matches));
            bool granted = argless
                ? RuleGrantQueries.UnitHasGrantedRule(unit, definition)
                : !onUnit && !onAnyModel; // an argumented copy found on no carrier came from somewhere unit-wide

            return unit.Models.Count(m => m.GetIsAlive()
                && (granted || m.RuleDefinitions.Any(Matches) || (onUnit && !IsJoinedHero(m))));
        }
    }

    /// <summary>
    /// #422 Brace(X): "Enemies charging this unit get -X total Impact rolls, unless this unit is Fatigued"
    /// (Core Rules v3.6.0). A defender-seat effect at charge contact: it resolves to
    /// <see cref="DicePerModel"/> x the living models carrying this rule and emits that as a NEGATIVE
    /// <see cref="RuleOperation.ChargeImpactHits"/>, which folds into the same <see cref="IImpactSink"/> as the
    /// charger's Impact(X), so <c>ResolveImpactHitsStage</c> rolls the net dice and skips at zero or below (the
    /// floor). The Fatigue clause is the entry's condition (<see cref="Condition.CountsAsFatiguedInMelee"/>),
    /// not this effect's business.
    /// <para>
    /// Who counts. Brace is a weapon rule (AoF spears carry Brace(1); "Brace in Melee" puts Brace(1) on a
    /// model's weapons), so a model counts when one of its MELEE weapons carries the rule; attached to the unit
    /// itself (no carrying weapon) every living model counts. "Total" is read as the SUM over those models -
    /// Brace(2) on three models cancels 6 dice (#422 decision). An (X) rule fires once per distinct
    /// attachment (the evaluator's dedup), so with an argument it counts only the models carrying THIS
    /// attachment (<see cref="RuleInvocation.Attachment"/>): a fresh army shares one attachment across a weapon
    /// entry's copies and fires once, while a resumed game rehydrates one attachment per weapon and fires once
    /// per weapon - counting every carrier on every firing would multiply the reduction there. An
    /// argument-less copy (Brace in Melee's literal 1) fires once per bearer whatever the attachment count,
    /// so it counts every carrier of the definition.
    /// </para>
    /// </summary>
    public sealed record CancelImpactDice(ValueSource DicePerModel) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            int carriers = CountCarriers(ruleInvocation);
            int dice = DicePerModel.Resolve(ruleInvocation) * carriers;
            if (dice > 0)
            {
                operations.Add(new RuleOperation.ChargeImpactHits(-dice, Models: carriers));
            }
        }

        private static int CountCarriers(RuleInvocation invocation)
        {
            if (invocation.Weapon == null || invocation.Definition == null)
            {
                return invocation.Bearer.Models.Count(m => m.GetIsAlive());
            }

            // Melee weapons only: Brace is "on its weapons in melee", and a unit-level mention is re-homed onto
            // EVERY weapon, so a model whose only Brace copy sits on its rifle brings nothing to the melee.
            Dispatch.ResolvedRule? attachment = invocation.Arguments.Count > 0 ? invocation.Attachment : null;
            return invocation.Bearer.Models.Count(m => m.GetIsAlive()
                && m.Weapons.Any(w => w.IsMelee() && w.RuleDefinitions.Any(r => attachment != null
                    ? r.Equals(attachment)
                    : r.Definition == invocation.Definition)));
        }
    }

    /// <summary>
    /// #423 Parry(X), Core Rules v3.6.0: "When this unit takes hits in melee, roll X dice per model with this rule in
    /// it, up to as many dice as hits taken. For each 6+ one hit is ignored (defender picks which)." A defender-seat
    /// effect at <see cref="Rules.Foundation.EHookID.Melee_OnHitsTaken"/>: it resolves X and names the living models
    /// carrying THIS attachment, emitting one <see cref="RuleOperation.ParryDice"/>. <c>ParryHitsStage</c> folds every
    /// firing through a <c>ParrySink</c> (the highest X per model, so a model with two Parry weapons - no 3.6.0 unit has
    /// one - rolls X once), rolls min(budget, hits) with the defender's own dice and removes one hit per 6+ from the
    /// worst hit group first. "Up to as many dice as hits taken" and the one-budget-per-enemy-attack rule (#423 B1) are
    /// the stage's business, not this effect's.
    /// <para>
    /// Who counts: the <see cref="CancelImpactDice"/> accounting. Parry is a weapon rule (AoF Hand Weapons, the GDF
    /// Daemons' claws), so a model counts when one of its MELEE weapons carries this copy of the rule - a unit-level
    /// mention is re-homed onto every weapon at army load, so a model whose only Parry copy sits on its bow brings
    /// nothing to the melee. Attached to the unit itself (a grant, a hand-built invocation), every living model counts.
    /// An argumented copy counts the models carrying THIS attachment (one shared attachment on a fresh army's weapons,
    /// one per weapon after a resume rehydrates them - the sink's per-model fold makes both read the same); an
    /// argument-less copy (a literal X) counts every carrier of the definition.
    /// </para>
    /// </summary>
    public sealed record ParryHits(ValueSource DicePerModel) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            int perModel = DicePerModel.Resolve(ruleInvocation);
            if (perModel <= 0) return;

            List<ModelID> carriers = Carriers(ruleInvocation);
            if (carriers.Count == 0)
            {
                // Nobody carrying this copy is left alive: no dice. Tracing reports "produced no operations".
                return;
            }

            operations.Add(new RuleOperation.ParryDice(perModel, carriers));
        }

        private static List<ModelID> Carriers(RuleInvocation invocation)
        {
            IUnit unit = invocation.Bearer;
            if (invocation.Weapon == null || invocation.Definition == null)
            {
                return unit.Models.Where(m => m.GetIsAlive()).Select(m => m.ID).ToList();
            }

            Dispatch.ResolvedRule? attachment = invocation.Arguments.Count > 0 ? invocation.Attachment : null;
            return unit.Models
                .Where(m => m.GetIsAlive() && m.Weapons.Any(w => w.IsMelee() && w.RuleDefinitions.Any(r => attachment != null
                    ? r.Equals(attachment)
                    : r.Definition == invocation.Definition)))
                .Select(m => m.ID)
                .ToList();
        }
    }

    /// <summary>
    /// 3.5 Counter's companion facet ("the charging unit rolls -1 Impact die per model with Counter"), which
    /// Core Rules v3.6.0 dropped from Counter and re-homed as <see cref="CancelImpactDice"/> (Brace) (#422). The
    /// JSON kind <c>reduceImpactDicePerModel</c> still parses because pre-3.6 army files and the bundled books
    /// until the #438 re-bake embed "Counter in Melee" with it, and those must keep loading (plan rule 6).
    /// It emits nothing, so they get 3.6 semantics; <c>RuleValidator.ValidateAuthoring</c> refuses it in new
    /// data.
    /// </summary>
    public sealed record LegacyReduceImpactDicePerModel : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
        }
    }

    /// <summary>
    /// Adds <see cref="Amount"/> to the ENEMY's taken-wounds total when deciding who won a melee (#419,
    /// Core Rules v3.6.0 Fear(X): "enemies count as having taken +X wounds when checking who won"). Covers
    /// Fear(X) — the amount is the rule's argument. No real wounds are dealt.
    /// </summary>
    public sealed record ExtraMeleeWoundCount(ValueSource Amount) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.ExtraMeleeWoundCount(Amount.Resolve(ruleInvocation)));
        }
    }

    /// <summary>
    /// The bearer strikes before the charging unit resolves its strikes. Covers Counter
    /// ("May strike first with this weapon when charged, unless this unit is Fatigued" - the
    /// Fatigue clause is the entry's condition, #422). 3.5 Counter's companion facet, the
    /// charger losing Impact rolls, became Brace(X) in 3.6 (<see cref="CancelImpactDice"/>).
    /// </summary>
    public sealed record StrikeFirst : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.StrikeFirst());
        }
    }

    /// <summary>
    /// The bearer's strikes come after those of the unit it charged - Unwieldy's "strikes last when
    /// charging" (#197 P20). Authored at <see cref="EHookID.Melee_OnCounterTrigger"/> on the ACTOR seat,
    /// where <see cref="StrikeFirst"/> sits on the Subject seat: the two describe the same swapped melee
    /// from opposite sides, so a charger with this and a defender with Counter still swap exactly once.
    /// "When charging" needs no condition - the hook only fires for a charge.
    /// </summary>
    public sealed record StrikeLast : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.StrikeLast());
        }
    }

    /// <summary>
    /// The bearer may shoot after a move that would normally forbid it - Quick Shot's "may shoot after
    /// using Rush actions" (#197 P20). Authored at <see cref="EHookID.Activation_OnActionChoice"/>, the
    /// hook <c>ChooseActionStage</c> already fires to collect <see cref="RestrictActions"/>; the shoot gate
    /// waives its advance-and-shoot distance cap when the op is present. Modelled as a PERMISSION rather
    /// than a movement bonus on purpose - see <see cref="RuleOperation.AllowShootAfterRush"/>.
    /// </summary>
    public sealed record ShootAfterRush : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.AllowShootAfterRush());
        }
    }

    /// <summary>
    /// #425: the bearer may not shoot once it has moved this activation - Cumbersome's "can't shoot when
    /// moving" (Core Rules v3.6.0: "May only use Hold or Advance actions, but can't shoot when moving."). The
    /// mirror of <see cref="ShootAfterRush"/>, authored at the same <see cref="EHookID.Activation_OnActionChoice"/>
    /// hook: <c>ChooseActionStage.GetCanShoot</c> closes Shoot for a unit that has moved (a 0" Advance included -
    /// the action, not the distance, is what moved it) when the op is present. Read through
    /// <see cref="Dispatch.ActionRestrictionQueries.ShootingAfterMovingForbiddenBy"/>.
    /// </summary>
    public sealed record NoShootingAfterMoving : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.ForbidShootingAfterMoving());
        }
    }

    // --- Capabilities --------------------------------------------------------------------------
    //
    // Authored at EHookID.Lifecycle_OnCapabilityQuery, where an effect is the ANSWER to "what can this
    // unit do?" rather than something that happens. Each grants no token and mutates nothing: holding a
    // capability is not a state a unit carries around, it is a question re-answered whenever it is asked,
    // so the entry's Condition gates it and rule suppression can cancel it.
    //
    // The alternative - the engine testing for a named rule - silently excludes any OTHER rule conferring
    // the same thing, which is exactly how Caster Group (not Caster, and never grantable as one: its X is
    // a live model count and grants carry no arguments) went uncastable.

    /// <summary>
    /// The bearer can cast spells. Conferred by core <c>Caster(X)</c> and by <c>Caster Group</c>.
    /// </summary>
    public sealed record EnableCasting : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.EnableCasting());
        }
    }

    /// <summary>
    /// The bearer can carry friendly units, with room for <see cref="Capacity"/> spaces. Conferred by
    /// core <c>Transport(X)</c>, whose X is the capacity — hence a <see cref="ValueSource"/> rather than a
    /// plain int, so the capacity is read off the rule instance like every other argumented rule.
    /// </summary>
    public sealed record EnableTransport(ValueSource Capacity) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.EnableTransport(Capacity.Resolve(ruleInvocation)));
        }
    }

    /// <summary>
    /// The bearer may be re-deployed in the post-deployment pass. Conferred by core <c>Re-Deployment</c>.
    /// </summary>
    public sealed record EnableReDeployment : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.EnableReDeployment());
        }
    }

    /// <summary>
    /// #424: the carrying weapon counts as a MELEE weapon whatever its printed range. Conferred by Core Rules
    /// v3.6.0 <c>Assault</c> ("Counts as both a ranged weapon and a melee weapon."), authored on a weapon-scoped
    /// rule at <see cref="EHookID.Lifecycle_OnCapabilityQuery"/> like every other capability - so any other rule
    /// that confers the same thing needs no engine change, and nothing tests for the name "Assault".
    ///
    /// <para>A weapon ROLE, not a unit capability: it is read by <c>WeaponRoleQueries</c> off the weapon's own
    /// rules, resolved once when the weapon is built (the <c>Weapon.EffectKey</c>-at-load precedent, #239) and
    /// cached on it, because the classification seam runs in hot paths (the AI's search, per-frame UI) and from
    /// places with no table at all (the Forge card, the list compiler, the importer). Roles are grants only: a
    /// future "may not be used in melee" is an action restriction beside <c>ActionRestrictionQueries</c>, never a
    /// negation here.</para>
    /// </summary>
    public sealed record EnableMeleeUse : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.EnableMeleeUse());
        }
    }

    /// <summary>
    /// #424: the carrying weapon counts as a RANGED weapon whatever its printed range - the twin of
    /// <see cref="EnableMeleeUse"/>. Assault authors both arms because the rule says "both"; every 3.6.0 Assault
    /// weapon has a printed range, so for the corpus this arm only restates what the range already says.
    /// </summary>
    public sealed record EnableRangedUse : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.EnableRangedUse());
        }
    }

    /// <summary>
    /// The bearer opens its <see cref="Pool"/> to other friendly casters within <see cref="RangeInches"/>,
    /// who spend those tokens as if they were their own spell tokens. Conferred by <c>Spell Accumulator(X)</c>,
    /// whose X is how many tokens the pool gains each round (a separate <see cref="GrantToken"/> entry) - the
    /// range is not argumented in the corpus, hence a plain float here rather than a <see cref="ValueSource"/>.
    ///
    /// <para>Authored at <see cref="EHookID.Lifecycle_OnCapabilityQuery"/> like every other capability, which
    /// is what makes "friendly casters may only use this rule if this unit isn't Shaken" a plain
    /// <see cref="Condition"/> on the entry rather than a special case in the casting stage.</para>
    /// </summary>
    public sealed record EnableSpellLending(TokenType Pool, float RangeInches) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.EnableSpellLending(Pool, RangeInches));
        }
    }

    /// <summary>
    /// The bearer relays casts for other friendly casters within <see cref="RangeInches"/>: they may cast
    /// "as if they were in this model's position" and get <see cref="CastRollBonus"/> to the roll when they
    /// do. Conferred by <c>Spell Conduit</c>. Neither number is argumented in the corpus, hence plain values
    /// rather than <see cref="ValueSource"/>s.
    ///
    /// <para>Authored at <see cref="EHookID.Lifecycle_OnCapabilityQuery"/> like every other capability, so
    /// "friendly casters may only use this rule if this unit isn't Shaken" is a plain <see cref="Condition"/>
    /// on the entry - the same shape <c>Spell Accumulator</c> uses for the identical clause.</para>
    /// </summary>
    public sealed record EnableSpellRelay(float RangeInches, int CastRollBonus) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.EnableSpellRelay(RangeInches, CastRollBonus));
        }
    }

    /// <summary>
    /// The bearer relays non-spell friendly picks for other friendly units within <see cref="RangeInches"/>:
    /// an activated ability that picks friendly units may pick as if the user stood in the bearer's position
    /// (#197, <c>Extended Buff Range</c> — the HDF radios; 12" relay leg + the ability's own 12" makes the
    /// audit's "across 24\""). The spell twin is <see cref="EnableSpellRelay"/>; this one confers no roll
    /// bonus, and it is read by <c>AbilityTargeting</c> rather than <c>SpellRelay</c>.
    ///
    /// <para>Authored at <see cref="EHookID.Lifecycle_OnCapabilityQuery"/> like every other capability, so a
    /// wording gate (Conduit's "isn't Shaken") would be a plain <see cref="Condition"/> on the entry - the
    /// corpus rule carries none.</para>
    /// </summary>
    public sealed record EnableBuffRelay(float RangeInches) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.EnableBuffRelay(RangeInches));
        }
    }

    /// <summary>
    /// #197 Instinctive — the bearer is COMPELLED to attack: when it can shoot or charge it must (no
    /// idling, no non-attack actions first), and its attack must go to the closest valid target. A
    /// capability answer at <see cref="EHookID.Lifecycle_OnCapabilityQuery"/>: <c>ChooseActionStage</c>
    /// restricts the action menu, <c>ChooseRangedAttackStage</c> and <c>ChooseMeleeDefenderStage</c>
    /// narrow their target lists, and the resolvers inherit the narrowed requests by construction (the
    /// P20 Quick Shot marks pattern). The "+1 to hit for that attack" rider is NOT this effect — it is
    /// authored as ordinary <see cref="RollModifier"/> entries on the same rule, so the compulsion and
    /// the buff stay separately testable.
    ///
    /// <para>Authored with <see cref="Condition.MostModelsHaveThisRule"/> per the #267 convention (#428: "most
    /// models" since OPR 3.6): the compulsion binds the WHOLE unit's action, so it binds only while most of the
    /// unit has the rule - a joined hero without it is one model and no longer frees the unit (#303).</para>
    /// </summary>
    public sealed record CompelClosestTarget : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.CompelClosestTarget());
        }
    }

    /// <summary>
    /// Enemy Ambush arrivals must set up over <see cref="DistanceInches"/> from the bearer's unit
    /// (#197 P22, <c>Repel Ambushers</c>: "enemy units using Ambush must be set up over 12\" away from
    /// this model's unit"). A capability answer, not an event: the reserve-arrival pass asks every
    /// on-table unit what it does to an arriving Ambusher and folds the answers into the placement
    /// request's keep-out discs, so the entry's <see cref="Condition"/> gates it live and suppression
    /// applies.
    /// </summary>
    public sealed record RepelAmbushers(float DistanceInches) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.RepelAmbushers(DistanceInches));
        }
    }

    /// <summary>
    /// FRIENDLY Ambush arrivals within <see cref="RangeInches"/> of the bearer ignore every
    /// enemy-distance restriction — the core over-9" rule and any <see cref="RepelAmbushers"/> keep-away
    /// alike (#197 P22, <c>Ambush Beacon</c>; owner sign-off 2026-07-28: the waiver is judged per
    /// arriving MODEL, and overrides both restriction kinds). Same capability shape as
    /// <see cref="RepelAmbushers"/>, folded into the placement request's waiver discs.
    /// </summary>
    public sealed record AmbushBeacon(float RangeInches) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.AmbushBeacon(RangeInches));
        }
    }

    /// <summary>
    /// The bearer removes itself from the table at the end of its activation and redeploys as if it had
    /// Ambush at the start of the next round (#197 P22, <c>Ambush Re-Deployment</c>). Executable: the
    /// removal drops any objective the bearer's side holds within 1", parks the models off-table in
    /// reserve, and stamps <see cref="Rules.Foundation.TokenType.PendingAmbushArrival"/> — which the
    /// rule's own token-gated <see cref="DeferDeployment"/> entry reads, so the round-start arrival
    /// pass finds the return leg without a special case.
    /// </summary>
    public sealed record AmbushRedeploy : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.InvokeAmbushRedeploy(ruleInvocation.Bearer));
        }
    }

    /// <summary>
    /// The bearer places a NEW unit within <see cref="RadiusInches"/> of itself (#197 P17: Spawn's
    /// "place a new unit of X fully within 6\" of it"; Split reuses the shape at the destruction
    /// seam). WHICH unit is the rule instance's text argument — Spawn("Spores [5]") — naming an
    /// auxiliary unit spec the army carries (<c>ArmyListFile.AuxiliaryUnits</c>, compiled from the
    /// book or hand-authored). Executable: the creation, army registration, placement prompt and
    /// same-round activation adoption all live behind <see cref="IOperationServices.SpawnUnit"/>.
    /// A missing/wrong-kind argument warns and produces nothing rather than throwing mid-stage.
    /// </summary>
    public sealed record SpawnUnit(float RadiusInches = 6f) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            if (ruleInvocation.Arguments.Count == 0)
            {
                // No diagnostics from the Definitions layer (RuleDiagnostics lives in Dispatch); the
                // service warns when it can't find the spec, which covers the observable outcome.
                return;
            }

            // A Str argument is the shipped shape; an Int (e.g. the lint's generic seed) stringifies,
            // so a synthesized firing still proves the executable is produced and consumed.
            string specName = ruleInvocation.Arguments[0] switch
            {
                RuleArgument.Str str => str.Value,
                RuleArgument.Int intArg => intArg.Value.ToString(),
                _ => string.Empty,
            };

            operations.Add(new RuleOperation.InvokeSpawnUnit(ruleInvocation.Bearer, specName, RadiusInches));
        }
    }

    /// <summary>
    /// The bearer is removed from the table as destroyed and a fresh full-strength copy of it deploys
    /// within 9\" of any table edge, over 9\" from enemies (#426, 3.6), at the start of the next round, after Ambushers (#197 P17c,
    /// <c>Reinforcement</c>). Authored at BOTH trigger moments — the bearer's own destruction
    /// (<see cref="Rules.Foundation.EHookID.Lifecycle_OnSelfDestroyed"/>) and the Shaken application
    /// (<see cref="Rules.Foundation.EHookID.Morale_OnShakenApplied"/>) — each gated on the
    /// ReinforcementSpent token's absence, so accepting once (which kills the original, landing on the
    /// destruction seam) cannot re-prompt. Carries the firing rule's name so the copy is built WITHOUT
    /// it ("this rule doesn't apply to the new copy").
    /// </summary>
    public sealed record ReinforceUnit : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.InvokeReinforce(ruleInvocation.Bearer,
                ruleInvocation.Definition?.Name));
        }
    }

    /// <summary>
    /// The bearer rolls one die per wound it is missing (dead models included) and each
    /// <see cref="MinRoll"/>+ restores one wound (#197 P17d, <c>Reanimation</c>). Owner-ruled
    /// 2026-07-28: successes top up wounded LIVING models first, then revive dead ones at one wound
    /// each (a just-revived Tough model is then the wounded living model the next success tops up),
    /// and revived models auto-place in coherency beside a living model — no per-die prompts. The
    /// roll is DECISIVE (the ClearTokenOnRoll/Storm precedent): each die's outcome is binary, so a
    /// histogram would want to restore fractions of a model.
    /// </summary>
    public sealed record RestoreWounds(int MinRoll = 5) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.InvokeRestoreWounds(ruleInvocation.Bearer, MinRoll));
        }
    }

    /// <summary>
    /// Resolves the attack against a single chosen model in the target unit, as if
    /// it were a unit of one. Covers Takedown. Structural targeting change with no
    /// numeric parameter; the "resolved first, before other weapons" ordering is a
    /// dispatch-time detail (Phase 8).
    /// </summary>
    public sealed record TargetIndividualModel : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.TargetIndividualModel());
        }
    }

    /// <summary>
    /// Restricts the bearer to declaring only the actions in <see cref="Allowed"/>.
    /// Covers Immobile (<c>[Hold]</c> only) and Cumbersome (<c>[Hold, Advance]</c>, #425). The
    /// engine drops disallowed actions from the choice set, and a set without Rush caps the
    /// unit's move at its Advance (<c>MovementActionContext</c>).
    /// </summary>
    public sealed record RestrictActions(IReadOnlyList<EActionType> Allowed) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.RestrictActions(Allowed));
        }
    }

    /// <summary>
    /// Adjusts a weapon's effective shooting range by <see cref="Delta"/> inches. The seat decides whose
    /// range: at the Actor seat it widens the bearer's OWN weapons (Increased Shooting Range, +6); at the
    /// Subject seat it shrinks the range of enemies shooting the bearer (Ranged Shrouding, −6; Aircraft,
    /// "units targeting it get −12\""). Folded by <see cref="Dispatch.RangeRuleQueries.EffectiveRange"/>
    /// into the range check (#102). <see cref="MinResultInches"/> floors the resulting effective range —
    /// a reduction can't drop it below this (e.g. "−6\" to a min. of 6\""); 0 means no floor. Distinct from
    /// <see cref="Effect.RollModifier"/>, which adjusts the roll itself rather than the range threshold.
    /// <para>
    /// #432: <see cref="ChargeDelta"/> is the CHARGE half of the same rule - OPR 3.6 writes the range family as
    /// one effect over both attack kinds: Darkborn / Shadowborn / Shrouded / Wild Veil "that enemy unit gets
    /// -4"/-2" range to a min. of 6"" (Subject seat: <c>delta -4, chargeDelta -2, minResultInches 6</c>), Melee
    /// Shrouding "-2" range to a min. of 6"" when charged (<c>delta 0, chargeDelta -2</c>), Highborn / Extra Reach /
    /// Royal Legion "+4"/+2" range when shooting/charging" (Actor seat: <c>delta 4, chargeDelta 2</c>). Charge range
    /// is the charge move (3.6: "may only charge if at least one charging model is within charge distance"), so the
    /// charge half folds into the charge budget: the charger's own (Actor seat) through
    /// <see cref="Dispatch.RangeRuleQueries.OwnChargeRangeDelta"/> into every charge-budget reader (the movement
    /// context and its per-model budgets, the AI), the charged unit's (Subject seat) through
    /// <see cref="Dispatch.MovementRuleQueries.EffectiveChargeDistanceAgainst"/> (the path stage's worst case, the
    /// AI). <see cref="MinResultInches"/> floors each half the effect changes, and never RAISES a range that was
    /// already shorter than the floor ("to a min. of 6"" limits a reduction; it does not lengthen a 4" gun).
    /// Authored at <see cref="Foundation.EHookID.Shooting_OnRangeCheck"/>, the range check for both halves.
    /// </para>
    /// </summary>
    public sealed record RangeModifier(int Delta, int MinResultInches = 0, int ChargeDelta = 0) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.ApplyRangeModifier(Delta, MinResultInches, ChargeDelta));
        }
    }

    /// <summary>
    /// The bearer ignores terrain effects while moving. <see cref="Scope"/> selects how much: Strider waives
    /// only the difficult-terrain move cap (<see cref="ETerrainIgnoreScope.DifficultOnly"/>, the default),
    /// while Flying waives ALL terrain effects (<see cref="ETerrainIgnoreScope.AllTerrain"/> — difficult cap,
    /// Dangerous wound rolls, and Impassible blocking). Flying additionally moves through units via the
    /// separate <see cref="IgnoreEnemyMovementBlock"/>.
    /// </summary>
    public sealed record IgnoreTerrainEffects(ETerrainIgnoreScope Scope = ETerrainIgnoreScope.DifficultOnly) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.IgnoreTerrainEffects(Scope));
        }
    }

    /// <summary>
    /// The bearer may move through enemy units (its path isn't blocked by enemy bases), though it still
    /// may not end a move stacked on an enemy. The "Flying-only facet" foreshadowed by
    /// <see cref="IgnoreTerrainEffects"/> — granted by Strafing today (a future Flying rule can reuse it).
    /// </summary>
    public sealed record IgnoreEnemyMovementBlock : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.IgnoreEnemyMovementBlock());
        }
    }

    /// <summary>
    /// The bearer's attack ignores the target's cover (the cover save bonus does not apply). Covers
    /// Blast's "ignores cover" facet. Read by the cover stage (to drop the bonus) and surfaced to the
    /// movement + ranged-target resolver requests per-weapon, so they can represent that cover-blocked
    /// targets are still shootable with this weapon.
    /// </summary>
    public sealed record IgnoreCover : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.IgnoreCover());
        }
    }

    /// <summary>
    /// #431 Pierce: "Ignores all positive Defense modifiers on the target". Authored at
    /// <see cref="EHookID.Shooting_OnHitRollComplete"/> (Actor), where the defender's own Defense bonuses
    /// (Shielded, Grounded Protection, ... - every positive Save delta in the corpus rides that hook's Subject
    /// seat) fold into the attack's save modifier. <c>RollToHitStage</c> drops the positive Save deltas from
    /// that net when the op is present (the attacker's negative ones - Thrust, Destructive - still apply) and
    /// flags the results, so <c>DetermineSaveRollsNeededStage</c> also drops cover's +1 and any positive granted
    /// "+X to Defense rolls" buff. AP reduction (Fortified) changes the weapon's AP, not the target's Defense,
    /// and is unaffected (#431 decision).
    /// <para>
    /// Pierce ALSO carries <see cref="IgnoreCover"/>: cover's only effect is that +1, so the targeting
    /// overlays, the shooting forecast, the AI and the calculator - which ask
    /// <c>SightRuleQueries.IgnoresCover</c>, not this op - price the weapon the way the save stage resolves it.
    /// </para>
    /// </summary>
    public sealed record IgnorePositiveDefenseModifiers : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.IgnorePositiveDefenseModifiers());
        }
    }

    /// <summary>
    /// #427 Unstoppable: "ignores all negative modifiers to this weapon which are not from this model's special
    /// rules". Authored Actor-seat at <see cref="EHookID.Shooting_OnHitRollModifier"/> (the to-hit penalties) AND
    /// at <see cref="EHookID.Shooting_OnHitRollComplete"/> (the AP reductions), so the evaluator sees the marker
    /// in the same pass as the modifiers it removes. What is ignored: a to-hit penalty
    /// (<see cref="RuleOperation.ApplyRollModifier"/> of <see cref="ERollKind.Hit"/> below zero), an AP reduction
    /// (<see cref="RuleOperation.ReduceArmorPenetration"/> - Fortified and its kin), and a granted negative
    /// "-X to hit" token another unit placed. What is NOT: any modifier produced by a rule on the attacking unit
    /// itself (its own Imprecise, Unwieldy, Indirect, ...), shooting-range reductions (a reach limit, not a
    /// modifier to the attack - #427 D2), and the Defense side. See <see cref="RuleOperation.IgnoreNegativeWeaponModifiers"/>.
    /// </summary>
    public sealed record IgnoreNegativeWeaponModifiers : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.IgnoreNegativeWeaponModifiers());
        }
    }

    /// <summary>
    /// The bearer's attack ignores intervening terrain for line of sight — it may fire at targets it has
    /// no clear line to, as if in line of sight. Covers Indirect's "target non-LoS as if LoS" facet (Hold only
    /// since #425 - the condition, not this effect, carries the gate) and Quick Throw's "always".
    /// (Takedown does NOT grant it - see #314.) Read by the ranged-target enumeration and the occlusion
    /// stage (so the shot isn't blocked) and surfaced per-weapon to the movement + ranged-target resolver
    /// requests, so they can represent that LoS-blocked targets are still shootable with this weapon.
    /// </summary>
    public sealed record IgnoreLineOfSight : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.IgnoreLineOfSight());
        }
    }

    /// <summary>
    /// #425: the bearer's attack ignores cover granted by Sight Blockers, and only that - Core Rules v3.6.0
    /// Indirect's "ignores Cover from Sight Blockers" (Cover terrain still counts, unlike
    /// <see cref="IgnoreCover"/>, which drops every cover bonus). Meaningful together with
    /// <see cref="IgnoreLineOfSight"/>: only a shot that ignores line of sight can be resolved against a model
    /// behind a Sight Blocker. Read by <c>SightRuleQueries.SightBlockerCoverFor</c> into the cover verdict
    /// (<see cref="ESightBlockerCover.Ignored"/>).
    /// </summary>
    public sealed record IgnoreSightBlockerCover : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.IgnoreSightBlockerCover());
        }
    }

    /// <summary>
    /// Sets the bearer aside during normal deployment to be placed by its own dedicated pass.
    /// Covers Scout (deploy after others, within <see cref="PlacementRangeInches"/>" of the zone)
    /// and Ambush (deploy a later round, over <see cref="PlacementRangeInches"/>" from enemies).
    /// <see cref="Timing"/> selects the pass; <see cref="PlacementRangeInches"/>'s meaning depends on
    /// it (zone-forward-extension for Scout, min-distance-from-enemies for Ambush). Both default so a
    /// bare <c>DeferDeployment()</c> (used by the queue-level shape tests) keeps compiling.
    /// <see cref="EnemyClearanceInches"/> (#426, <see cref="EDeferTiming.AfterNormalDeployment"/> only) is
    /// Scout 3.6's "over 9\" away from non-Scout enemy units": the set-aside placement must keep that far from
    /// every enemy unit that did NOT come through the same set-aside pass. 0 = no enemy clause.
    /// </summary>
    public sealed record DeferDeployment(
        EDeferTiming Timing = EDeferTiming.AfterNormalDeployment,
        float PlacementRangeInches = 0f,
        int MinArrivalRound = 2,
        bool MandatoryArrival = false,
        float EnemyClearanceInches = 0f) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.DeferDeployment(Timing, PlacementRangeInches, MinArrivalRound,
                MandatoryArrival, EnemyClearanceInches));
        }
    }

    /// <summary>
    /// #035 — marker for the Disembark activated ability (a unit leaving a transport on its activation).
    /// Carries no resolvable operation: the disembark itself — placing the unit within 6" of the transport
    /// and un-embarking it — is enacted by <c>DisembarkStage</c>, to which <c>ChooseActionStage</c> routes
    /// this offer specially (the generic custom-action resolver is token-ops only). <see cref="Apply"/> is a
    /// deliberate no-op so a stray generic resolution can't crash mid-activation.
    /// </summary>
    public sealed record Disembark : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
        }
    }

    /// <summary>
    /// #035 — marker for the Embark activated ability (a unit boarding a friendly transport on its
    /// activation). Like <see cref="Disembark"/>, it carries no resolvable operation: the embark — setting
    /// the unit aside off-table into the transport — is enacted by <c>EmbarkStage</c>, to which
    /// <c>ChooseActionStage</c> routes this offer specially (after an engine "transport in range" gate,
    /// since the availability is spatial and can't be a data condition). <see cref="Apply"/> is a no-op.
    /// </summary>
    public sealed record Embark : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
        }
    }

    /// <summary>
    /// #197 — marker for the Teleport activated ability (a unit repositioning up to 6" on its activation,
    /// "once per activation, before attacking"). Like <see cref="Disembark"/>/<see cref="Embark"/>, it
    /// carries no resolvable operation: the placement itself - each living model set anywhere fully within
    /// 6" of its own position - is enacted by <c>TeleportStage</c>, to which <c>ChooseActionStage</c> routes
    /// this offer by name (the generic custom-action resolver is token-ops only). Being a menu action that
    /// loops back to Choose Action, the unit re-evaluates Charge/Shoot/Pass from its new position each time
    /// (#206's proximity Pass gate is what makes "teleport clear of an enemy -> Pass returns" work).
    /// <see cref="Apply"/> is a deliberate no-op so a stray generic resolution can't crash mid-activation.
    /// </summary>
    public sealed record Teleport : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
        }
    }

    /// <summary>
    /// #034 conditional/triggered spell (primitive #5): the target takes a morale test and, only on a
    /// FAILURE, suffers <see cref="OnFailure"/>. Covers Terrifying Fury ("morale test; if failed, becomes
    /// fatigued" → <see cref="ApplyFatigue"/>) and Deep Hypnosis ("morale test; if failed, you may move it
    /// up to 6&quot;" → <see cref="TriggeredMove"/>).
    ///
    /// <para>Unlike the flat effects above, the test is an async, presentation-emitting roll against live
    /// state, so it cannot be expressed as queued operations from a synchronous <see cref="Apply"/>. Like
    /// <see cref="DealHits"/> (a child pipeline) and <see cref="Disembark"/>/<see cref="Embark"/> (their own
    /// stages), it is enacted stage-side — <c>CastSpellStage</c> runs <c>MoraleUtilities.TakeMoraleTest</c>
    /// per target and applies <see cref="OnFailure"/> on a fail. <see cref="Apply"/> is therefore an
    /// unreachable no-op marker. <see cref="OnFailure"/> is a flat (token/executable) effect; a damage
    /// on-fail would need the child pipeline, which no corpus conditional spell requires.</para>
    /// </summary>
    public sealed record MoraleTestThen(Effect OnFailure) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            // Both units travel with the operation: the on-failure effect is resolved with the RULE'S
            // OWNER as bearer and the failing unit as target, which is what routes Mind Control's forced
            // move to the controlling player rather than to the victim (Effect.TriggeredMove reads
            // Bearer.PlayerID). CastSpellStage's spell path passes the caster for the same reason.
            operations.Add(new RuleOperation.InvokeMoraleTestThen(
                ruleInvocation.Bearer, ruleInvocation.EffectiveTarget, OnFailure));
        }
    }

    /// <summary>
    /// The bearer's models may each be PLACED anywhere fully within a rolled distance of where they stand —
    /// the corpus' reposition-at-activation rules (Wolfborn / Rapid Blink, "you may place all models with this
    /// rule in it anywhere fully within D3\" of their position"; Bounding, D3+1\"). A placement, not a move:
    /// nothing is asked of the path, only of the destination.
    ///
    /// <para>The die is rolled here, at Apply, exactly as <see cref="Heal"/> rolls its own — the operation
    /// carries a concrete distance. Multiple entries SUM: Enhanced Rapid Blink widens "within D3\"" to
    /// "within 2D3\"" by contributing a second, independently rolled D3, which is the increment discipline
    /// every other Enhanced rule follows. The stage folds them into ONE placement rather than prompting twice.</para>
    ///
    /// <para>#434: <see cref="Move"/> makes it a real MOVE instead - OPR 3.6 rewrote the family as "you may move all
    /// models with this rule in it by up to D3+1"" (Bounding, Dash, Nimble, Rapid Blink, Wolfborn), so the models
    /// travel a path that terrain, other units and coherency constrain, through the same optional triggered move the
    /// post-combat family uses (<see cref="TriggeredMove"/>'s <c>IOperationServices.MoveUnit</c>). False (the default,
    /// and what an old book or saved list without the field parses to) keeps the 3.5 placement.</para>
    /// </summary>
    public sealed record RepositionAtActivation(DiceExpression Distance, int PlusInches = 0, bool Move = false) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            // Σ face·count over the histogram, like Heal: a distance is a continuous magnitude, so the
            // probabilistic roller's expected value is meaningful here (unlike a pass/fail test, which must
            // ride RollDecisive to stay binary).
            IDiceResults results = ruleInvocation.DiceRoller!.Roll(Distance.Sides, 1f);
            float inches = PlusInches;
            for (int face = results.SideMin; face <= results.SideMax; face++)
            {
                inches += face * results.At(face);
            }

            operations.Add(new RuleOperation.RepositionModels(inches, AsMove: Move));
        }
    }

    /// <summary>
    /// A FLAT-distance reposition placement, no dice — the deploy-time twin of
    /// <see cref="RepositionAtActivation"/>. Covers Fanatic (#197 P21) and, since #426, Vanguard and the AoF
    /// Drakesworn: "After this model is deployed, it may be placed anywhere fully within 9" of its position,
    /// over 9" away from non-Vanguard enemy units." (Core Rules v3.6.0 added the enemy clause.) Emits the same
    /// <see cref="RuleOperation.RepositionModels"/> op, so the placement machinery is shared — but folded by
    /// the deployment stage (<c>DeployUnitStage</c>) rather than <c>ActivationStartStage</c>, since it fires
    /// at <see cref="Rules.Foundation.EHookID.Deployment_OnUnitDeployed"/>. Flat rather than reusing
    /// <see cref="RepositionAtActivation"/> with a degenerate die because every <c>DiceExpression</c> rolls
    /// at least one die; the corpus' deploy reposition is a fixed range.
    /// <para><see cref="EnemyClearanceInches"/> &gt; 0 keeps every placed model over that far from each enemy
    /// unit, except enemy units carrying <see cref="ExemptEnemyRule"/> (the text's "non-Vanguard"); null
    /// exempts nobody.</para>
    /// </summary>
    public sealed record RepositionOnDeploy(float MaxInches, float EnemyClearanceInches = 0f,
        string? ExemptEnemyRule = null) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.RepositionModels(MaxInches, EnemyClearanceInches, ExemptEnemyRule));
        }
    }

    /// <summary>
    /// Roll one die; on <see cref="MinRoll"/>+ every token of <see cref="TType"/> is removed from the bearer.
    /// The corpus' round-start Shaken recovery (Steadfast / Battleborn / Honor Code: "if a unit where all
    /// models have this rule is Shaken at the beginning of the round, roll one die. On a 4+, it stops being
    /// Shaken"). Gate it on <see cref="Condition.TokenPresent"/> so the die is only rolled when there is
    /// something to clear.
    ///
    /// <para>A sink fold, not an executable (#376 Vale Oath Boost, now Enhanced Vale Oath): all entries for one token type fold
    /// to the BEST (lowest) threshold and the stage makes ONE decisive roll at it — so an Enhanced rule is
    /// authored as the full boosted band (3), the min-threshold convention (see RerollSink's doctrine
    /// note), and never becomes a second chance. The stage still rolls with <c>RollDecisive</c>, never a
    /// histogram — the outcome is binary (the unit either recovers or does not), so a probabilistic
    /// roller must produce a concrete face or the token would be fractionally removed.</para>
    /// </summary>
    public sealed record ClearTokenOnRoll(TokenType TType, int MinRoll) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.ClearTokenOnRoll(TType, MinRoll));
        }
    }

    /// <summary>
    /// Rolls one die and, on <see cref="MinRoll"/>+, grants the effect's target <see cref="Count"/>
    /// tokens of <see cref="TType"/> — the roll-gated twin of <see cref="GrantToken"/>, and the grant
    /// mirror of <see cref="ClearTokenOnRoll"/> (same decisive-roll discipline, for the same dice
    /// invariant). Covers the Spotter family (#100 #14b): "roll one die, on a 4+ place a marker on it".
    /// An <see cref="ExecutableOperation"/> because the roll is live state.
    ///
    /// <para><see cref="OnFailure"/> (#376 Reckless Piercing): the optional failure arm of the SAME
    /// die — "on a 2+ gain the boon, but on a 1 ... instead". One roll, two outcomes; authoring the
    /// arms as two effects would roll two independent dice, the both-or-neither failure the
    /// Unpredictable machinery documents. Applied with the roll's unit as both bearer and target
    /// (the <see cref="MoraleTestThen"/> pattern); a flat (token/executable) effect, like that one's.</para>
    /// </summary>
    public sealed record GrantTokenOnRoll(TokenType TType, ValueSource Count, TokenClearTrigger Clear,
        int MinRoll, Effect? OnFailure = null) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.InvokeGrantTokenOnRoll(
                ruleInvocation.EffectiveTarget,
                new Token(TType, Count.Resolve(ruleInvocation), Clear,
                    OwnerUnitID: ruleInvocation.OwnerForEffectiveTarget),
                MinRoll, OnFailure));
        }
    }

    /// <summary>
    /// Fatigues the target for the rest of the round — it then hits only on unmodified 6s in melee (the
    /// #020 fatigue penalty). The wound-side mirror has no analogue; this is the conferred-debuff face of
    /// the same state melee charging/striking-back already inflicts. Enacted through the
    /// <c>IOperationServices</c> seam so it routes to the one authority on fatigue
    /// (<c>FatigueUtilities.ApplyFatigued</c>: round-end clear, idempotent, owner-cleanup-free) rather than
    /// re-deriving the token here. Covers Terrifying Fury's on-fail branch (#034 #5).
    /// </summary>
    public sealed record ApplyFatigue : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.InvokeApplyFatigue(ruleInvocation.EffectiveTarget));
        }
    }

    /// <summary>
    /// The bearer's move suffers a terrain effect regardless of the table's actual terrain: the
    /// difficult-terrain move cap (<see cref="ECountAsTerrain.Difficult"/>) or the dangerous-terrain
    /// per-model wound roll (<see cref="ECountAsTerrain.Dangerous"/>). Covers the "counts as being in
    /// X Terrain once" spell family (Cursed Stride, Aura of Pestilence) — granted one-shot via
    /// <see cref="AddRule"/>, read by <c>MovementRuleQueries.CountsAsInTerrain</c> throughout the move,
    /// and spent by the move-resolved consumption in <c>ExecuteMoveStage</c>. An
    /// <see cref="IgnoreTerrainEffects"/> bearer (Strider/Flying, per its scope) still waives the effect.
    /// </summary>
    public sealed record CountAsInTerrain(ECountAsTerrain Terrain) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.CountAsInTerrain(Terrain));
        }
    }

    /// <summary>
    /// The target takes a dangerous-terrain test immediately, standing still — one d6 per wound it would
    /// take to destroy it (3.6, #420), a wound to the unit on each 1 — rather than the next time it moves. The immediate arm of #197 P8's Dangerous
    /// Terrain Debuff, which two books (Lust Disciples, War Disciples) word as "must immediately take a
    /// Dangerous Terrain test" where the other four say "counts as being in Dangerous Terrain once".
    /// <para>
    /// Deliberately NOT modelled as <see cref="CountAsInTerrain"/> with an immediate trigger: that arm
    /// only bites if the victim moves, so a unit that holds still would shrug the debuff off entirely,
    /// inverting the rule. A Flying target still waives it, per the shared terrain-ignore rule.
    /// </para>
    /// </summary>
    public sealed record DangerousTerrainTest : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.InvokeDangerousTerrainTest(ruleInvocation.EffectiveTarget));
        }
    }

    /// <summary>
    /// #100 #14 mark/tag: places a <see cref="TokenType.Mark"/> on the target enemy carrying the rule named
    /// <see cref="RuleName"/>. The mark sits inert on the enemy until a friendly unit attacks it: the
    /// attacker-side claim (in <c>DetermineHitRollStage</c>) transfers <see cref="RuleName"/> to that
    /// attacker as a one-attack grant and removes the mark — so the FIRST friendly attack into the marked
    /// enemy gains the rule, dice-independent, and no later attack benefits. Covers the "pick an enemy,
    /// friendly units get X against it once" spell family (#034). Applied to the picked enemy by the cast
    /// path, exactly like any other token-granting effect.
    /// </summary>
    public sealed record MarkTarget(string RuleName) : Effect
    {
        public override void Apply(RuleInvocation ruleInvocation, List<RuleOperation> operations)
        {
            operations.Add(new RuleOperation.GrantTokenToUnit(
                ruleInvocation.EffectiveTarget,
                new Token(TokenType.Mark, 1, new TokenClearTrigger.ManualOnly(),
                    Payload: new TokenPayload.RuleGrant(RuleName, ELifetime.ThisAttack))));
        }
    }
}
