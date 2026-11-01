using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Tests
{
    // #431 - the OPR 3.6 weapon-rule family as authored in FdgRaylib/Assets/Books/GdfRuleSupplement.json (the AoF
    // supplement inherits the GDF names). Hand-built because the engine suite cannot read the app's supplement -
    // the same convention as UnwieldyRuleIntegrationTests / ArmorRuleIntegrationTests. The app-side
    // WeaponRules36ShippedDataTests asserts the authored JSON has exactly these entries, so the two cannot drift
    // apart unnoticed.
    internal static class WeaponRules36Definitions
    {
        private static readonly Condition Always = new Condition.Always();

        private static HookEntry IgnoreRegeneration => new(EHookID.Shooting_OnSaveRollComplete, Always,
            new Effect.IgnoreRule("Regeneration"), ELifetime.ThisAttack);

        private static HookEntry IgnoreCover => new(EHookID.Shooting_OnSaveRollModifier, Always,
            new Effect.IgnoreCover(), ELifetime.ThisAttack);

        private static HookEntry Ap(Condition condition, int ap) => new(EHookID.Shooting_OnHitRollComplete,
            condition, new Effect.RollModifier(ERollKind.Save, -ap), ELifetime.ThisAttack);

        private static HookEntry Reroll(Condition condition, int minValue) => new(EHookID.Shooting_OnSaveRollComplete,
            condition, new Effect.Reroll(ERollKind.Save, new RerollCondition.OnUnmodifiedValue(minValue)),
            ELifetime.ThisAttack);

        private static SpecialRuleDefinition Weapon(string name, string description, params HookEntry[] passive) =>
            new(name, passive, System.Array.Empty<ActivatedAbility>(), ERuleScope.Weapon,
                Valence: EValence.Positive, Description: description);

        public static SpecialRuleDefinition AntiCover { get; } = Weapon("Anti-Cover",
            "This weapon ignores Cover.", IgnoreCover);

        public static SpecialRuleDefinition AntiRegen { get; } = Weapon("Anti-Regen",
            "This weapon ignores Regeneration.", IgnoreRegeneration);

        public static SpecialRuleDefinition Smash { get; } = Weapon("Smash",
            "When attacking units over half of this weapon's range away, the target must re-roll unmodified " +
            "Defense results of 6.",
            Reroll(new Condition.TargetOverHalfWeaponRange(), 6));

        public static SpecialRuleDefinition Pierce { get; } = Weapon("Pierce",
            "Ignores all positive Defense modifiers on the target, and when attacking units over half of this " +
            "weapon's range away, this weapon gets AP(+1).",
            IgnoreCover,
            new HookEntry(EHookID.Shooting_OnHitRollComplete, Always, new Effect.IgnorePositiveDefenseModifiers(),
                ELifetime.ThisAttack),
            Ap(new Condition.TargetOverHalfWeaponRange(), 1));

        public static SpecialRuleDefinition Fracture { get; } = Weapon("Fracture",
            "For each unmodified result of 6 to hit, those hits get AP(+2).",
            new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.UnmodifiedRollEquals(6),
                new Effect.PerHitSaveModifier(OnRollValue: 6, Delta: -2), ELifetime.ThisAttack));

        public static SpecialRuleDefinition Fragment { get; } = Weapon("Fragment",
            "When attacking units where most models have Defense 5+ to Defense 6+, the target must re-roll " +
            "successful unmodified Defense results of 5-6.",
            Reroll(new Condition.TargetMostModelsDefenseInRange(5, 6), 5));

        public static SpecialRuleDefinition Puncture { get; } = Weapon("Puncture",
            "Ignores Regeneration, and when attacking units where most models have Defense 2+ to Defense 3+, this " +
            "weapon gets AP(+2).",
            IgnoreRegeneration, Ap(new Condition.TargetMostModelsDefenseInRange(2, 3), 2));

        public static SpecialRuleDefinition Obliterate { get; } = Weapon("Obliterate",
            "Ignores Regeneration, and when attacking units where most models have Tough(3) or higher, the target " +
            "must re-roll successful unmodified Defense results of 5-6.",
            IgnoreRegeneration, Reroll(new Condition.TargetMajorityHasTough(3), 5));

        public static SpecialRuleDefinition Decimate { get; } = Weapon("Decimate",
            "When attacking units with more than half of their total size or Tough value (for single-model units), " +
            "the target must re-roll unmodified Defense results of 6.",
            Reroll(new Condition.TargetAboveHalfStrength(), 6));

        public static SpecialRuleDefinition Destructive { get; } = Weapon("Destructive",
            "When attacking units with more than half of their total size or Tough value (for single-model units), " +
            "those hits get AP(+1).",
            Ap(new Condition.TargetAboveHalfStrength(), 1));

        public static SpecialRuleDefinition Disintegrate { get; } = Weapon("Disintegrate",
            "Ignores Regeneration, and when attacking units where most models have Tough(3) or higher, this weapon " +
            "gets AP(+2).",
            IgnoreRegeneration, Ap(new Condition.TargetMajorityHasTough(3), 2));

        public static SpecialRuleDefinition Demolish { get; } = Weapon("Demolish",
            "Ignores Cover, and when attacking units with most models within 1\" of terrain, this weapon gets AP(+1).",
            IgnoreCover, Ap(new Condition.TargetMostModelsWithinInchesOfTerrain(1f), 1));

        public static SpecialRuleDefinition Purge { get; } = Weapon("Purge",
            "When attacking units that are Shaken or Fatigued, this weapon gets AP(+2).",
            Ap(new Condition.TargetShakenOrFatigued(), 2));

        public static SpecialRuleDefinition Reap { get; } = Weapon("Reap",
            "When attacking units that are Shaken or Fatigued, the target must re-roll successful unmodified " +
            "Defense results of 5-6.",
            Reroll(new Condition.TargetShakenOrFatigued(), 5));

        public static SpecialRuleDefinition Scourge { get; } = Weapon("Scourge",
            "For each unmodified result of 1 to block hits, enemies count as having taken 2 extra wounds when " +
            "checking who won melee, or if shooting wounds would trigger Morale Tests.",
            new HookEntry(EHookID.Shooting_OnSaveRollComplete, Always,
                new Effect.CountsAsWoundsTaken(OnRollValue: 1, WoundsPerDie: 2), ELifetime.ThisAttack));

        public static SpecialRuleDefinition Unwieldy { get; } = new("Unwieldy",
            new[]
            {
                new HookEntry(EHookID.Melee_OnCounterTrigger, Always, new Effect.StrikeLast(), ELifetime.ThisAttack),
            },
            System.Array.Empty<ActivatedAbility>(), ERuleScope.Weapon, Valence: EValence.Negative,
            Description: "Strikes last when charging.");

        public static SpecialRuleDefinition Imprecise { get; } = new("Imprecise",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollModifier, Always, new Effect.RollModifier(ERollKind.Hit, -1),
                    ELifetime.ThisAttack),
            },
            System.Array.Empty<ActivatedAbility>(), ERuleScope.Weapon, Valence: EValence.Negative,
            Description: "Gets -1 to hit when attacking.");

        // Strafing's 3.6 name: the text is word for word the one the catalog's Strafing carries, so the shipped
        // definition is the same activated ability under the new name.
        public static SpecialRuleDefinition CrossingStrike { get; } = new("Crossing Strike",
            System.Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Movement_OnMoveThroughEnemy, new Cost.OncePerActivation(),
                    new TargetSelector(1f, 1, 1, ETargetAffinity.Foe, false),
                    new Effect.AttackWithThisWeapon(),
                    new Condition.Always()),
            },
            ERuleScope.Weapon, Valence: EValence.Positive,
            Description: "Once per activation, when this model moves through enemy units, pick one of them and " +
                "attack it with this weapon as if it was shooting. This weapon may only be used in this way.");
    }
}
