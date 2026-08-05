using System;
using System.Collections.Generic;
using FDG.Data;
using FDG.Network.Messages;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Presentation.Messages;
using NUnit.Framework;

namespace FDG.Tests
{
    // The slice-1 beats are polymorphic and travel host->client inside a PresentBeatMessage, so
    // they must survive the real bus serializer (TypeNameHandling.Auto). A broken round-trip would
    // silently mean networked clients render nothing. Driving the full emission flow through the
    // state machine is impractical in a unit test (StageBinding.Activate needs a bound transition +
    // real parent), so emission-in-context is left to the #7 integration pass; here we lock down the
    // beat payloads and their wire round-trip.
    [TestFixture]
    public class PresentationBeatSerializationTests
    {
        private MessageSerializer _serializer = null!;

        [SetUp]
        public void SetUp()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _serializer = new MessageSerializer(store);
            _serializer.RegisterMessageType<PresentBeatMessage>();
        }

        private PresentationBeat RoundTrip(PresentationBeat beat)
        {
            var sent = new PresentBeatMessage(beat);
            ArraySegment<byte> bytes = _serializer.SerializeMessage(sent);
            var received = _serializer.DeserializeMessage(bytes) as PresentBeatMessage;
            Assert.That(received, Is.Not.Null, "PresentBeatMessage failed to deserialize");
            return received!.Beat;
        }

        [Test]
        public void UnitMovedBeat_CarriesPayloadAndProjection()
        {
            var beat = new UnitMovedBeat(
                new UnitID(Guid.NewGuid()),
                "Warriors",
                new List<ModelMove>
                {
                    new ModelMove(new ModelID(Guid.NewGuid()),
                        new List<Position> { new Position(1f, 2f), new Position(3f, 4f) }),
                },
                PresentationDurations.UnitMove);

            Assert.That(beat.NominalDuration, Is.EqualTo(PresentationDurations.UnitMove));
            Assert.That(beat.Text, Is.EqualTo("Warriors moves."));
            Assert.That(beat.Toughness, Is.EqualTo(1),
                "#294: an ordinary model weighs 1 - the default must not read as a monster");
        }

        // #294: the front-end's footfall voice pitches down with the beat's weight proxy, so a
        // nonsensical Tough would mistune it (0 or negative would divide the pitch curve by <= 0).
        [Test]
        public void UnitMovedBeat_ToughnessFloorsAtOne()
        {
            var moves = new List<ModelMove>
            {
                new ModelMove(new ModelID(Guid.NewGuid()), new List<Position> { new Position(0f, 0f) }),
            };

            Assert.That(new UnitMovedBeat(new UnitID(Guid.NewGuid()), "Warriors", moves,
                PresentationDurations.UnitMove, toughness: 0).Toughness, Is.EqualTo(1));
            Assert.That(new UnitMovedBeat(new UnitID(Guid.NewGuid()), "Warriors", moves,
                PresentationDurations.UnitMove, toughness: -3).Toughness, Is.EqualTo(1));
        }

