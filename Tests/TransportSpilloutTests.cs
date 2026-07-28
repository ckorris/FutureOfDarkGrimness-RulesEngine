using System;
using System.Linq;
using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Integration tests for Transport destruction spillout (#035 slice E, rearchitected by #169).
    // Spillout now runs from UnitDestructionNotifier — the single destruction choke point every
    // notifying death path funnels through (shooting/melee/impact/spell/strafing via ApplyWoundsStage,
    // melee Rout via MoraleUtilities.RoutWithPresentation) — with the flow itself extracted into
    // SpilloutExecutor. When the dead unit is a Transport, its embarked units spill out: placed within
    // 6" of the wreck (interactive PlaceObjectsRequest), un-embarked + Shaken + dangerous-tested.
    // The deterministic effects are unit-tested in slice A; these prove the orchestration
    // (detect destruction -> place -> apply) and the #169 Rout path end-to-end.
    [TestFixture]
    public class TransportSpilloutTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public async Task DestroyedTransport_SpillsOutOccupants()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, new Position(10f, 10f));
            DataBinding<UnitData> occupant = MakeUnit(_player, "Grunts", 2, new Position(0f, 0f)); // embarked → origin
            TransportUtilities.Embark(occupant.GetValue(), transport.GetValue());

            transport.GetValue().Models[0].DealWounds(1f); // destroy the transport (last model dies)
            Assert.That(transport.GetValue().GetIsDead(), Is.True, "precondition: the transport is destroyed.");

            int spilled = await RunSpillout(transport);

            Assert.That(spilled, Is.EqualTo(1));
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

            int spilled = await RunSpillout(transport);

            Assert.That(spilled, Is.EqualTo(0));
            Assert.That(TransportUtilities.IsEmbarked(occupant.GetValue()), Is.True, "a surviving transport keeps its passengers.");
        }

        [Test]
        public async Task DestroyedNonTransport_DoesNotSpill()
        {
            DataBinding<UnitData> squad = MakeUnit(_player, "Grunts", 1, new Position(10f, 10f)); // not a transport
            squad.GetValue().Models[0].DealWounds(1f); // destroyed
            Assert.That(squad.GetValue().GetIsDead(), Is.True);

            int spilled = await RunSpillout(squad);

            Assert.That(spilled, Is.EqualTo(0), "a destroyed non-transport has no occupants to spill.");
        }

        [Test]
        public async Task DestroyedEmptyTransport_DoesNotSpill()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, new Position(10f, 10f)); // nobody aboard
            transport.GetValue().Models[0].DealWounds(1f);

            int spilled = await RunSpillout(transport);

            Assert.That(spilled, Is.EqualTo(0));
        }

        // #169: the bug this rearchitecture fixes — a Transport killed by a melee-morale Rout (the
        // killer-less path through UnitDestructionNotifier) must spill its occupants exactly like one
        // shot or cut down. Drives the REAL Rout path: RoutWithPresentation -> NotifyUnitDestroyed
        // (killer: null) -> SpilloutExecutor. Previously the occupants stayed permanently embarked
        // off-table ("ghost" state).
        [Test]
        public async Task RoutedTransport_SpillsOutOccupants()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, new Position(10f, 10f));
            DataBinding<UnitData> occupant = MakeUnit(_player, "Grunts", 2, new Position(0f, 0f));
            TransportUtilities.Embark(occupant.GetValue(), transport.GetValue());
            Assert.That(transport.GetValue().GetIsAlive(), Is.True, "precondition: the transport routs from alive.");

            var ctx = new TriggeredMoveTestContext(_store, new CannedPlaceRequester(new Position(10f, 10f)));
            await MoraleUtilities.RoutWithPresentation(ctx, transport);

            Assert.That(transport.GetValue().GetIsDead(), Is.True, "the routed transport is destroyed.");
            Assert.That(TransportUtilities.IsEmbarked(occupant.GetValue()), Is.False,
                "the occupant is un-embarked - not stranded in the ghost state.");
            Assert.That(occupant.GetValue().GetIsOnBattlefield(), Is.True, "the occupant is placed near the wreck.");
            Assert.That(occupant.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True, "spilled-out occupants are Shaken.");
        }

        // #169: the choke point itself — NotifyUnitDestroyed spills regardless of killer attribution
        // (the killer-less early-return must not skip spillout).
        [Test]
        public async Task NotifyUnitDestroyed_WithoutKiller_StillSpills()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, new Position(10f, 10f));
            DataBinding<UnitData> occupant = MakeUnit(_player, "Grunts", 2, new Position(0f, 0f));
            TransportUtilities.Embark(occupant.GetValue(), transport.GetValue());
            transport.GetValue().Models[0].DealWounds(1f);

            var ctx = new TriggeredMoveTestContext(_store, new CannedPlaceRequester(new Position(10f, 10f)));
            await UnitDestructionNotifier.NotifyUnitDestroyed(ctx, transport.GetValue(), killer: null);

            Assert.That(TransportUtilities.IsEmbarked(occupant.GetValue()), Is.False);
            Assert.That(occupant.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True);
        }

        // #096 facet 2: the spillout narrates itself with presentation beats (was log-only). A destroyed
        // transport with a doomed occupant plays a wreck banner, the occupant's Shaken banner, the batched
        // dangerous-terrain dice row, and a death animation for a model the test kills.
        [Test]
        public async Task DestroyedTransport_PresentsSpilloutBeats()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, new Position(10f, 10f));
            DataBinding<UnitData> occupant = MakeUnit(_player, "Grunts", 1, new Position(0f, 0f)); // 1-wound model
            TransportUtilities.Embark(occupant.GetValue(), transport.GetValue());
            transport.GetValue().Models[0].DealWounds(1f); // destroy the transport

            var sink = new RecordingPresentationSink();
            // Every model rolls a 1 → the dangerous test wounds it; the 1-wound occupant model dies.
            await RunSpilloutCapturing(transport, new FixedFaceDiceRoller(1), sink);

            var banners = sink.Beats.OfType<BannerBeat>().ToList();
            Assert.That(banners.Any(b => b.BannerText.Contains("destroyed")), Is.True, "a wreck banner is presented.");
            Assert.That(banners.Any(b => b.BannerText.Contains("Shaken")), Is.True, "the spilled unit's Shaken banner is presented.");
            Assert.That(sink.Beats.OfType<DiceRolledBeat>().Any(d => d.Label == "Dangerous Terrain"), Is.True,
                "each occupant model's dangerous-terrain die is surfaced.");
            Assert.That(sink.Beats.OfType<ModelDiedBeat>().Any(), Is.True,
                "a model killed by the dangerous test animates its death.");
        }

        // The dangerous-terrain test rolls as ONE batched row (a die per living model, a single
        // DiceRolledBeat) instead of one beat per model — per-model beats made big spillouts crawl.
        [Test]
        public async Task Spillout_PresentsOneBatchedDiceBeat_SafeRollHasNoCasualtyBeats()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, new Position(10f, 10f));
            DataBinding<UnitData> occupant = MakeUnit(_player, "Grunts", 2, new Position(0f, 0f));
            TransportUtilities.Embark(occupant.GetValue(), transport.GetValue());
            transport.GetValue().Models[0].DealWounds(1f);

            var sink = new RecordingPresentationSink();
            await RunSpilloutCapturing(transport, new FixedFaceDiceRoller(4), sink); // 4 = safe, no wounds

            var diceBeats = sink.Beats.OfType<DiceRolledBeat>().ToList();
            Assert.That(diceBeats.Count, Is.EqualTo(1), "the whole unit's dangerous-terrain test is one batched dice row.");
            Assert.That(diceBeats[0].FaceCounts.Sum(), Is.EqualTo(2f), "the single beat carries one die per living model.");
            Assert.That(sink.Beats.OfType<ModelDiedBeat>().Any(), Is.False, "a safe roll kills no one.");
            Assert.That(sink.Beats.OfType<ModelWoundedBeat>().Any(), Is.False);
        }

        // #299: every spilled model must still be ON THE TABLE when its unit's dangerous-terrain roll is
        // read, and only drop as its own death beat plays. The bug was the reverse: ApplySpilloutEffects
        // landed the whole batch at placement time, so casualties were already dead-in-state (and so
        // hidden by the front-end, which drops any model dead with no death beat registered) through the
        // announce and the dice row - the player saw models missing, then watched them reappear to die.
        [Test]
        public async Task Spillout_ModelsStayOnTableUntilTheirOwnDeathBeat()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, new Position(10f, 10f));
            DataBinding<UnitData> occupant = MakeUnit(_player, "Grunts", 3, new Position(0f, 0f)); // 1-wound models
            TransportUtilities.Embark(occupant.GetValue(), transport.GetValue());
            transport.GetValue().Models[0].DealWounds(1f); // destroy the transport

            // Snapshots how many occupant models are dead at the instant each beat is emitted.
            var sink = new StateSnapshotSink(() =>
                occupant.GetValue().Models.Count(m => m.GetIsDead()));

            await RunSpilloutCapturing(transport, new FixedFaceDiceRoller(1), sink); // every model rolls a 1

            (PresentationBeat Beat, int DeadAtEmit)[] timeline = sink.Timeline.ToArray();

            var dice = timeline.Where(e => e.Beat is DiceRolledBeat).ToList();
            Assert.That(dice, Is.Not.Empty, "the batched dangerous-terrain roll is presented.");
            Assert.That(dice[0].DeadAtEmit, Is.EqualTo(0),
                "the whole squad is still standing when the player reads the roll that kills it.");

            foreach ((PresentationBeat beat, int deadAtEmit) in timeline.Where(e => e.Beat is BannerBeat))
            {
                Assert.That(deadAtEmit, Is.EqualTo(0),
                    "no model has dropped while the wreck / Shaken banners are still going up.");
            }

            // Each death beat is emitted as its own wound lands: the Nth death sees exactly N models dead.
            var deaths = timeline.Where(e => e.Beat is ModelDiedBeat).ToList();
            Assert.That(deaths.Count, Is.EqualTo(3), "all three models die, each animating once.");
            for (int i = 0; i < deaths.Count; i++)
            {
                Assert.That(deaths[i].DeadAtEmit, Is.EqualTo(i + 1),
                    "a model dies in the same instant its death beat is enqueued - never before it.");
            }

            // And nobody is animated twice.
            var diedIds = deaths.Select(e => ((ModelDiedBeat)e.Beat).Model.ID).ToList();
            Assert.That(diedIds.Distinct().Count(), Is.EqualTo(diedIds.Count),
                "no model plays its death animation more than once.");
        }

        // --- helpers ---

        // Records each beat together with a caller-supplied snapshot of game state taken at the moment
        // the beat was emitted - which is what lets a test assert ORDERING between authoritative state
        // changes and the beats that announce them, not just the sequence of beats.
        private sealed class StateSnapshotSink : IPresentationSink
        {
            private readonly Func<int> _snapshot;
            public readonly List<(PresentationBeat Beat, int DeadAtEmit)> Timeline = new();
            public StateSnapshotSink(Func<int> snapshot) => _snapshot = snapshot;
            public void OnBeat(PresentationBeat beat) => Timeline.Add((beat, _snapshot()));
        }

        private async Task<int> RunSpilloutCapturing(DataBinding<UnitData> dead,
            IDiceRoller roller, IPresentationSink sink)
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedPlaceRequester(new Position(10f, 10f)),
                roller, sink);
            return await SpilloutExecutor.SpillIfDestroyedTransport(ctx, dead.GetValue());
        }

        private async Task<int> RunSpillout(DataBinding<UnitData> dead)
        {
            // The place-requester drops each spilled unit at the wreck position (within 6").
            var ctx = new TriggeredMoveTestContext(_store, new CannedPlaceRequester(new Position(10f, 10f)));
            return await SpilloutExecutor.SpillIfDestroyedTransport(ctx, dead.GetValue());
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
