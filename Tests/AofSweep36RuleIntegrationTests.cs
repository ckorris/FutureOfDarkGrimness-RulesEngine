using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #435 - the AoF supplement sweep's re-authored definitions that sit on existing primitives, through the REAL stages
    // where the effect lands (the GdfSweep36RuleIntegrationTests rig): Great Sergeant's AP(+2) at RollToHitStage ->
    // DetermineSaveRollsNeededStage, and Ethereal's "if you do" move penalty through TeleportStage. Units are Quality 4 / Defense 4 and the weapon AP(0) unless a case says otherwise,
    // so a save threshold of 4 is "no modifier". (Banner / Musician / Great Banner / Great Musician drive the morale and
    // movement sites in AofCommandGroup36RuleIntegrationTests.)
    [TestFixture]
    public class AofSweep36RuleIntegrationTests
    {
        private const float BaseRadius = 0.5f;

        private GameDataStore _store = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new ProbabilisticDiceRoller());
        }

        // --- Great Sergeant -----------------------------------------------------------------------------------------

        [Test]
        public async Task GreatSergeant_TheCarryingWeapon_GetsApPlusTwo_InMeleeAndShooting()
        {
            Weapon champion = WeaponWith(AofSweep36Definitions.GreatSergeant());

            Assert.That(await SaveNeeded(champion, MakeDefender(), 10f), Is.EqualTo(6), "shooting: Defense 4+ at AP(2)");
            Assert.That(await SaveNeeded(champion, MakeDefender(), 0.5f, isMelee: true), Is.EqualTo(6), "melee");
            Assert.That(await SaveNeeded(PlainWeapon(), MakeDefender(), 10f), Is.EqualTo(4),
                "control: a weapon without the champion's copy");
        }

        [Test]
        public async Task GreatSergeant_NoLongerAddsHits_OnFivesAndSixes()
        {
            // 3.5 Great Sergeant dealt an extra hit on each unmodified 5-6; 3.6 is AP only.
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new FixedDiceRoller(6));

            RollToHitResults hits = await Hits(WeaponWith(AofSweep36Definitions.GreatSergeant()), MakeDefender(), 10f);

            Assert.That(hits.SuccessfulHitList.Sum(g => g.HitCount), Is.EqualTo(1f));
        }

        // --- Ethereal ----------------------------------------------------------------------------------------------

        [TestCase(EActionType.Advance)]
        [TestCase(EActionType.Rush)]
        [TestCase(EActionType.Charge)]
        public async Task Ethereal_AfterTeleporting_MovesMinusSix_UntilTheActivationEnds(EActionType action)
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedPlaceRequester(new Position(13f, 10f)));
            DataBinding<UnitData> unit = EtherealUnit();

            await Teleport(ctx, unit);

            Assert.That(NetMove(ctx, unit, action), Is.EqualTo(-6f), "'if you do, this model moves -6\"'");
            Token teleported = unit.GetValue().Tokens.GetAllTokens(TokenType.TeleportedThisActivation).Single();
            Assert.That(teleported.ClearTrigger, Is.InstanceOf<TokenClearTrigger.ActivationEnd>(),
                "'until the end of that activation'");
        }

        [Test]
        public async Task Ethereal_AnAcceptedPlacementThatStandsStill_CostsNoMovement()
        {
            // The CLI / AI default answer to the placement is "stay put": the once-per-activation cost is paid (so the
            // offer does not loop), but no model was placed elsewhere - not "you do".
            var ctx = new TriggeredMoveTestContext(_store, new CannedPlaceRequester(new Position(10f, 10f)));
            DataBinding<UnitData> unit = EtherealUnit();

            await Teleport(ctx, unit);

            Assert.That(unit.GetValue().Tokens.HasToken(new TokenType("AbilityUsed:Ethereal")), Is.True, "cost paid");
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.TeleportedThisActivation), Is.False);
            Assert.That(NetMove(ctx, unit, EActionType.Advance), Is.EqualTo(0f));
        }

        [Test]
        public void Ethereal_WithoutTeleporting_MovesNormally()
        {
            // 3.5 Ethereal moved -6" on every move; 3.6 only after the placement.
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> unit = EtherealUnit();

            Assert.That(NetMove(ctx, unit, EActionType.Advance), Is.EqualTo(0f));
            Assert.That(NetMove(ctx, unit, EActionType.Rush), Is.EqualTo(0f));
        }

        [Test]
        public async Task Ethereal_ATeleportBackedOutOf_CostsNoMovement()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CancellingPlaceRequester());
            DataBinding<UnitData> unit = EtherealUnit();

            await Teleport(ctx, unit);

            Assert.That(NetMove(ctx, unit, EActionType.Advance), Is.EqualTo(0f), "a declined placement is not 'if you do'");
        }

        // --- helpers ------------------------------------------------------------------------------------------------

        private DataBinding<UnitData> EtherealUnit()
        {
            var player = new PlayerID(System.Guid.NewGuid());
            var model = new ModelData(BaseRadius, new List<Weapon>(), new Position(10f, 10f), _store);
            var unit = new UnitData(player, "Spirits", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) });
            SpecialRuleDefinition ethereal = AofSweep36Definitions.Ethereal();
            unit.AttachRuleDefinition(new ResolvedRule(ethereal.Name, ethereal));
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // ChooseActionStage routes the Ethereal offer (an Effect.Teleport) to TeleportStage, which pays the cost - the
        // used marker - only when the placement is accepted.
        private static async Task Teleport(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            unitCtx.SetPendingCustomAction(ctx.RuleEvaluator.GatherOffers(new ActionChoiceContext(unit.GetValue()))
                .Single(o => o.RuleName == "Ethereal"));
            var stage = new TeleportStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);
        }

        private static float NetMove(IGameContext ctx, DataBinding<UnitData> unit, EActionType action)
        {
            var sink = new MovementModifierSink();
            sink.ApplyFrom(ctx.RuleEvaluator.EvaluateAll(new MoveActionDeclaredContext(unit.GetValue(), action, 6f),
                RuleParticipant.Actor(unit.GetValue())));
            return sink.Net(action);
        }

        private async Task<int> SaveNeeded(Weapon weapon, DataBinding<UnitData> defender, float distance,
            DataBinding<UnitData>? attacker = null, bool isMelee = false, bool charging = false, bool moved = false)
        {
            attacker ??= MakeUnit("Attacker", 0f, 1, rule: null);
            RollToHitResults hits = await Hits(weapon, defender, distance, attacker, isMelee, charging, moved);

            var stage = new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);
            metadata.AddResult(hits);
            metadata.AddResult(new CoverCheckResults(0));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineSaveRollNeededResults result), Is.True);
            return result.PendingSaveRollsList.Max(p => p.SaveNeeded);
        }

        private async Task<RollToHitResults> Hits(Weapon weapon, DataBinding<UnitData> defender, float distance,
            DataBinding<UnitData>? attacker = null, bool isMelee = false, bool charging = false, bool moved = false)
        {
            attacker ??= MakeUnit("Attacker", 0f, 1, rule: null);
            Place(defender, distance);

            var stage = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: moved, isMelee: isMelee, isCharging: charging);
            metadata.AddResult(new DetermineHitRollResults(4, 1));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True);
            return result;
        }

        private static Weapon PlainWeapon() => new Weapon("Plain gun", rangeInches: 24f, attacks: 1, armorPenetration: 0);

        private static Weapon WeaponWith(SpecialRuleDefinition rule)
        {
            var weapon = new Weapon("Test weapon", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            weapon.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return weapon;
        }

        private DataBinding<UnitData> MakeDefender(int tough = 1) => MakeUnit("Target", 10f, 1, rule: null, tough: tough);

        private DataBinding<UnitData> MakeUnit(string name, float z, int models, SpecialRuleDefinition? rule, int tough = 1)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(BaseRadius, new List<Weapon>(), new Position(i * 1.5f, z), _store);
                if (tough > 1) model.SetMaxWounds(tough);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: bindings);
            if (rule != null) unit.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private static void Place(DataBinding<UnitData> unit, float edgeDistance)
        {
            List<DataBinding<ModelData>> models = unit.GetValue().ModelBindings;
            for (int i = 0; i < models.Count; i++)
            {
                models[i].GetValue().SetPosition(new Position(i * 1.5f, edgeDistance + 2f * BaseRadius));
            }
        }
    }
}