        [Test]
        public void UnitMovedBeat_SurvivesWireRoundTrip_PreservingTypeMovesAndDuration()
        {
            var unitId = new UnitID(Guid.NewGuid());
            var modelId = new ModelID(Guid.NewGuid());
            // Multi-node polyline: start -> corner -> destination.
            var original = new UnitMovedBeat(unitId, "Warriors",
                new List<ModelMove>
                {
                    new ModelMove(modelId, new List<Position>
                    {
                        new Position(1f, 2f), new Position(1f, 5f), new Position(4f, 5f),
                    },
                    // #341: the attitude at each of those points - [0] the pre-move resting facing, then one
                    // per waypoint. A networked client turns the model between them exactly as the placer's
                    // client does, so the two must not disagree over the wire.
                    new List<Float2> { new Float2(0f, 1f), new Float2(0f, 1f), new Float2(1f, 0f) }),
                },
                TimeSpan.FromMilliseconds(900), toughness: 6);

            PresentationBeat result = RoundTrip(original);

            Assert.That(result, Is.TypeOf<UnitMovedBeat>(), "concrete beat type must survive TypeNameHandling.Auto");
            var moved = (UnitMovedBeat)result;
            Assert.That(moved.Unit, Is.EqualTo(unitId));
            Assert.That(moved.UnitName, Is.EqualTo("Warriors"));
            Assert.That(moved.NominalDuration, Is.EqualTo(TimeSpan.FromMilliseconds(900)),
                "carried duration must round-trip so distance-based pacing survives the wire");
            Assert.That(moved.Toughness, Is.EqualTo(6),
                "#294: the weight proxy must survive the wire or networked clients mistune footfalls");
            Assert.That(moved.Moves, Has.Count.EqualTo(1));
            ModelMove m = moved.Moves[0];
            Assert.That(m.Model, Is.EqualTo(modelId));
            Assert.That(m.Waypoints, Has.Count.EqualTo(3), "the full corner-rounding polyline must survive");
            Assert.That(m.Waypoints[0].x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(m.Waypoints[0].z, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(m.Waypoints[1].x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(m.Waypoints[1].z, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(m.Waypoints[2].x, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(m.Waypoints[2].z, Is.EqualTo(5f).Within(0.0001f));

            Assert.That(m.Facings, Is.Not.Null, "#341: the per-waypoint attitudes must survive the wire");
            Assert.That(m.Facings, Has.Count.EqualTo(3), "one attitude per polyline point, resting facing first");
            Assert.That(m.Facings![0].X, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(m.Facings[0].Y, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(m.Facings[2].X, Is.EqualTo(1f).Within(0.0001f),
                "the turn taken at the last node is what the glide has to interpolate to");
            Assert.That(m.Facings[2].Y, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void ModelDiedBeat_CarriesPayloadAndProjection()
        {
            var beat = new ModelDiedBeat(new ModelID(Guid.NewGuid()), new UnitID(Guid.NewGuid()),
                "Heavy Gunners", new Position(5f, 6f));

            Assert.That(beat.NominalDuration, Is.EqualTo(PresentationDurations.ModelDeath));
            Assert.That(beat.Text, Is.EqualTo("Heavy Gunners: a model is destroyed."));
        }

        [Test]
        public void ModelDiedBeat_SurvivesWireRoundTrip_PreservingConcreteTypeAndPosition()
        {
            var modelId = new ModelID(Guid.NewGuid());
            var unitId = new UnitID(Guid.NewGuid());
            var original = new ModelDiedBeat(modelId, unitId, "Heavy Gunners", new Position(5f, 6f),
                overlap: true);

            PresentationBeat result = RoundTrip(original);

            Assert.That(result, Is.TypeOf<ModelDiedBeat>());
            var died = (ModelDiedBeat)result;
            Assert.That(died.Model, Is.EqualTo(modelId));
            Assert.That(died.Unit, Is.EqualTo(unitId));
            Assert.That(died.UnitName, Is.EqualTo("Heavy Gunners"));
            Assert.That(died.Position.x, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(died.Position.z, Is.EqualTo(6f).Within(0.0001f));
            // #232 cascade: the overlap flag must ride the wire so networked clients stagger too.
            Assert.That(died.Overlap, Is.True);
            Assert.That(died.Held, Is.True, "an overlapped death paces only its stagger lead-in");
            Assert.That(died.HoldLeadIn, Is.EqualTo(PresentationDurations.CasualtyStagger));
        }

        [Test]
        public void UnitRoutedBeat_SurvivesWireRoundTrip_PreservingAllModelDeaths()
        {
            var m0 = new ModelID(Guid.NewGuid());
            var m1 = new ModelID(Guid.NewGuid());
            var unitId = new UnitID(Guid.NewGuid());
            var original = new UnitRoutedBeat(unitId, "Warriors", new List<RoutedModel>
            {
                new RoutedModel(m0, new Position(1f, 2f)),
                new RoutedModel(m1, new Position(3f, 4f)),
            });

            PresentationBeat result = RoundTrip(original);

            Assert.That(result, Is.TypeOf<UnitRoutedBeat>());
            var routed = (UnitRoutedBeat)result;
            Assert.That(routed.Unit, Is.EqualTo(unitId));
            Assert.That(routed.UnitName, Is.EqualTo("Warriors"));
            Assert.That(routed.NominalDuration, Is.EqualTo(PresentationDurations.ModelDeath));
            Assert.That(routed.Models, Has.Count.EqualTo(2), "every routed model must ride the wire");
            Assert.That(routed.Models[0].Model, Is.EqualTo(m0));
            Assert.That(routed.Models[1].Model, Is.EqualTo(m1));
            Assert.That(routed.Models[1].Position.x, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(routed.Models[1].Position.z, Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void RollOffBeat_SurvivesWireRoundTrip_PreservingPerCompetitorRollsAndResults()
        {
            var original = new RollOffBeat("Map Side Roll-Off", new List<RollOffEntry>
            {
                new RollOffEntry("Team 1", 5, ERollOffResult.Won),
                new RollOffEntry("Team 2", 3, ERollOffResult.Lost),
            });

            PresentationBeat result = RoundTrip(original);

            Assert.That(result, Is.TypeOf<RollOffBeat>());
            var rollOff = (RollOffBeat)result;
            Assert.That(rollOff.Label, Is.EqualTo("Map Side Roll-Off"));
            Assert.That(rollOff.Entries, Has.Count.EqualTo(2));
            Assert.That(rollOff.Entries[0].Name, Is.EqualTo("Team 1"));
            Assert.That(rollOff.Entries[0].Roll, Is.EqualTo(5));
            Assert.That(rollOff.Entries[0].Result, Is.EqualTo(ERollOffResult.Won));
            Assert.That(rollOff.Entries[1].Result, Is.EqualTo(ERollOffResult.Lost));
        }

        [Test]
        public void DiceRolledBeat_From_CapturesHistogramAndComputesSuccesses()
        {
            // d6: one 2, two 4s, one 6  →  4 dice, 3 of them at-or-above 4.
            var roll = new DiceResults(new float[] { 0f, 1f, 0f, 2f, 0f, 1f }, 1);

            var beat = DiceRolledBeat.From(roll, successThreshold: 4, ERandomnessType.Realistic, "To Hit");

            Assert.That(beat.SideMin, Is.EqualTo(1));
            Assert.That(beat.SideMax, Is.EqualTo(6));
            Assert.That(beat.Total, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(beat.Successes, Is.EqualTo(3f).Within(0.0001f), "faces 4,4,6 are >= 4");
            Assert.That(beat.Text, Is.EqualTo("To Hit (4+): 3 / 4"));
        }

        [Test]
        public void DiceRolledBeat_Realistic_SurvivesWireRoundTrip()
        {
            var original = new DiceRolledBeat(new List<float> { 0f, 1f, 0f, 2f, 0f, 1f },
                sideMin: 1, successThreshold: 4, ERandomnessType.Realistic, "Roll to Hit", "3 hits");

            PresentationBeat result = RoundTrip(original);

            Assert.That(result, Is.TypeOf<DiceRolledBeat>());
            var dice = (DiceRolledBeat)result;
            Assert.That(dice.FaceCounts, Is.EqualTo(new[] { 0f, 1f, 0f, 2f, 0f, 1f }));
            Assert.That(dice.SideMin, Is.EqualTo(1));
            Assert.That(dice.SuccessThreshold, Is.EqualTo(4));
            Assert.That(dice.Mode, Is.EqualTo(ERandomnessType.Realistic));
            Assert.That(dice.Label, Is.EqualTo("Roll to Hit"));
            Assert.That(dice.ResultSummary, Is.EqualTo("3 hits"), "the settled-result summary must ride the wire");
        }

        [Test]
        public void AttackBeat_Ranged_SurvivesWireRoundTrip_PreservingPositionsAndKind()
        {
            // Two firing weapons (From), two volleys.
            var original = new AttackBeat(isMelee: false,
                from: new List<Position> { new Position(1f, 2f), new Position(3f, 2f) },
                to: new List<Position> { new Position(10f, 20f) },
                volleyCount: 2, armorPenetration: 2,
                weaponEffect: "plasma-bolt", hitCount: 1.5f, attackCount: 4f);

            PresentationBeat result = RoundTrip(original);

            Assert.That(result, Is.TypeOf<AttackBeat>());
            var atk = (AttackBeat)result;
            Assert.That(atk.IsMelee, Is.False);
            Assert.That(atk.VolleyCount, Is.EqualTo(2));
            Assert.That(atk.ArmorPenetration, Is.EqualTo(2));
            Assert.That(atk.NominalDuration, Is.EqualTo(PresentationDurations.ForVolleys(2)),
                "duration scales with the volley count (all From weapons fire together per volley)");
            Assert.That(atk.From, Has.Count.EqualTo(2));
            Assert.That(atk.To, Has.Count.EqualTo(1));
            Assert.That(atk.From[1].x, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(atk.To[0].z, Is.EqualTo(20f).Within(0.0001f));
            // #239: the effect key and hit share must ride the wire so networked clients render
            // the same per-weapon effect and the same hits/misses as the host.
            Assert.That(atk.WeaponEffect, Is.EqualTo("plasma-bolt"));
            Assert.That(atk.HitCount, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(atk.AttackCount, Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void AttackBeat_Melee_DurationScalesWithVolleyCount()
        {
            var melee = new AttackBeat(isMelee: true,
                from: new List<Position> { new Position(1f, 1f) },
                to: new List<Position> { new Position(2f, 1f) },
                volleyCount: 4, armorPenetration: 0);

            var result = (AttackBeat)RoundTrip(melee);
            Assert.That(result.IsMelee, Is.True);
            Assert.That(result.VolleyCount, Is.EqualTo(4));
            Assert.That(result.NominalDuration, Is.EqualTo(PresentationDurations.ForVolleys(4)));
            Assert.That(result.WeaponEffect, Is.Null, "#239: an unset effect key round-trips as null");
        }

        [Test]
        public void ModelWoundedBeat_SurvivesWireRoundTrip()
        {
            var modelId = new ModelID(Guid.NewGuid());
            var original = new ModelWoundedBeat(modelId, new Position(4f, 7f), overlap: true);

            var result = (ModelWoundedBeat)RoundTrip(original);
            Assert.That(result.Model, Is.EqualTo(modelId));
            Assert.That(result.Position.x, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(result.Position.z, Is.EqualTo(7f).Within(0.0001f));
            Assert.That(result.NominalDuration, Is.EqualTo(PresentationDurations.ModelWounded));
            // #232 cascade: the overlap flag must ride the wire (see ModelDiedBeat above).
            Assert.That(result.Overlap, Is.True);
            Assert.That(result.Held, Is.True);
        }

        [Test]
        public void SaveBeat_SurvivesWireRoundTrip_PreservingCountAndPositions()
        {
            var original = new SaveBeat(
                new List<Position> { new Position(1f, 1f), new Position(2f, 2f) },
                savedCount: 3);

            var result = (SaveBeat)RoundTrip(original);
            Assert.That(result.SavedCount, Is.EqualTo(3));
            Assert.That(result.DefenderPositions, Has.Count.EqualTo(2));
            Assert.That(result.DefenderPositions[1].x, Is.EqualTo(2f).Within(0.0001f));
        }

        // #274: the spell visuals are chosen host-side (disposition, who assisted, how much), so the
        // variant, both position sets and the magnitude must ride the wire or a networked client
        // renders the wrong effect — or none.
        [Test]
        public void SpellEffectBeat_SurvivesWireRoundTrip_PreservingVariantPositionsAndMagnitude()
        {
            var original = new SpellEffectBeat(ESpellVisual.AssistHinder,
                new List<Position> { new Position(10f, 12f), new Position(11f, 12f) },
                "Doom Bolt",
                new List<Position> { new Position(30f, 40f) },
                magnitude: 3);

            PresentationBeat result = RoundTrip(original);

            Assert.That(result, Is.TypeOf<SpellEffectBeat>());
            var spell = (SpellEffectBeat)result;
            Assert.That(spell.Visual, Is.EqualTo(ESpellVisual.AssistHinder));
            Assert.That(spell.SpellName, Is.EqualTo("Doom Bolt"));
            Assert.That(spell.Magnitude, Is.EqualTo(3), "tokens spent scale the effect front-end-side");
            Assert.That(spell.Positions, Has.Count.EqualTo(2));
            Assert.That(spell.Positions[1].x, Is.EqualTo(11f).Within(0.0001f));
            Assert.That(spell.Sources, Has.Count.EqualTo(1));
            Assert.That(spell.Sources[0].z, Is.EqualTo(40f).Within(0.0001f));
            Assert.That(spell.NominalDuration, Is.EqualTo(PresentationDurations.SpellAssist));
        }

        [Test]
        public void SpellEffectBeat_OmittedSources_RoundTripAsEmpty_NotNull()
        {
            // A cast outcome (and a pure #244 self-boost) has no source unit. The front-end indexes
            // Sources directly, so it must come back empty rather than null.
            var original = new SpellEffectBeat(ESpellVisual.CastSuccess,
                new List<Position> { new Position(5f, 5f) }, "Bless");

            var spell = (SpellEffectBeat)RoundTrip(original);
            Assert.That(spell.Sources, Is.Not.Null);
            Assert.That(spell.Sources, Is.Empty);
            Assert.That(spell.Magnitude, Is.EqualTo(0));
            Assert.That(spell.NominalDuration, Is.EqualTo(PresentationDurations.SpellCast));
        }

        [Test]
        public void SpellEffectBeat_TargetVariants_UseTheTargetDuration()
        {
            var boon = new SpellEffectBeat(ESpellVisual.TargetBoon,
                new List<Position> { new Position(1f, 1f) }, "Mend");
            var bane = new SpellEffectBeat(ESpellVisual.TargetBane,
                new List<Position> { new Position(1f, 1f) }, "Curse");

            Assert.That(boon.NominalDuration, Is.EqualTo(PresentationDurations.SpellTarget));
            Assert.That(bane.NominalDuration, Is.EqualTo(PresentationDurations.SpellTarget));
            Assert.That(boon.Text, Is.Null, "purely visual - the cast banners already narrate it");
        }

        [Test]
        public void BannerBeat_SurvivesWireRoundTrip_PreservingTextAndColor()
        {
            var original = new BannerBeat("Round 3", new TextColor(120, 200, 255, 255));

            PresentationBeat result = RoundTrip(original);

            Assert.That(result, Is.TypeOf<BannerBeat>());
            var banner = (BannerBeat)result;
            Assert.That(banner.BannerText, Is.EqualTo("Round 3"));
            Assert.That(banner.Text, Is.EqualTo("Round 3"), "Text projection mirrors the banner text");
            Assert.That(banner.Color, Is.EqualTo(new TextColor(120, 200, 255, 255)));
        }

        [Test]
        public void BannerBeat_TierSurvivesWireRoundTrip_WithItsPacingContract()
        {
            // #275: the tier has to cross the wire, or a networked client would draw and pace every
            // announcement as a full-size Headline while the host treats it as a passing toast.
            var original = new BannerBeat("Warriors embarked Rhino.", new TextColor(120, 200, 255, 255),
                EBannerTier.Toast);

            var banner = (BannerBeat)RoundTrip(original);

            Assert.That(banner.Tier, Is.EqualTo(EBannerTier.Toast));
            Assert.That(banner.Held, Is.True, "the pacing contract rides the tier, so it round-trips with it");
            Assert.That(banner.HoldLeadIn, Is.EqualTo(TimeSpan.Zero));
            Assert.That(banner.NominalDuration, Is.EqualTo(PresentationDurations.BannerToast));

            var notice = (BannerBeat)RoundTrip(
                new BannerBeat("Alice deploys first", TextColor.White, EBannerTier.Notice));
            Assert.That(notice.Tier, Is.EqualTo(EBannerTier.Notice));
            Assert.That(notice.HoldLeadIn, Is.EqualTo(PresentationDurations.BannerNoticeLeadIn));
        }

        [Test]
        public void DiceRolledBeat_Probabilistic_RoundTrip_PreservesFractionsAndMode()
        {
            // 5 dice spread evenly: 5/6 per face. Successes for 4+ = three faces * 0.8333…
            float per = 5f / 6f;
            var original = new DiceRolledBeat(new List<float> { per, per, per, per, per, per },
                sideMin: 1, successThreshold: 4, ERandomnessType.Probabilistic, "To Save");

            PresentationBeat result = RoundTrip(original);

            var dice = (DiceRolledBeat)result;
            Assert.That(dice.Mode, Is.EqualTo(ERandomnessType.Probabilistic),
                "the mode must ride on the beat — the front-end renders fractional rolls differently");
            Assert.That(dice.Total, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(dice.Successes, Is.EqualTo(3f * per).Within(0.0001f));
            for (int i = 0; i < dice.FaceCounts.Count; i++)
                Assert.That(dice.FaceCounts[i], Is.EqualTo(per).Within(0.0001f), "fractional counts must survive the wire");
        }
    }
}
