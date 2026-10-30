using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FDG.Tests
{
    // #424 - the hand-built copy of the Assault definition the GDF supplement ships (FdgRaylib/Assets/Books/
    // GdfRuleSupplement.json; AoF reads the same file). The engine suite cannot read the app's supplements, so
    // the mechanics are pinned against this copy and the app-side AssaultShippedDataTests pins that the authored
    // JSON is exactly this, entry for entry (the WeaponRules36Definitions convention).
    internal static class AssaultDefinitions
    {
        public const string Text = "Counts as both a ranged weapon and a melee weapon.";

        public static SpecialRuleDefinition Assault { get; } = new SpecialRuleDefinition("Assault",
            new[]
            {
                new HookEntry(EHookID.Lifecycle_OnCapabilityQuery, new Condition.Always(),
                    new Effect.EnableMeleeUse(), ELifetime.UntilEndOfGame),
                // Melee arm only (#424 D3, owner): every Assault weapon has a printed range, so a ranged arm would
                // only restate it, and a range-0 Assault weapon would be a senseless 0" gun.
            },
            Array.Empty<ActivatedAbility>(),
            Scope: ERuleScope.Weapon,
            Valence: EValence.Positive,
            Description: Text);

        public static Weapon HeavyPistol(string name = "Heavy Pistol", int attacks = 1, int ap = 1)
        {
            var pistol = new Weapon(name, rangeInches: 12f, attacks: attacks, armorPenetration: ap);
            pistol.AttachRuleDefinition(new ResolvedRule("Assault", Assault));
            return pistol;
        }
    }

    // Core Rules v3.6.0 Assault: "Counts as both a ranged weapon and a melee weapon." (#424, plan Q12).
    //
    // Assault is NOT a name-based marker. It is an ordinary definition whose effect grants a weapon ROLE through
    // the capability convention (CapabilityRuleQueries: "capabilities are asked for, not tested for"): a weapon
    // rule authoring Effect.EnableMeleeUse / EnableRangedUse at Lifecycle_OnCapabilityQuery is read by
    // WeaponRoleQueries, and the single classification seam IWeaponExtensions.IsMelee / IsRanged widens by the
    // grant. Roles are grants only, resolved once when the weapon is built and cached on it.
    //
    // This fixture pins the seam itself: a weapon with a range AND Assault is both; a plain pistol is ranged only;
    // a range-0 sword is melee only; a bare weapon with no unit and no table answers; the answer is cached; the
    // grant comes from the EFFECT, so a differently named rule confers it and a rule merely NAMED Assault does
    // not; and the definition itself is sound (fire-lint, JSON kinds).
    [TestFixture]
    public class AssaultRuleIntegrationTests
    {
        // --- the seam ---------------------------------------------------------------------------------------

        [Test]
        public void ARangedWeaponWithAssault_IsBothRangedAndMelee()
        {
            Weapon pistol = AssaultDefinitions.HeavyPistol();

            Assert.That(pistol.IsRanged(), Is.True, "it keeps its 12\" shot");
            Assert.That(pistol.IsMelee(), Is.True, "and counts as a melee weapon too");
        }

        [Test]
        public void APlainPistol_IsRangedOnly()
        {
            var pistol = new Weapon("Pistol", rangeInches: 12f, attacks: 1, armorPenetration: 0);

            Assert.That(pistol.IsRanged(), Is.True);
            Assert.That(pistol.IsMelee(), Is.False, "no grant, range 12: not a melee weapon");
        }

        [Test]
        public void ARangeZeroSword_IsMeleeOnly()
        {
            var sword = new Weapon("Sword", rangeInches: 0f, attacks: 2, armorPenetration: 0);

            Assert.That(sword.IsMelee(), Is.True);
            Assert.That(sword.IsRanged(), Is.False, "no grant, range 0: not a ranged weapon");
        }

        // The seam runs from the Forge card, the list compiler and the importer, none of which has a unit or a
        // table - so the query must answer off the weapon's own resolved rules alone.
        [Test]
        public void ABareWeapon_WithNoUnitAndNoTable_Answers()
        {
            Weapon pistol = AssaultDefinitions.HeavyPistol();

            Assert.That(WeaponRoleQueries.Resolve(pistol), Is.EqualTo(EWeaponRoles.Melee));
            Assert.That(WeaponRoleQueries.GrantsMeleeUse(pistol), Is.True);
            Assert.That(WeaponRoleQueries.GrantsRangedUse(pistol), Is.False, "its shot comes from its printed range (D3)");
            Assert.That(WeaponRoleQueries.MeleeUseSource(pistol), Is.EqualTo("Assault"),
                "the *Source form names the rule the army wrote, for UI attribution");
            Assert.That(WeaponRoleQueries.Resolve(new Weapon("Pistol", 12f, 1, 0)), Is.EqualTo(EWeaponRoles.None));
            Assert.That(WeaponRoleQueries.MeleeUseSource(new Weapon("Sword", 0f, 1, 0)), Is.Null,
                "a sword's melee role comes from its range, not from a rule");
        }

        // The EffectKey-at-load precedent (#239): the seam runs in the AI's search and per frame in the overlays,
        // so the grant is resolved once per weapon and read back from the cache; attaching a rule resets it.
        [Test]
        public void TheRoles_AreResolvedOnce_AndCachedOnTheWeapon()
        {
            var pistol = new Weapon("Heavy Pistol", rangeInches: 12f, attacks: 1, armorPenetration: 1);
            Assert.That(pistol.RolesResolved, Is.False, "nothing asked yet");

            Assert.That(pistol.IsMelee(), Is.False);
            Assert.That(pistol.RolesResolved, Is.True, "the first query resolves and caches");
            Assert.That(pistol.GrantedRoles, Is.EqualTo(EWeaponRoles.None));

            pistol.AttachRuleDefinition(new ResolvedRule("Assault", AssaultDefinitions.Assault));
            Assert.That(pistol.RolesResolved, Is.False, "a rule change invalidates the cache, like the profile key");
            Assert.That(pistol.IsMelee(), Is.True, "and the next query sees the grant");
            Assert.That(pistol.RolesResolved, Is.True);
            Assert.That(pistol.GrantedRoles, Is.EqualTo(EWeaponRoles.Melee));
            Assert.That(pistol.IsMelee(), Is.True, "a second query reads the cache (no re-resolution: still resolved)");
            Assert.That(pistol.RolesResolved, Is.True);
        }

        [Test]
        public void TheRoles_SurviveTheWholeStoreClone()
        {
            Weapon pistol = AssaultDefinitions.HeavyPistol();
            Assume.That(pistol.IsMelee(), Is.True);

            var clone = new Weapon(pistol);

            Assert.That(clone.RolesResolved, Is.True, "the clone copies the resolved answer rather than re-asking");
            Assert.That(clone.IsMelee(), Is.True);
            Assert.That(clone.IsRanged(), Is.True);
        }

        // #325: a weapon that crosses a Newtonsoft hop (a stage request reaching a remote player) arrives with its
        // rules rehydrated from the persisted blob; the roles must follow, or the remote melee panel would drop
        // the pistol.
        [Test]
        public void TheRoles_SurviveTheWire()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            Weapon pistol = AssaultDefinitions.HeavyPistol();

            string json = JsonConvert.SerializeObject(pistol, store.GetJsonSettings());
            var back = JsonConvert.DeserializeObject<Weapon>(json, store.GetJsonSettings())!;

            Assert.That(back.RuleDefinitions.Select(r => r.Definition.Name), Is.EqualTo(new[] { "Assault" }));
            Assert.That(back.IsMelee(), Is.True);
            Assert.That(back.IsRanged(), Is.True);
        }

        // The capability convention's point: the grant is the EFFECT, not the name.
        [Test]
        public void AnyRuleAuthoringTheEffect_GrantsTheRole_ARuleMerelyNamedAssault_DoesNot()
        {
            var sidearm = new SpecialRuleDefinition("Sidearm",
                new[]
                {
                    new HookEntry(EHookID.Lifecycle_OnCapabilityQuery, new Condition.Always(),
                        new Effect.EnableMeleeUse(), ELifetime.UntilEndOfGame),
                },
                Array.Empty<ActivatedAbility>(), Scope: ERuleScope.Weapon);
            var pistol = new Weapon("Pistol", 12f, 1, 0);
            pistol.AttachRuleDefinition(new ResolvedRule("Sidearm", sidearm));

            Assert.That(pistol.IsMelee(), Is.True, "a rule that is not Assault confers the same role");
            Assert.That(pistol.IsRanged(), Is.True, "from its range - Sidearm grants melee use only");
            Assert.That(WeaponRoleQueries.MeleeUseSource(pistol), Is.EqualTo("Sidearm"));

            var impostor = new SpecialRuleDefinition("Assault", Array.Empty<HookEntry>(),
                Array.Empty<ActivatedAbility>(), Scope: ERuleScope.Weapon);
            var decoy = new Weapon("Pistol", 12f, 1, 0);
            decoy.AttachRuleDefinition(new ResolvedRule("Assault", impostor));

            Assert.That(decoy.IsMelee(), Is.False, "the name alone confers nothing - no entry, no role");
        }

        // Roles are grants only: nothing takes a role away, so two role rules can only widen the set.
        [Test]
        public void Roles_OnlyWiden_TwoGrantsAreOneAnswer()
        {
            Weapon pistol = AssaultDefinitions.HeavyPistol();
            var sidearm = new SpecialRuleDefinition("Sidearm",
                new[]
                {
                    new HookEntry(EHookID.Lifecycle_OnCapabilityQuery, new Condition.Always(),
                        new Effect.EnableMeleeUse(), ELifetime.UntilEndOfGame),
                },
                Array.Empty<ActivatedAbility>(), Scope: ERuleScope.Weapon);
            pistol.AttachRuleDefinition(new ResolvedRule("Sidearm", sidearm));

            Assert.That(pistol.GrantedRoles, Is.EqualTo(EWeaponRoles.Melee), "two melee grants, one flag");
            Assert.That(pistol.IsMelee(), Is.True);
        }

        // --- the definition -------------------------------------------------------------------------------

        [Test]
        public void TheDefinition_PassesTheFireLint()
        {
            IReadOnlyList<string> problems = RuleFireLint.Check(AssaultDefinitions.Assault);

            Assert.That(problems, Is.Empty, string.Join(Environment.NewLine, problems));
        }

        [Test]
        public void TheDefinition_ValidatesForAuthoring()
        {
            Assert.That(new RuleValidator().ValidateAuthoring(AssaultDefinitions.Assault), Is.Empty);
        }

        // The JSON kinds the supplement is written in: enableMeleeUse / enableRangedUse round-trip to the effects.
        [Test]
        public void TheDefinition_RoundTripsThroughTheSupplementJson()
        {
            string json = System.Text.Json.JsonSerializer.Serialize(AssaultDefinitions.Assault, RuleJson.Options);
            Assert.That(json, Does.Contain("\"enableMeleeUse\"").And.Not.Contain("\"enableRangedUse\""),
                "the shipped shape is the melee arm alone (D3)");

            SpecialRuleDefinition back =
                System.Text.Json.JsonSerializer.Deserialize<SpecialRuleDefinition>(json, RuleJson.Options)!;

            Assert.That(back.Passive, Is.EqualTo(AssaultDefinitions.Assault.Passive));
            Assert.That(back.Scope, Is.EqualTo(ERuleScope.Weapon));
            Assert.That(back.Description, Is.EqualTo(AssaultDefinitions.Text));
        }

        // The capability family stays out of the game log (CapabilityOperation): asking what a weapon may be used
        // as is a question, not an event.
        [Test]
        public void TheRoleOperations_AreCapabilityAnswers()
        {
            Assert.That(new RuleOperation.EnableMeleeUse(), Is.InstanceOf<CapabilityOperation>());
            Assert.That(new RuleOperation.EnableRangedUse(), Is.InstanceOf<CapabilityOperation>());
        }
    }
}
