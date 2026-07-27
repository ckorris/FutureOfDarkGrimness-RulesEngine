using System;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #035 slice D + #097: mid-game embark, the rules-supplied way.
    //  - Eligibility (GetEmbarkableTransports): a friendly transport with room, on the table, within the
    //    caller's distance — over-capacity and too-far transports are excluded.
    //  - ChooseActionStage: an "Embark" action is offered only when an engine spatial check finds an
    //    eligible transport (the availability gate is spatial, like Charge's), and choosing it routes to
    //    EmbarkStage.
    //  - EmbarkStage: boards the unit (EmbarkedIn token + models set aside off-table) and ENDS the
    //    activation (the unit is now inside); cancelling the transport choice returns to Choose Action.
    //
    // #097 replaced slice D's "set aside from Advance range" shortcut with boarding-from-contact: the
    // approach is an ordinary Move, so having moved no longer disqualifies the unit, and a transport
    // within Rush reach but short of contact is listed GREYED with a "move up first" reason.
    //
    // Test geometry (all models are 0.5" circular bases, so base-to-base is centre distance minus 1"):
    //   squad at x=10  |  contact transport at x=11.5 (0.5" apart)
    //                  |  approach transport at x=16  (5" apart - inside Rush, outside contact)
    //                  |  distant transport  at x=60  (49" apart - beyond everything)
    [TestFixture]
    public class TransportEmbarkTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;

        private const float ContactDistance = TransportUtilities.EmbarkContactDistanceInches;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public void GetEmbarkableTransports_FindsFriendlyTransportInContactWithRoom()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, x: 11.5f, z: 10f);
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f);

            var eligible = EmbarkStage.GetEmbarkableTransports(ctx, squad.GetValue(), ContactDistance);

            Assert.That(eligible, Has.Count.EqualTo(1));
            Assert.That(eligible[0], Is.EqualTo(transport));
        }

        // The distance is the caller's question, not a fixed property of embarking: the same transport is
        // out of reach for "can board now" and in reach for "could reach one if it moved first".
        [Test]
        public void GetEmbarkableTransports_ShortOfContact_FoundOnlyAtTheWiderReach()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            MakeTransport("Rhino", capacity: 6, x: 16f, z: 10f); // 5" away
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f);

            Assert.That(EmbarkStage.GetEmbarkableTransports(ctx, squad.GetValue(), ContactDistance), Is.Empty,
                "5\" short of the hull is not boarding range - the unit has to walk over first.");
            Assert.That(EmbarkStage.GetEmbarkableTransports(ctx, squad.GetValue(),
                GameWideConstants.RUSH_DISTANCE_INCHES), Has.Count.EqualTo(1),
                "but it is well inside the Rush the unit would use to close the gap.");
        }

        [Test]
        public void GetEmbarkableTransports_ExcludesOutOfRange()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            MakeTransport("Rhino", capacity: 6, x: 60f, z: 10f); // far away
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f);

            Assert.That(EmbarkStage.GetEmbarkableTransports(ctx, squad.GetValue(),
                GameWideConstants.RUSH_DISTANCE_INCHES), Is.Empty,
                "a transport beyond the unit's whole move can't be reached this activation.");
        }

        [Test]
        public void GetEmbarkableTransports_ExcludesOverCapacity()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            MakeTransport("Buggy", capacity: 1, x: 11.5f, z: 10f); // only 1 space
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f); // needs 2

            Assert.That(EmbarkStage.GetEmbarkableTransports(ctx, squad.GetValue(), ContactDistance), Is.Empty);
        }

        [Test]
        public async Task ChooseAction_TransportInContact_OffersEmbark_AndRoutes()
        {
            var requester = new RecordingActionRequester("Embark");
            var ctx = new TriggeredMoveTestContext(_store, requester);
            MakeTransport("Rhino", capacity: 6, x: 11.5f, z: 10f);
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f);

            var unitCtx = NewActivation(ctx, squad);
            bool routedToEmbark = false;
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToEmbark.Bind("ToEmbark");
            stage.ToEmbark.OnWillActivate += _ => routedToEmbark = true;
            await stage.Enter(unitCtx);

            Assert.That(requester.OfferedOptions, Does.Contain(CoreRuleCatalog.EmbarkRuleName),
                "Embark is offered when a friendly transport is in contact.");
            Assert.That(routedToEmbark, Is.True, "choosing Embark routes to EmbarkStage.");
        }

        // #097: a transport the unit could walk to is NOT silently absent from the menu — it is listed
        // greyed with the reason, so the player learns that moving up produces the option.
        [Test]
        public async Task ChooseAction_TransportWithinRushButNotContact_OffersEmbarkGreyedWithHint()
        {
            var requester = new RecordingActionRequester(ChooseActionStage.MOVEMENT_CHOICE_NAME);
            var ctx = new TriggeredMoveTestContext(_store, requester);
            MakeTransport("Rhino", capacity: 6, x: 16f, z: 10f); // 5" away
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f);

            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToEmbark.Bind("ToEmbark");
            stage.ToMovement.Bind("ToMovement");
            await stage.Enter(NewActivation(ctx, squad));

            Assert.That(requester.OfferedOptions, Does.Not.Contain(CoreRuleCatalog.EmbarkRuleName),
                "5\" short of the hull, boarding is not yet a legal choice.");
            var greyed = requester.OfferedInvalidOptions
                .FirstOrDefault(option => option.Option == CoreRuleCatalog.EmbarkRuleName);
            Assert.That(greyed, Is.Not.Null, "but the entry is still shown, greyed.");
            Assert.That(greyed!.Reason, Is.EqualTo("Move into contact with Rhino first."));
        }

        // The whole point of boarding-from-contact: the unit Rushes over and boards on the SAME activation.
        // Embark is the one action here that having moved must not disqualify - the move IS the entering move.
        [Test]
        public async Task ChooseAction_InContactAfterMoving_StillOffersEmbark()
        {
            var requester = new RecordingActionRequester("Embark");
            var ctx = new TriggeredMoveTestContext(_store, requester);
            MakeTransport("Rhino", capacity: 6, x: 11.5f, z: 10f);
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f);

            var unitCtx = NewActivation(ctx, squad);
            unitCtx.RegisterMoveFinished(GameWideConstants.RUSH_DISTANCE_INCHES, GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES); // rushed into contact

            bool routedToEmbark = false;
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToEmbark.Bind("ToEmbark");
            stage.ToEmbark.OnWillActivate += _ => routedToEmbark = true;
            await stage.Enter(unitCtx);

            Assert.That(requester.OfferedOptions, Does.Contain(CoreRuleCatalog.EmbarkRuleName),
                "a unit that rushed up to its transport may board with the move it just made.");
            Assert.That(routedToEmbark, Is.True);
        }

        // Stopping an inch short is the easy mistake to make, so the entry stays and changes its reason
        // from an instruction to an explanation rather than vanishing unexplained.
        [Test]
        public async Task ChooseAction_MovedAndStillShortOfContact_ExplainsRatherThanVanishing()
        {
            var requester = new RecordingActionRequester(ChooseActionStage.PASS_CHOICE_NAME);
            var ctx = new TriggeredMoveTestContext(_store, requester);
            MakeTransport("Rhino", capacity: 6, x: 16f, z: 10f); // 5" away
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f,
                new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0));
            // A chargeable enemy 1.5" off: something has to remain VALID or Choose Action auto-passes
            // without ever prompting, and then there is no menu for the greyed entry to appear on. At 1.5"
            // it is inside melee range (2") but outside the standoff band (1"), so Pass survives too.
            MakeEnemy("Cultists", x: 12.5f, z: 10f);

            var unitCtx = NewActivation(ctx, squad);
            unitCtx.RegisterMoveFinished(6f, GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES); // move already spent, and it didn't reach

            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToEmbark.Bind("ToEmbark");
            stage.ToReconcileEndOfActivation.Bind("end");
            await stage.Enter(unitCtx);

            Assert.That(requester.OfferedOptions, Does.Not.Contain(CoreRuleCatalog.EmbarkRuleName));
            var greyed = requester.OfferedInvalidOptions
                .FirstOrDefault(option => option.Option == CoreRuleCatalog.EmbarkRuleName);
            Assert.That(greyed, Is.Not.Null, "the entry stays, so the player is told why it is unavailable.");
            Assert.That(greyed!.Reason, Is.EqualTo("Not in contact with Rhino, and the move is spent."),
                "and the reason is an explanation, not the pre-move instruction to walk over.");
        }

        [Test]
        public async Task ChooseAction_NoTransport_DoesNotOfferEmbark()
        {
            var requester = new RecordingActionRequester(ChooseActionStage.PASS_CHOICE_NAME);
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f); // no transport anywhere

            var unitCtx = NewActivation(ctx, squad);
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToEmbark.Bind("ToEmbark");
            stage.ToReconcileEndOfActivation.Bind("end"); // the unit picks Pass, which routes here
            await stage.Enter(unitCtx);

            Assert.That(requester.OfferedOptions, Does.Not.Contain(CoreRuleCatalog.EmbarkRuleName),
                "no transport in range → no Embark action.");
        }

        [Test]
        public async Task EmbarkStage_BoardsUnit_SetsAsideAndEndsActivation()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, x: 11.5f, z: 10f);
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f);
            var ctx = new TriggeredMoveTestContext(_store, new EmbarkChoiceRequester(transport));

            var unitCtx = NewActivation(ctx, squad);
            bool embarked = false, back = false;
            var stage = new EmbarkStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnEmbarked.Bind("OnEmbarked"); stage.OnEmbarked.OnWillActivate += _ => embarked = true;
            stage.OnBackToChooseAction.Bind("OnBack"); stage.OnBackToChooseAction.OnWillActivate += _ => back = true;
            await stage.Enter(unitCtx);

            Assert.That(embarked, Is.True, "boarding fires OnEmbarked (ends the activation).");
            Assert.That(back, Is.False);
            Assert.That(TransportUtilities.IsEmbarked(squad.GetValue()), Is.True);
            Assert.That(TransportUtilities.GetTransportId(squad.GetValue()), Is.EqualTo(transport.GetValue().ID));
            Assert.That(squad.GetValue().GetIsOnBattlefield(), Is.False, "the unit is set aside off-table.");
        }

        [Test]
        public async Task EmbarkStage_Cancel_ReturnsToChooseAction()
        {
            MakeTransport("Rhino", capacity: 6, x: 11.5f, z: 10f);
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f);
            var ctx = new TriggeredMoveTestContext(_store, new EmbarkChoiceRequester(pickTransport: null)); // cancel

            var unitCtx = NewActivation(ctx, squad);
            bool embarked = false, back = false;
            var stage = new EmbarkStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnEmbarked.Bind("OnEmbarked"); stage.OnEmbarked.OnWillActivate += _ => embarked = true;
            stage.OnBackToChooseAction.Bind("OnBack"); stage.OnBackToChooseAction.OnWillActivate += _ => back = true;
            await stage.Enter(unitCtx);

            Assert.That(back, Is.True, "cancelling the transport choice returns to the action menu.");
            Assert.That(embarked, Is.False);
            Assert.That(TransportUtilities.IsEmbarked(squad.GetValue()), Is.False);
            Assert.That(squad.GetValue().GetIsOnBattlefield(), Is.True, "the unit stays on the table.");
        }

        // Regression: the universal Embark ability (AvailableWhen=Always) is in GatherOffers for every unit,
        // but it's gated out in ChooseActionStage. A unit that has moved + attacked has no real action left,
        // so Choose Action must auto-pass (end the activation) instead of prompting with a lone Pass option.
        [Test]
        public async Task ChooseAction_NoRealActions_AutoPassesWithoutPrompting()
        {
            var requester = new RecordingActionRequester(ChooseActionStage.PASS_CHOICE_NAME); // must not be consulted
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f); // no transport in range

            var unitCtx = NewActivation(ctx, squad);
            unitCtx.RegisterMoveFinished(0f, GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES);   // already moved
            unitCtx.RegisterAttackedFinished(); // already attacked

            bool ended = false;
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToReconcileEndOfActivation.Bind("end");
            stage.ToReconcileEndOfActivation.OnWillActivate += _ => ended = true;
            await stage.Enter(unitCtx);

            Assert.That(ended, Is.True, "with no action but Pass, the activation ends without prompting.");
            Assert.That(requester.OfferedOptions, Is.Empty, "no Choose Action prompt is shown when only Pass remains.");
        }

        // --- helpers ---

        private static UnitActionContext NewActivation(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return unitCtx;
        }

        // An enemy unit (its own player, so it is never screened out as an ally) parked on the table.
        private DataBinding<UnitData> MakeEnemy(string name, float x, float z)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(x, z), _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var enemyPlayer = new PlayerID(Guid.NewGuid());
            var unit = new UnitData(enemyPlayer, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // A unit on the table carrying the universal Embark ability FDGServer attaches at army-load.
        private DataBinding<UnitData> MakeSquad(string name, int modelCount, float x, float z,
            params Weapon[] weapons)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(weapons), new Position(x, z), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(
                new ResolvedRule(CoreRuleCatalog.EmbarkRuleName, CoreRuleCatalog.Embark));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private DataBinding<UnitData> MakeTransport(string name, int capacity, float x, float z)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(x, z), _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(_player, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            unit.AttachRuleDefinition(new ResolvedRule(TransportUtilities.TransportRuleName,
                CoreRuleCatalog.Transport, new RuleArgument[] { new RuleArgument.Int(capacity) }));
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
