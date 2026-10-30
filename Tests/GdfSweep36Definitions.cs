using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Tests
{
    // #434 - the OPR 3.6 GDF supplement sweep: the definitions it (re-)authored in FdgRaylib/Assets/Books/GdfRuleSupplement.json
    // on existing primitives, hand-built because the engine suite cannot read the app's supplements (the
    // RangeConditional36Definitions / WeaponRules36Definitions convention). The app-side GdfSweep36ShippedDataTests asserts
    // the authored JSON has exactly these entries with the 3.6.0 text, so the engine tests and the data cannot drift apart.
    // One shape stands for each family; the siblings named in each comment share it entry for entry.
    internal static class GdfSweep36Definitions
    {
        private static Condition MostModels => new Condition.MostModelsHaveThisRule();
        private static Condition NotSpell => new Condition.IsNotSpell();
        private static Condition NotMelee => new Condition.Not(new Condition.IsMelee());

        public const string SpellResistanceText =
            "When a unit where most models have this rule takes wounds, roll one die for each. If the wound was from a " +
            "spell, then it is ignored on a 2+. If the wound was not from a spell, then it is ignored on a 6+ instead.";
        public const string DodgeText =
            "If most models in this unit have this rule, as long as they aren't Fatigued, enemy units get -1 to hit " +
            "rolls in melee against them.";
        public const string EntrenchedText =
            "If most models in this unit have this rule, as long as they haven't moved during this round, enemy units " +
            "shooting at them from over 12\" away get -1 to hit rolls.";
        public const string FortifiedGrowthText =
            "If most models in this unit have this rule,  place one token on it at the beginning of each round if it's " +
            "on the table and isn't Shaken, up to a max. of four tokens. For every two tokens, enemies attacking this " +
            "unit get AP(-1) to a min. of AP(0).";
        public const string GuardianText =
            "When units where most models have this rule take hits, those hits count as having AP(-1), to a min. of " +
            "AP(0).";
        public const string ShatterText = "For each unmodified result of 6 to hit, those hits get AP(+3).";
        public const string SergeantText = "This model gets AP(+1) when attacking.";
        public const string GroundedPrecisionText =
            "If most models in this unit are within 1\" of terrain, this model gets +1 to hit rolls when attacking.";
        public const string PiercingMarksmanText =
            "When shooting at enemies over 9\" away, this model's weapons get AP(+1).";
        public const string PiercingChargeText = "This model gets AP(+1) on its weapons when charging.";
        public const string VersatileExpertText =
            "When this unit is activated, pick one effect: until the end of the activation all models with this rule " +
            "in this unit either get AP(+1) when attacking, or get +1 to hit rolls when attacking.";
        public const string InfiltratingPrecisionStrikeText =
            "Counts as having Infiltrate, and gets +1 to hit rolls in melee on the round in which it deploys via this " +
            "rule.";

        private static SpecialRuleDefinition Unit(string name, string description, params HookEntry[] passive) =>
            new(name, passive, System.Array.Empty<ActivatedAbility>(), ERuleScope.Unit,
                Valence: EValence.Positive, Description: description);

        /// <summary>Spell Resistance (new in 3.6): Knightborn's spell arm, and an UNCONDITIONAL 6+ arm for every other wound.</summary>
        public static SpecialRuleDefinition SpellResistance() => Unit("Spell Resistance", SpellResistanceText,
            new HookEntry(EHookID.Shooting_OnSaveRollComplete, new Condition.And(new Condition.IsSpell(), MostModels),
                new Effect.IgnoreWoundOnRoll(2), ELifetime.ThisAttack, ERuleSeat.Subject),
            new HookEntry(EHookID.Shooting_OnSaveRollComplete, new Condition.And(NotSpell, MostModels),
                new Effect.IgnoreWoundOnRoll(6), ELifetime.ThisAttack, ERuleSeat.Subject));

        /// <summary>Dodge (new in 3.6): Melee Evasion's -1, gated on most models and on the bearer not counting as Fatigued.</summary>
        public static SpecialRuleDefinition Dodge() => Unit("Dodge", DodgeText,
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.And(new Condition.IsMelee(),
                    new Condition.And(MostModels, new Condition.Not(new Condition.CountsAsFatiguedInMelee()))),
                new Effect.RollModifier(ERollKind.Hit, -1), ELifetime.ThisAttack, ERuleSeat.Subject));

        /// <summary>Entrenched (3.6: 12"/-1, unmoved this round): the defensive facet of Artillery, entry for entry.</summary>
        public static SpecialRuleDefinition Entrenched() => Unit("Entrenched", EntrenchedText,
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.And(MostModels, new Condition.And(
                    new Condition.Not(new Condition.TokenPresent(TokenType.MovedThisRound)),
                    new Condition.DistanceGreaterThan(12f))),
                new Effect.RollModifier(ERollKind.Hit, -1), ELifetime.ThisAttack, ERuleSeat.Subject));

        public static readonly TokenType FortifiedGrowthMarker = new("FortifiedGrowthMarker");

        /// <summary>Fortified Growth (3.6 wording; the 3.5 copy ticked at round END, per token, and cleared on Shaken):
        /// Defensive Growth's round-start clock, AP(-1) for attackers per two tokens.</summary>
        public static SpecialRuleDefinition FortifiedGrowth() => Unit("Fortified Growth", FortifiedGrowthText,
            new HookEntry(EHookID.Round_OnRoundStart,
                new Condition.And(MostModels, new Condition.And(
                    new Condition.Not(new Condition.TokenPresent(TokenType.Shaken)),
                    new Condition.And(new Condition.Not(new Condition.TokenPresent(TokenType.InReserve)),
                        new Condition.And(new Condition.Not(new Condition.TokenPresent(TokenType.EmbarkedIn)),
                            new Condition.Not(new Condition.TokenPresent(TokenType.OffTableFromForcedMove)))))),
                new Effect.GrantToken(FortifiedGrowthMarker, new ValueSource.Literal(1),
                    new TokenClearTrigger.ManualOnly(), MaxTotal: 4),
                ELifetime.ThisRound),
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.And(MostModels, new Condition.TokenPresent(FortifiedGrowthMarker, MinCount: 2)),
                new Effect.TokenScaledReduceArmorPenetration(FortifiedGrowthMarker, PerMarkers: 2, MaxReduction: 2),
                ELifetime.ThisAttack, ERuleSeat.Subject));

        /// <summary>Guardian; also Primeborn (3.6: unconditional - Fortified's shape, no "shot from over 9"" gate).</summary>
        public static SpecialRuleDefinition Guardian(string name = "Guardian") => Unit(name, GuardianText,
            new HookEntry(EHookID.Shooting_OnHitRollComplete, MostModels, new Effect.ReduceArmorPenetration(1),
                ELifetime.ThisAttack, ERuleSeat.Subject));

        /// <summary>Shatter (3.6: AP(+3) on natural 6s; 3.5 was AP(+2) against Tough(3)+). Fracture's shape.</summary>
        public static SpecialRuleDefinition Shatter() => new("Shatter",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.UnmodifiedRollEquals(6),
                    new Effect.PerHitSaveModifier(OnRollValue: 6, Delta: -3), ELifetime.ThisAttack),
            },
            System.Array.Empty<ActivatedAbility>(), ERuleScope.Weapon, Valence: EValence.Positive,
            Description: ShatterText);

        /// <summary>Sergeant (3.6: AP(+1) when attacking; 3.5 was a Surge body). Weapon scope (the champion marking, #197).</summary>
        public static SpecialRuleDefinition Sergeant() => new("Sergeant",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.Always(),
                    new Effect.RollModifier(ERollKind.Save, -1), ELifetime.ThisAttack),
            },
            System.Array.Empty<ActivatedAbility>(), ERuleScope.Weapon, Valence: EValence.Positive,
            Description: SergeantText);

        /// <summary>Grounded Precision (3.6: the terrain gate alone - no "where most models have this rule").</summary>
        public static SpecialRuleDefinition GroundedPrecision() => Unit("Grounded Precision", GroundedPrecisionText,
            new HookEntry(EHookID.Shooting_OnHitRollModifier, new Condition.MostModelsWithinInchesOfTerrain(1f),
                new Effect.RollModifier(ERollKind.Hit, 1), ELifetime.ThisAttack));

        /// <summary>Piercing Marksman (new in 3.6): AP(+1) shooting over 9" - not in melee, not on a spell's hits.</summary>
        public static SpecialRuleDefinition PiercingMarksman() => Unit("Piercing Marksman", PiercingMarksmanText,
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.And(NotMelee, new Condition.And(NotSpell, new Condition.DistanceGreaterThan(9f))),
                new Effect.RollModifier(ERollKind.Save, -1), ELifetime.ThisAttack));

        /// <summary>Piercing Charge (new in 3.6): Piercing Assault's shape, AP(+1) when charging.</summary>
        public static SpecialRuleDefinition PiercingCharge() => Unit("Piercing Charge", PiercingChargeText,
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.And(new Condition.IsMelee(), new Condition.IsCharging()),
                new Effect.RollModifier(ERollKind.Save, -1), ELifetime.ThisAttack));

        /// <summary>Versatile Expert (new in 3.6): Versatile Warrior's activation-start pick without the range gate.</summary>
        public static SpecialRuleDefinition VersatileExpert() => new("Versatile Expert",
            System.Array.Empty<HookEntry>(),
            new[]
            {
                SelfPick("Versatile Expert (Piercing)", "AP(+1)"),
                SelfPick("Versatile Expert (Precision)", "+1 to hit"),
            },
            ERuleScope.Unit, Valence: EValence.Positive, Description: VersatileExpertText);

        private static ActivatedAbility SelfPick(string helper, string label) => new(
            EHookID.Activation_OnActivationStart, new Cost.OncePerActivation(),
            new TargetSelector(0f, 1, 1, ETargetAffinity.Self, RequireLineOfSight: false),
            new Effect.AddRule(helper, ELifetime.ThisActivation), new Condition.Always(), label);

        /// <summary>The AP helper: every attack but a spell's hits (Versatile Warrior's helpers exclude spells too).</summary>
        public static SpecialRuleDefinition VersatileExpertPiercing() => Unit("Versatile Expert (Piercing)",
            "AP(+1) when attacking, for this activation.",
            new HookEntry(EHookID.Shooting_OnHitRollComplete, NotSpell, new Effect.RollModifier(ERollKind.Save, -1),
                ELifetime.ThisAttack));

        public static SpecialRuleDefinition VersatileExpertPrecision() => Unit("Versatile Expert (Precision)",
            "+1 to hit rolls when attacking, for this activation.",
            new HookEntry(EHookID.Shooting_OnHitRollModifier, new Condition.Always(),
                new Effect.RollModifier(ERollKind.Hit, 1), ELifetime.ThisAttack));

        /// <summary>Infiltrating Precision Strike (new in 3.6): Infiltrate's deferral plus Ambushing Piercing Shot's
        /// arrival-round gate, on a melee +1 to hit.</summary>
        public static SpecialRuleDefinition InfiltratingPrecisionStrike() => Unit("Infiltrating Precision Strike",
            InfiltratingPrecisionStrikeText,
            new HookEntry(EHookID.Deployment_OnPreDeploymentSelect, new Condition.Always(),
                new Effect.DeferDeployment(EDeferTiming.LaterRound, PlacementRangeInches: 3f), ELifetime.UntilEndOfGame),
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.And(new Condition.IsMelee(), new Condition.TokenPresent(TokenType.ArrivedFromReserve)),
                new Effect.RollModifier(ERollKind.Hit, 1), ELifetime.ThisAttack));
    }
}
