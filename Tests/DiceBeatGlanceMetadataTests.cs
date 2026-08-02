using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Network.Messages;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Presentation.Messages;
using FDG.Rules.Definitions;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #245 glance metadata on DiceRolledBeat: the roll's category (offense/defense/misc), a "who"
    // context line, modifier chips explaining the threshold arithmetic, and proc chips for
    // face-triggered rules. Each info block (modifiers, procs) stretches the beat so there is time
    // to read it: +400ms on the duration, +200ms on a held beat's lead-in. These tests pin the
    // duration math, the wire round-trip (networked clients must see the same chips), and the
    // stages' chip-composition helpers.
    [TestFixture]
    public class DiceBeatGlanceMetadataTests
    {
        private static DiceRolledBeat Beat(IReadOnlyList<string>? modifierTags = null,
            IReadOnlyList<string>? procTags = null) =>
            new(new List<float> { 0f, 1f, 0f, 2f, 0f, 1f }, 1, 4, ERandomnessType.Realistic,
                "Roll to Hit", "3 hits", held: true,
                category: ERollBeatCategory.Offense, context: "Warriors -> Gunners",
                modifierTags: modifierTags, procTags: procTags);

        [Test]
        public void NoInfoBlocks_KeepsTheBaseDurationAndLeadIn()
        {
            DiceRolledBeat beat = Beat();
            Assert.That(beat.NominalDuration, Is.EqualTo(PresentationDurations.DiceRoll),
                "context alone is a glance - no extension");
            Assert.That(beat.HoldLeadIn, Is.EqualTo(TimeSpan.FromMilliseconds(600)));
        }

        [Test]
        public void EachInfoBlock_StretchesTheBeat()
        {
            DiceRolledBeat withMods = Beat(modifierTags: new[] { "Quality 4+", "Stealth -1" });
            Assert.That(withMods.NominalDuration,
                Is.EqualTo(PresentationDurations.DiceRoll + TimeSpan.FromMilliseconds(400)));
            Assert.That(withMods.HoldLeadIn, Is.EqualTo(TimeSpan.FromMilliseconds(800)));

            DiceRolledBeat withBoth = Beat(modifierTags: new[] { "Quality 4+", "Stealth -1" },
                procTags: new[] { "Furious +2 on 6s" });
            Assert.That(withBoth.NominalDuration,
                Is.EqualTo(PresentationDurations.DiceRoll + TimeSpan.FromMilliseconds(800)));
            Assert.That(withBoth.HoldLeadIn, Is.EqualTo(TimeSpan.FromMilliseconds(1000)));
        }

        [Test]
        public void EmptyTagLists_CountAsNoInfo()
        {
            DiceRolledBeat beat = Beat(modifierTags: new List<string>(), procTags: new List<string>());
            Assert.That(beat.NominalDuration, Is.EqualTo(PresentationDurations.DiceRoll));
        }

        [Test]
        public void GlanceMetadata_SurvivesTheWireRoundTrip()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var serializer = new MessageSerializer(store);
            serializer.RegisterMessageType<PresentBeatMessage>();

            DiceRolledBeat original = Beat(modifierTags: new List<string> { "Quality 4+", "Stealth -1" },
                procTags: new List<string> { "Furious +2 on 6s" });

            ArraySegment<byte> bytes = serializer.SerializeMessage(new PresentBeatMessage(original));
            var received = serializer.DeserializeMessage(bytes) as PresentBeatMessage;
            Assert.That(received, Is.Not.Null);

            var dice = (DiceRolledBeat)received!.Beat;
            Assert.That(dice.Category, Is.EqualTo(ERollBeatCategory.Offense));
            Assert.That(dice.Context, Is.EqualTo("Warriors -> Gunners"));
            Assert.That(dice.ModifierTags, Is.EqualTo(new[] { "Quality 4+", "Stealth -1" }));
            Assert.That(dice.ProcTags, Is.EqualTo(new[] { "Furious +2 on 6s" }));
            Assert.That(dice.NominalDuration,
                Is.EqualTo(PresentationDurations.DiceRoll + TimeSpan.FromMilliseconds(800)),
                "a networked client must pace the stretched beat identically");
        }

        // ---------------- chip composition ----------------

        [Test]
        public void HitThresholdTags_NullWhenUnmodified_ChipsWhenModified()
        {
            var noMods = new List<(RuleOperation, string)>();
            Assert.That(DetermineHitRollStage<ICombatMetadata>.ComposeThresholdTags(4, noMods, 0, false),
                Is.Null, "an unmodified quality needs no chips (and no stretched beat)");

            var named = new List<(RuleOperation, string)>
            {
                (new RuleOperation.ApplyRollModifier(ERollKind.Hit, -1), "Stealth"),
                (new RuleOperation.ApplyRollModifier(ERollKind.Save, 1), "Shielded"), // wrong kind - skipped
            };
            List<string>? tags = DetermineHitRollStage<ICombatMetadata>.ComposeThresholdTags(4, named, 1, false);
            Assert.That(tags, Is.EqualTo(new[] { "Quality 4+", "Stealth -1", "buff +1" }));
        }

        [Test]
        public void HitThresholdTags_FatigueReadsAsItsOwnChip()
        {
            List<string>? tags = DetermineHitRollStage<ICombatMetadata>.ComposeThresholdTags(
                3, new List<(RuleOperation, string)>(), 0, fatiguedInMelee: true);
            Assert.That(tags, Is.EqualTo(new[] { "Quality 3+", "Fatigued: 6s only" }));
        }

        [Test]
        public void ProcTags_NameFaceTriggeredRules()
        {
            var named = new List<(RuleOperation, string)>
            {
                (new RuleOperation.InsertExtraHits(2f), "Furious"),
                (new RuleOperation.ApplyPerHitSaveModifier(6, -1), "Rending"),
                (new RuleOperation.ApplyRollModifier(ERollKind.Hit, 1), "Focus"), // not a proc - skipped
            };
            List<string> tags = RollToHitStage<ICombatMetadata>.ComposeProcTags(named);
            Assert.That(tags, Is.EqualTo(new[] { "Furious +2 on 6s", "Rending AP+1 on 6s" }));
        }

        [Test]
        public void SaveModifierTags_NameWholeAttackSaveRules()
        {
            var named = new List<(RuleOperation, string)>
            {
                (new RuleOperation.ApplyRollModifier(ERollKind.Save, 1), "Shielded"),
                (new RuleOperation.ApplyRollModifier(ERollKind.Save, -1), "Thrust"),
                (new RuleOperation.ReduceArmorPenetration(1), "Fortified"),
            };
            List<string>? tags = RollToHitStage<ICombatMetadata>.ComposeSaveModifierTags(named);
            Assert.That(tags, Is.EqualTo(new[] { "Shielded +1", "Thrust -1", "Fortified AP-1" }));

            Assert.That(RollToHitStage<ICombatMetadata>.ComposeSaveModifierTags(
                new List<(RuleOperation, string)>()), Is.Null);
        }

        [Test]
        public void SaveThresholdTags_ComposeTheAttackWideArithmetic()
        {
            Assert.That(DetermineSaveRollsNeededStage<ICombatMetadata>.ComposeThresholdTags(
                4, 0, 0, null, 0), Is.Null, "an unmodified defense needs no chips");

            List<string>? tags = DetermineSaveRollsNeededStage<ICombatMetadata>.ComposeThresholdTags(
                4, 2, 1, new List<string> { "Shielded +1" }, -1);
            Assert.That(tags, Is.EqualTo(new[] { "Defense 4+", "AP 2", "Cover +1", "Shielded +1", "buff -1" }));
        }

        // ---------------- to-hit beat caption (weapon + volley size) ----------------

        [Test]
        public void HitBeatLabel_NamesTheWeaponBeingRolledWith()
        {
            Assert.That(RollToHitStage<ICombatMetadata>.HitBeatLabel(
                    new Weapon("Heavy Rifle", rangeInches: 24f, attacks: 2, armorPenetration: 1)),
                Is.EqualTo("Roll to Hit - Heavy Rifle"));

            Assert.That(RollToHitStage<ICombatMetadata>.HitBeatLabel(
                    new Weapon("  ", rangeInches: 24f, attacks: 1, armorPenetration: 0)),
                Is.EqualTo("Roll to Hit"), "a nameless weapon falls back to the bare label");
        }

        [Test]
        public void HitBeatContext_CarriesTheMatchupAndTheVolleySize()
        {
            Assert.That(RollToHitStage<ICombatMetadata>.HitBeatContext("Warriors", "Gunners", 6f),
                Is.EqualTo("Warriors -> Gunners  |  6 attacks"));
            Assert.That(RollToHitStage<ICombatMetadata>.HitBeatContext("Hero", "Gunners", 1f),
                Is.EqualTo("Hero -> Gunners  |  1 attack"), "a single attack reads singular");
        }

        // The live stage: the caption must carry the weapon and the count of dice actually rolled -
        // the determined AttackCount, not the weapon's per-carrier Attacks stat.
        [Test]
        public async Task ToHitBeat_IsCaptionedWithTheWeaponAndTheAttacksRolled()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var presenter = new RecordingPresenter();
            var ctx = new TestGameContext(store, new FixedFaceDiceRoller(6), presenter: presenter);

            var weapon = new Weapon("Energy Sword", rangeInches: 0f, attacks: 3, armorPenetration: 0);
            DataBinding<UnitData> attacker = MakeUnit(store, "Warriors", new Position(0f, 5f), weapon);
            DataBinding<UnitData> defender = MakeUnit(store, "Gunners", new Position(1f, 5f), null);

            var stage = new RollToHitStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 2, isMelee: true);
            metadata.AddResult(new DetermineHitRollResults(4, attackCount: 6));
            await stage.Enter(metadata);

            DiceRolledBeat beat = presenter.Beats.OfType<DiceRolledBeat>().Single();
            Assert.That(beat.Label, Is.EqualTo("Roll to Hit - Energy Sword"));
            Assert.That(beat.Context, Is.EqualTo("Warriors -> Gunners  |  6 attacks"),
                "the volley's rolled attack count, not the weapon's A3 profile");
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, string name, Position position,
            Weapon? weapon)
        {
            var weapons = weapon == null ? new List<Weapon>() : new List<Weapon> { weapon };
            var model = new ModelData(0.75f, weapons, position, store);
            DataBinding<ModelData> modelBinding = store.GetDataBinding<ModelData>(store.Create(model));

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return store.GetDataBinding<UnitData>(store.Create(unit));
        }

        [Test]
        public void MoraleTags_ComposeQualityPlusNamedAndGrantedModifiers()
        {
            Assert.That(MoraleUtilities.ComposeMoraleTags(4, new List<(RuleOperation, string)>(), 0),
                Is.Null, "an unmodified test needs no chips");

            var named = new List<(RuleOperation, string)>
            {
                (new RuleOperation.ApplyRollModifier(ERollKind.Morale, -1), "Terrifying"),
            };
            List<string>? tags = MoraleUtilities.ComposeMoraleTags(4, named, -1);
            Assert.That(tags, Is.EqualTo(new[] { "Quality 4+", "Terrifying -1", "buff -1" }));
        }
    }
}
