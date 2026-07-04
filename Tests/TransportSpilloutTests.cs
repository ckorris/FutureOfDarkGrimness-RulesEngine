using System;
using System.Linq;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #035 slice E: mid-combat destruction spillout, driven through the
    // real SpilloutOccupantsStage (the stage inserted after ApplyWounds in both the shooting FireStage and
    // the melee SwingMeleeWeaponStage). When the unit that just took wounds is a Transport that has now been
    // destroyed, its embarked units spill out: placed within 6" of the wreck (interactive PlaceObjectsRequest)
    // and un-embarked + Shaken + dangerous-tested. The deterministic effects are unit-tested in slice A; this
    // proves the stage orchestration (detect destruction → place → apply).
    [TestFixture]
    public class TransportSpilloutTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;
        private DataBinding<UnitData> _attacker = null!; // irrelevant to spillout, but CombatMetadata needs one

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
            _attacker = MakeUnit(new PlayerID(Guid.NewGuid()), "Attacker", 1, new Position(50f, 50f));
        }

        [Test]
        public async Task DestroyedTransport_SpillsOutOccupants()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, new Position(10f, 10f));
            DataBinding<UnitData> occupant = MakeUnit(_player, "Grunts", 2, new Position(0f, 0f)); // embarked → origin
            TransportUtilities.Embark(occupant.GetValue(), transport.GetValue());

            transport.GetValue().Models[0].DealWounds(1f); // destroy the transport (last model dies)
            Assert.That(transport.GetValue().GetIsDead(), Is.True, "precondition: the transport is destroyed.");

            SpilloutResults result = await RunSpillout(transport);

            Assert.That(result.UnitsSpilledOut, Is.EqualTo(1));
            Assert.That(TransportUtilities.IsEmbarked(occupant.GetValue()), Is.False, "the occupant is no longer embarked.");
            Assert.That(occupant.GetValue().GetIsOnBattlefield(), Is.True, "the occupant is placed near the wreck (on the table).");
            Assert.That(occupant.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True, "spilled-out occupants are Shaken.");
        }

        [Test]
        public async Task SurvivingTransport_DoesNotSpill()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, new Position(10f, 10f));
            DataBinding<UnitData> occupant = MakeUnit(_player, "Grunts", 2, new Position(0f, 0f));
            TransportUtilities.Embark(occupant.GetValue(), transport.GetValue());
            // Transport NOT destroyed (still alive).

            SpilloutResults result = await RunSpillout(transport);

            Assert.That(result.UnitsSpilledOut, Is.EqualTo(0));
            Assert.That(TransportUtilities.IsEmbarked(occupant.GetValue()), Is.True, "a surviving transport keeps its passengers.");
        }

        [Test]
        public async Task DestroyedNonTransport_DoesNotSpill()
        {
            DataBinding<UnitData> squad = MakeUnit(_player, "Grunts", 1, new Position(10f, 10f)); // not a transport
            squad.GetValue().Models[0].DealWounds(1f); // destroyed
            Assert.That(squad.GetValue().GetIsDead(), Is.True);

            SpilloutResults result = await RunSpillout(squad);

            Assert.That(result.UnitsSpilledOut, Is.EqualTo(0), "a destroyed non-transport has no occupants to spill.");
        }

        [Test]
        public async Task DestroyedEmptyTransport_DoesNotSpill()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, new Position(10f, 10f)); // nobody aboard
            transport.GetValue().Models[0].DealWounds(1f);

            SpilloutResults result = await RunSpillout(transport);

            Assert.That(result.UnitsSpilledOut, Is.EqualTo(0));
        }

        // #096 facet 2: the spillout narrates itself with presentation beats (was log-only). A destroyed
        // transport with a doomed occupant plays a wreck banner, the occupant's Shaken banner, a per-model
        // dangerous-terrain d6, and a death animation for a model the test kills.
        [Test]
        public async Task DestroyedTransport_PresentsSpilloutBeats()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, new Position(10f, 10f));
            DataBinding<UnitData> occupant = MakeUnit(_player, "Grunts", 1, new Position(0f, 0f)); // 1-wound model
            TransportUtilities.Embark(occupant.GetValue(), transport.GetValue());
            transport.GetValue().Models[0].DealWounds(1f); // destroy the transport

            var sink = new RecordingPresentationSink();
            // Every model rolls a 1 → the dangerous test wounds it; the 1-wound occupant model dies.
            await RunSpilloutCapturing(transport, new FixedDiceRoller(1), sink);

            var banners = sink.Beats.OfType<BannerBeat>().ToList();
            Assert.That(banners.Any(b => b.BannerText.Contains("destroyed")), Is.True, "a wreck banner is presented.");
            Assert.That(banners.Any(b => b.BannerText.Contains("Shaken")), Is.True, "the spilled unit's Shaken banner is presented.");
            Assert.That(sink.Beats.OfType<DiceRolledBeat>().Any(d => d.Label == "Dangerous Terrain"), Is.True,
                "each occupant model's dangerous-terrain die is surfaced.");
            Assert.That(sink.Beats.OfType<ModelDiedBeat>().Any(), Is.True,
                "a model killed by the dangerous test animates its death.");
        }

        [Test]
        public async Task SpilloutSafeRoll_PresentsDicePerModel_ButNoCasualtyBeat()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, new Position(10f, 10f));
            DataBinding<UnitData> occupant = MakeUnit(_player, "Grunts", 2, new Position(0f, 0f));
            TransportUtilities.Embark(occupant.GetValue(), transport.GetValue());
            transport.GetValue().Models[0].DealWounds(1f);

            var sink = new RecordingPresentationSink();
            await RunSpilloutCapturing(transport, new FixedDiceRoller(4), sink); // 4 = safe, no wounds

            Assert.That(sink.Beats.OfType<DiceRolledBeat>().Count(), Is.EqualTo(2), "one dangerous-terrain die per living model.");
            Assert.That(sink.Beats.OfType<ModelDiedBeat>().Any(), Is.False, "a safe roll kills no one.");
            Assert.That(sink.Beats.OfType<ModelWoundedBeat>().Any(), Is.False);
        }

        // --- helpers ---

        private async Task<SpilloutResults> RunSpilloutCapturing(DataBinding<UnitData> defender,
            IDiceRoller roller, RecordingPresentationSink sink)
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedPlaceRequester(new Position(10f, 10f)),
                roller, sink);

            var weapon = new Weapon("Test", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(ctx, _attacker, defender, weapon, weaponCount: 1);

            var stage = new SpilloutOccupantsStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            await stage.Enter(metadata);
            return metadata.QueryForResult(out SpilloutResults result) ? result : new SpilloutResults(0);
        }

        private async Task<SpilloutResults> RunSpillout(DataBinding<UnitData> defender)
        {
            // The place-requester drops each spilled unit at the wreck position (within 6").
            var ctx = new TriggeredMoveTestContext(_store, new CannedPlaceRequester(new Position(10f, 10f)));

            var weapon = new Weapon("Test", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(ctx, _attacker, defender, weapon, weaponCount: 1);

            var stage = new SpilloutOccupantsStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out SpilloutResults result), Is.True,
                "the spillout stage must store a SpilloutResults in the metadata.");
            return result;
        }

        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, int modelCount, Position pos)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private DataBinding<UnitData> MakeTransport(string name, int capacity, Position pos)
        {
            DataBinding<UnitData> binding = MakeUnit(_player, name, 1, pos);
            binding.GetValue().AttachRuleDefinition(new ResolvedRule(TransportUtilities.TransportRuleName,
                CoreRuleCatalog.Transport, new RuleArgument[] { new RuleArgument.Int(capacity) }));
            return binding;
        }
    }
}
