using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #239 weapon effect sets. Load-time: a weapon entry's explicit EffectSet wins, else the army's
    // default for the weapon's ranged/melee kind, else null (front-end global default). Emission:
    // RollToHitStage's AttackBeat carries the weapon's key plus the true hit share (natural
    // successes / attacks rolled) — the roll happens before the beat now, without moving the roll's
    // position in the RNG sequence or the beat's position before the to-hit dice (#238).
    [TestFixture]
    public class WeaponEffectKeyTests
    {
        // ---------------- load-time resolution (army file entries -> Weapon.EffectKey) ----------------

        [Test]
        public void ExplicitWeaponKey_WinsOverArmyDefault()
        {
            List<Weapon> weapons = BuildWeapons("storm-tracer", "energy-blade",
                new WeaponFileEntry { Name = "Plasma Rifle", RangeInches = 24, Attacks = 1, EffectSet = "plasma-bolt" });

            Assert.That(weapons.Single().EffectKey, Is.EqualTo("plasma-bolt"),
                "an explicit per-weapon key beats the army default");
        }

        [Test]
        public void NullWeaponKey_FallsToArmyDefault_ForItsKind()
        {
            List<Weapon> weapons = BuildWeapons("storm-tracer", "energy-blade",
                new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 },
                new WeaponFileEntry { Name = "CCW", RangeInches = 0, Attacks = 1 });

            Assert.That(weapons.Single(w => w.Name == "Rifle").EffectKey, Is.EqualTo("storm-tracer"),
                "a keyless ranged weapon takes the ranged default");
            Assert.That(weapons.Single(w => w.Name == "CCW").EffectKey, Is.EqualTo("energy-blade"),
                "a keyless melee weapon takes the melee default");
        }

        [Test]
        public void NoKeyAndNoDefaults_StaysNull()
        {
            List<Weapon> weapons = BuildWeapons(null, null,
                new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 });

            Assert.That(weapons.Single().EffectKey, Is.Null,
                "with nothing set anywhere the key is null and the front-end's global default applies");
        }

        // ---------------- #424 B1: the second, derived MELEE key ----------------
        //
        // A weapon that is both ranged and melee (Assault) is one Weapon object used by two pipelines, so it needs
        // two presentation keys: its shot plays the ranged set, its swing the melee set. The melee key is DERIVED
        // from the army's melee default exactly like the first key - no field in WeaponFileEntry / .fdgarmy /
        // .fdgbook names it, and no effect-set table needs an entry for pistols.

        [Test]
        public void AnAssaultPistol_WithNoPerWeaponKey_CarriesTheRangedDefaultForItsShot_AndTheMeleeDefaultForItsSwing()
        {
            var entry = new WeaponFileEntry
            {
                Name = "Heavy Pistol", RangeInches = 12, Attacks = 1, ArmorPenetration = 1,
                SpecialRules = { new SpecialRuleEntry_Core("Assault") },
            };
            Assume.That(entry.EffectSet, Is.Null, "the point: nothing authored per weapon");

            Weapon pistol = BuildWeaponsWith(WithAssault(), "storm-tracer", "energy-blade", entry).Single();

            Assert.That(pistol.IsMelee() && pistol.IsRanged(), Is.True, "test setup: the resolver attached Assault");
            Assert.That(pistol.EffectKey, Is.EqualTo("storm-tracer"), "the shot plays the ranged default");
            Assert.That(pistol.MeleeEffectKey, Is.EqualTo("energy-blade"), "the swing plays the melee default");
        }

        [Test]
        public void AnExplicitKey_StaysWithTheRangedUse_TheSwingKeepsTheMeleeDefault()
        {
            var entry = new WeaponFileEntry
            {
                Name = "Plasma Pistol", RangeInches = 12, Attacks = 1, EffectSet = "plasma-bolt",
                SpecialRules = { new SpecialRuleEntry_Core("Assault") },
            };

            Weapon pistol = BuildWeaponsWith(WithAssault(), "storm-tracer", "energy-blade", entry).Single();

            Assert.That(pistol.EffectKey, Is.EqualTo("plasma-bolt"));
            Assert.That(pistol.MeleeEffectKey, Is.EqualTo("energy-blade"),
                "an entry's explicit set describes the shot, as it always has; the swing is the army's melee default");
        }

        [Test]
        public void ARangeZeroWeapon_SwingsWithItsOneKey_ExplicitOrDefault()
        {
            List<Weapon> weapons = BuildWeapons("storm-tracer", "energy-blade",
                new WeaponFileEntry { Name = "Chainsword", RangeInches = 0, Attacks = 2, EffectSet = "chain-blade" },
                new WeaponFileEntry { Name = "CCW", RangeInches = 0, Attacks = 1 });

            Weapon chainsword = weapons.Single(w => w.Name == "Chainsword");
            Assert.That(chainsword.MeleeEffectKey, Is.EqualTo("chain-blade"),
                "a sword has no shot, so its explicit set keeps applying to its swing");
            Assert.That(chainsword.MeleeEffectKey, Is.EqualTo(chainsword.EffectKey));
            Weapon ccw = weapons.Single(w => w.Name == "CCW");
            Assert.That(ccw.MeleeEffectKey, Is.EqualTo("energy-blade"));
        }

        [Test]
        public void ANonAssaultRifle_CarriesTheMeleeKeyToo_ButNoDefaultsMeansNull()
        {
            Weapon rifle = BuildWeapons("storm-tracer", "energy-blade",
                new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 }).Single();
            Assert.That(rifle.MeleeEffectKey, Is.EqualTo("energy-blade"), "carried like every weapon; a rifle never reads it");

            Weapon bare = BuildWeapons(null, null,
                new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 }).Single();
            Assert.That(bare.MeleeEffectKey, Is.Null, "no defaults anywhere: both keys null, the front-end global default applies");
        }

        [Test]
        public async Task AnAssaultPistolsSwing_EmitsTheMeleeKey_ItsShot_TheRangedKey()
        {
            var pistol = new Weapon("Heavy Pistol", rangeInches: 12f, attacks: 3, armorPenetration: 0,
                effectKey: "storm-tracer", meleeEffectKey: "energy-blade");

            RecordingPresenter swing = await RunVolley(face: 6, pistol, isMelee: true);
            RecordingPresenter shot = await RunVolley(face: 6, pistol, isMelee: false);

            Assert.That(swing.Beats.OfType<AttackBeat>().Single().WeaponEffect, Is.EqualTo("energy-blade"),
                "a pistol swing does not play the gunshot");
            Assert.That(shot.Beats.OfType<AttackBeat>().Single().WeaponEffect, Is.EqualTo("storm-tracer"));
        }

        private static List<Weapon> BuildWeapons(string? rangedDefault, string? meleeDefault,
            params WeaponFileEntry[] entries) => BuildWeaponsWith(null, rangedDefault, meleeDefault, entries);

        private static List<Weapon> BuildWeaponsWith(IRuleResolver? ruleResolver, string? rangedDefault,
            string? meleeDefault, params WeaponFileEntry[] entries)
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var unitEntry = new UnitFileEntry
            {
                Name = "U",
                ModelCount = 1,
                Quality = 4,
                Defense = 4,
                Weapons = entries.ToList(),
            };
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), unitEntry, store, ruleResolver,
                rangedDefault, meleeDefault);
            return ((IUnit)unit).AllWeapons(_ => true);
        }

        private static IRuleResolver WithAssault()
        {
            var resolver = new RuleResolver();
            resolver.Register(AssaultDefinitions.Assault);
            return resolver;
        }

        // ---------------- emission (RollToHitStage -> AttackBeat) ----------------

        [Test]
        public async Task AttackBeat_CarriesEffectKeyAndHitShare()
        {
            RecordingPresenter presenter = await RunMeleeVolley(face: 6, effectKey: "energy-blade");
            AttackBeat beat = presenter.Beats.OfType<AttackBeat>().Single();

            Assert.That(beat.WeaponEffect, Is.EqualTo("energy-blade"), "the weapon's key rides the beat");
            Assert.That(beat.AttackCount, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(beat.HitCount, Is.EqualTo(3f).Within(0.0001f),
                "face 6 clears the 4+ threshold on all 3 dice");
        }

        [Test]
        public async Task AttackBeat_OnAWhiff_CarriesZeroHits()
        {
            RecordingPresenter presenter = await RunMeleeVolley(face: 2, effectKey: null);
            AttackBeat beat = presenter.Beats.OfType<AttackBeat>().Single();

            Assert.That(beat.WeaponEffect, Is.Null, "no key anywhere -> null -> front-end global default");
            Assert.That(beat.HitCount, Is.EqualTo(0f).Within(0.0001f), "face 2 misses the 4+ threshold");
            Assert.That(beat.AttackCount, Is.EqualTo(3f).Within(0.0001f),
                "attacks rolled still ride along so the front-end can show all shots flying");
        }

        [Test]
        public async Task AttackBeat_StillPrecedesTheToHitDiceBeat()
        {
            RecordingPresenter presenter = await RunMeleeVolley(face: 6, effectKey: "energy-blade");

            int attackIndex = presenter.Beats.FindIndex(b => b is AttackBeat);
            int diceIndex = presenter.Beats.FindIndex(b => b is DiceRolledBeat);
            Assert.That(attackIndex, Is.GreaterThanOrEqualTo(0), "an attack beat must be emitted");
            Assert.That(diceIndex, Is.GreaterThanOrEqualTo(0), "a to-hit dice beat must be emitted");
            Assert.That(attackIndex, Is.LessThan(diceIndex),
                "#238: the attack overlaps the dice behind it, so it must still be emitted first " +
                "even though the roll itself now happens before both (#239)");
        }

        private static Task<RecordingPresenter> RunMeleeVolley(int face, string? effectKey) =>
            RunVolley(face, new Weapon("Energy Sword", rangeInches: 0f, attacks: 3, armorPenetration: 0,
                effectKey: effectKey), isMelee: true);

        private static async Task<RecordingPresenter> RunVolley(int face, Weapon weapon, bool isMelee)
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var presenter = new RecordingPresenter();
            // FixedFaceDiceRoller honors the roll count, so 3 attacks report 3 dice on the face.
            var ctx = new TestGameContext(store, new FixedFaceDiceRoller(face), presenter: presenter);

            DataBinding<UnitData> attacker = MakeUnit(store, new Position(0f, 5f), weapon);
            DataBinding<UnitData> defender = MakeUnit(store, new Position(1f, 5f), null);

            var stage = new RollToHitStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 1, isMelee: isMelee);
            metadata.AddResult(new DetermineHitRollResults(4, attackCount: 3));
            await stage.Enter(metadata);
            return presenter;
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, Position position, Weapon? weapon)
        {
            var weapons = weapon == null ? new List<Weapon>() : new List<Weapon> { weapon };
            var model = new ModelData(0.75f, weapons, position, store);
            DataBinding<ModelData> modelBinding = store.GetDataBinding<ModelData>(store.Create(model));

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return store.GetDataBinding<UnitData>(store.Create(unit));
        }
    }
}
