using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Tests
{
    // #435 - the OPR 3.6 AoF supplement sweep: the definitions it (re-)authored in FdgRaylib/Assets/Books (Banner and
    // Musician in GdfRuleSupplement.json, which AoF books merge first; the rest in AofRuleSupplement.json), hand-built
    // because the engine suite cannot read the app's supplements (the GdfSweep36Definitions convention). The app-side
    // AofSweep36ShippedDataTests asserts the authored JSON has exactly these entries with the 3.6.0 text, so the engine
    // tests and the data cannot drift apart.
    internal static class AofSweep36Definitions
    {
        // AoF Core Rules v3.6.0, "Command Groups" (the PDF's wording; Army Forge's common-rules JSON prints
        // "shooting/melee" and "moves" - #435 D2).
        public const string BannerText =
            "This model and its unit get +1 to Morale Test rolls triggered by shooting or melee.";
        public const string MusicianText = "This model and its unit move +1\" when using move actions.";
        public const string GreatSergeantText = "This model gets AP(+2) when attacking.";
        public const string GreatBannerText =
            "Once per activation, before attacking, pick one friendly unit within 12\", which gets +1 to Morale Test " +
            "rolls triggered by shooting/melee once (next time the effect would apply).";
        public const string GreatMusicianText =
            "Once per activation, before attacking, pick one friendly unit within 12\", which moves +1\" when using " +
            "move actions once (next time the effect would apply).";

        private static SpecialRuleDefinition Unit(string name, string description, params HookEntry[] passive) =>
            new(name, passive, System.Array.Empty<ActivatedAbility>(), ERuleScope.Unit,
                Valence: EValence.Positive, Description: description);

        /// <summary>Banner (3.6: only tests triggered by shooting or melee; 3.5 helped every morale test). Unit scope -
        /// "this model and its unit" - so the champion upgrade folds unit-wide.</summary>
        public static SpecialRuleDefinition Banner() => Unit("Banner", BannerText,
            new HookEntry(EHookID.Morale_OnPreMoraleTest, new Condition.MoraleTestFromShootingOrMelee(),
                new Effect.RollModifier(ERollKind.Morale, 1), ELifetime.ThisActivation));

        /// <summary>Musician (3.6 text; the shape - +1" on each move action, one entry per action so the three-action
        /// budget fold counts it once - is unchanged).</summary>
        public static SpecialRuleDefinition Musician() => Unit("Musician", MusicianText,
            MoveBonus(EActionType.Advance), MoveBonus(EActionType.Rush), MoveBonus(EActionType.Charge));

        private static HookEntry MoveBonus(EActionType action) => new(EHookID.Movement_OnMoveActionDeclared,
            new Condition.ActionTypeIs(action), new Effect.MovementBonus(action, 1f), ELifetime.ThisActivation);

        /// <summary>Great Sergeant (3.6: AP(+2) when attacking; 3.5 was a 5-6 extra-hit body). Sergeant's weapon-scoped
        /// shape at -2, so the champion marking (ListCompiler) puts it on the bought model's own weapons.</summary>
        public static SpecialRuleDefinition GreatSergeant() => new("Great Sergeant",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.Always(),
                    new Effect.RollModifier(ERollKind.Save, -2), ELifetime.ThisAttack),
            },
            System.Array.Empty<ActivatedAbility>(), ERuleScope.Weapon, Valence: EValence.Positive,
            Description: GreatSergeantText);

        /// <summary>Great Banner (3.6): a friendly unit within 12" gets Banner once - the one-shot grant carries Banner's
        /// own shooting-or-melee gate (3.5 granted Courage, every test).</summary>
        public static SpecialRuleDefinition GreatBanner() => Buff("Great Banner", "Banner", GreatBannerText);

        /// <summary>Great Musician (3.6 text): a friendly unit within 12" gets Musician once (unchanged grant).</summary>
        public static SpecialRuleDefinition GreatMusician() => Buff("Great Musician", "Musician", GreatMusicianText);

        private static SpecialRuleDefinition Buff(string name, string granted, string description) => new(name,
            System.Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerActivation(),
                    new TargetSelector(12f, 1, 1, ETargetAffinity.Friend, RequireLineOfSight: false),
                    new Effect.AddRule(granted, ELifetime.NextTrigger), new Condition.Always()),
            },
            ERuleScope.Unit, Valence: EValence.Positive, Description: description);
    }
}
