using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Tests
{
    // #432 - the OPR 3.6 range-conditional army-rule family as authored in FdgRaylib/Assets/Books/GdfRuleSupplement.json
    // and AofRuleSupplement.json. Hand-built because the engine suite cannot read the app's supplements - the
    // WeaponRules36Definitions convention. The app-side RangeConditional36ShippedDataTests asserts the authored JSON
    // has exactly these entries (with the 3.6.0 text), so the engine tests and the data cannot drift apart. One shape
    // stands for each family; the siblings named in each comment share it entry for entry.
    internal static class RangeConditional36Definitions
    {
        // The 9"/6" pair is each rule's DATA (the condition's fields), never an engine constant.
        public static Condition Attacking => new Condition.AttackingFromOver(ShootInches: 9f, ChargeInches: 6f);
        public static Condition Attacked => new Condition.AttackedFromOver(ShootInches: 9f, ChargeInches: 6f);
        private static Condition MostModels => new Condition.MostModelsHaveThisRule();

        public const string ExtraHitText =
            "When this model shoots/charges enemies over 9\"/6\" away, unmodified result of 6 to hit deal 1 extra " +
            "hit (only the original hit counts as a 6 for special rules).";
        public const string ApText = "When this model shoots/charges enemies over 9\"/6\" away, its weapons get AP(+1).";
        public const string ExtraWoundText =
            "When this model shoots/charges enemies over 9\"/6\" away, unmodified results of 1 to block hits from " +
            "weapons deal 1 extra wound.";
        public const string PickText =
            "When this unit is activated, pick one effect: until the end of the activation, when shooting/charging " +
            "enemies over 9\"/6\" away, all models with this rule in it either get AP(+1) or get +1 to hit rolls.";
        public const string MinusHitText =
            "If most models in this unit have this rule, enemy units shooting/charging them from over 9\"/6\" away " +
            "get -1 to hit rolls."; // #436 D-whitespace: OPR prints "rolls ." - the stray space is normalised
        public const string PlusDefenseText =
            "If most models in this unit have this rule, they get +1 to Defense rolls when shot/charged from over " +
            "9\"/6\" away.";
        public const string WardText =
            "When a unit where most models have this rule takes wounds, roll one die for each. If the wound was from " +
            "a spell, then it is ignored on a 2+. If the wound was not from a spell, and the unit was shot/charged " +
            "from over 9\"/6\" away, then it is ignored on a 6+ instead.";
        public const string ShroudText =
            "When a unit where most models have this rule is targeted by an enemy unit for shooting/charging, that " +
            "enemy unit gets -4\"/-2\" range to a min. of 6\".";
        public const string MoraleText =
            "If most models in this unit have this rule, they get +1 to Morale Test rolls when shot/charged from " +
            "over 9\"/6\" away.";
        public const string ReachText = "This model gets +4\"/+2\" range when shooting/charging.";
        public const string MoveText = "Moves +1\" when using Advance, and +2\" when using Rush/Charge.";

        private static SpecialRuleDefinition Unit(string name, string description, params HookEntry[] passive) =>
            new(name, passive, System.Array.Empty<ActivatedAbility>(), ERuleScope.Unit,
                Valence: EValence.Positive, Description: description);

        // --- offensive (Actor seat) ---------------------------------------------------------------------------

        /// <summary>Devout; also Ferocious, Brutal, Lucky.</summary>
        public static SpecialRuleDefinition ExtraHit(string name = "Devout") => Unit(name, ExtraHitText,
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.And(Attacking, new Condition.UnmodifiedRollEquals(6)),
                new Effect.AddExtraHit(OnRollValue: 6, Count: 1), ELifetime.ThisAttack));

        /// <summary>Havocbound; also Piercing Warrior.</summary>
        public static SpecialRuleDefinition ApPlusOne(string name = "Havocbound", string description = ApText) =>
            Unit(name, description,
                new HookEntry(EHookID.Shooting_OnHitRollComplete, Attacking,
                    new Effect.RollModifier(ERollKind.Save, -1), ELifetime.ThisAttack));

        /// <summary>Infected; also Violent, Warbound, Destroyer. Weapon scope, like the 3.5 copies.</summary>
        public static SpecialRuleDefinition ExtraWound(string name = "Infected") => new(name,
            new[]
            {
                new HookEntry(EHookID.Shooting_OnSaveRollComplete, Attacking,
                    new Effect.AddExtraWound(OnRollValue: 1, Count: 1), ELifetime.ThisAttack),
            },
            System.Array.Empty<ActivatedAbility>(), ERuleScope.Weapon, Valence: EValence.Positive,
            Description: ExtraWoundText);

        /// <summary>Versatile Warrior; also Watchborn, Vinci Tech (same text, same two helpers).</summary>
        public static SpecialRuleDefinition Pick(string name = "Versatile Warrior") => new(name,
            System.Array.Empty<HookEntry>(),
            new[]
            {
                SelfPick($"{name} (Piercing)", "AP(+1)"),
                SelfPick($"{name} (Precision)", "+1 to hit"),
            },
            ERuleScope.Unit, Valence: EValence.Positive, Description: PickText);

        public static SpecialRuleDefinition PickPiercing(string name = "Versatile Warrior") =>
            ApPlusOne($"{name} (Piercing)",
                "AP(+1) when shooting/charging enemies over 9\"/6\" away, for this activation.");

        public static SpecialRuleDefinition PickPrecision(string name = "Versatile Warrior") =>
            Unit($"{name} (Precision)",
                "+1 to hit rolls when shooting/charging enemies over 9\"/6\" away, for this activation.",
                new HookEntry(EHookID.Shooting_OnHitRollModifier, Attacking,
                    new Effect.RollModifier(ERollKind.Hit, +1), ELifetime.ThisAttack));

        private static ActivatedAbility SelfPick(string granted, string label) =>
            new(EHookID.Activation_OnActivationStart, new Cost.OncePerActivation(),
                new TargetSelector(RangeInches: 0f, MinCount: 1, MaxCount: 1, ETargetAffinity.Self,
                    RequireLineOfSight: false),
                new Effect.AddRule(granted, ELifetime.ThisActivation),
                new Condition.Always(),
                Label: label);

        // --- defensive (Subject seat) -------------------------------------------------------------------------

        /// <summary>Changebound; also Machine-Fog, Screened, Empyrean Spirit.</summary>
        public static SpecialRuleDefinition MinusHit(string name = "Changebound") => Unit(name, MinusHitText,
            new HookEntry(EHookID.Shooting_OnHitRollModifier, new Condition.And(Attacked, MostModels),
                new Effect.RollModifier(ERollKind.Hit, -1), ELifetime.ThisAttack, ERuleSeat.Subject));

        /// <summary>Guarded; also Sturdy.</summary>
        public static SpecialRuleDefinition PlusDefense(string name = "Guarded") => Unit(name, PlusDefenseText,
            new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.And(Attacked, MostModels),
                new Effect.RollModifier(ERollKind.Save, +1), ELifetime.ThisAttack, ERuleSeat.Subject));

        /// <summary>Knightborn; also Warding, Angelic Blessing.</summary>
        public static SpecialRuleDefinition Ward(string name = "Knightborn") => Unit(name, WardText,
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.And(new Condition.IsSpell(), MostModels),
                new Effect.IgnoreWoundOnRoll(MinRoll: 2), ELifetime.ThisAttack, ERuleSeat.Subject),
            // attackedFromOver is false for spell damage, so this arm is exactly "not from a spell, and shot/charged
            // from over 9"/6" away".
            new HookEntry(EHookID.Shooting_OnSaveRollComplete, new Condition.And(Attacked, MostModels),
                new Effect.IgnoreWoundOnRoll(MinRoll: 6), ELifetime.ThisAttack, ERuleSeat.Subject));

        /// <summary>Darkborn; also Shadowborn, Shrouded, Wild Veil.</summary>
        public static SpecialRuleDefinition Shroud(string name = "Darkborn") => new(name,
            new[]
            {
                new HookEntry(EHookID.Shooting_OnRangeCheck, MostModels,
                    new Effect.RangeModifier(Delta: -4, MinResultInches: 6, ChargeDelta: -2),
                    ELifetime.ThisActivation, ERuleSeat.Subject),
            },
            System.Array.Empty<ActivatedAbility>(), ERuleScope.Unit, Valence: EValence.Positive,
            Description: ShroudText);

        // --- morale (the tested unit, Actor seat at the pre-morale hook) ----------------------------------------

        /// <summary>Hive Bond; also Hold the Line, Valiant.</summary>
        public static SpecialRuleDefinition Morale(string name = "Hive Bond") => new(name,
            new[]
            {
                new HookEntry(EHookID.Morale_OnPreMoraleTest, new Condition.And(Attacked, MostModels),
                    new Effect.RollModifier(ERollKind.Morale, +1), ELifetime.ThisActivation),
            },
            System.Array.Empty<ActivatedAbility>(), ERuleScope.Unit, Valence: EValence.Positive,
            Description: MoraleText);

        // --- reach and movement (Actor seat) --------------------------------------------------------------------

        /// <summary>Highborn; also Extra Reach, Royal Legion.</summary>
        public static SpecialRuleDefinition Reach(string name = "Highborn") => new(name,
            new[]
            {
                new HookEntry(EHookID.Shooting_OnRangeCheck, new Condition.Always(),
                    new Effect.RangeModifier(Delta: 4, MinResultInches: 0, ChargeDelta: 2), ELifetime.ThisActivation),
            },
            System.Array.Empty<ActivatedAbility>(), ERuleScope.Unit, Valence: EValence.Positive,
            Description: ReachText);

        /// <summary>Lustbound; also Scurry (Agile is the catalog's, same numbers).</summary>
        public static SpecialRuleDefinition Move(string name = "Lustbound") => Unit(name, MoveText,
            MoveBonus(EActionType.Advance, 1f), MoveBonus(EActionType.Rush, 2f), MoveBonus(EActionType.Charge, 2f));

        private static HookEntry MoveBonus(EActionType action, float inches) =>
            new(EHookID.Movement_OnMoveActionDeclared, new Condition.ActionTypeIs(action),
                new Effect.MovementBonus(action, inches, MinResultInches: 0f), ELifetime.ThisActivation);
    }
}
