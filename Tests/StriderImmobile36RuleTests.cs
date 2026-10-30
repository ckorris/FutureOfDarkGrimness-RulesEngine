using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #427 - OPR 3.6 Strider ("May ignore the effects of Difficult terrain.") and Immobile ("May only use Hold
    // actions."). Both behaved this way in 3.5; the work here is the 3.6 wording and pinning the two edges that
    // the existing tests left implicit: Strider is NOT Flying (Impassable still blocks it), and an Immobile unit
    // loses Charge as well as Move even when an enemy stands inside the 3\" strike range.
    [TestFixture]
    public class StriderImmobile36RuleTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void TheDescriptions_AreTheCore360Wording()
        {
            Assert.That(CoreRuleCatalog.Strider.Description, Is.EqualTo("May ignore the effects of Difficult terrain."));
            Assert.That(CoreRuleCatalog.Immobile.Description, Is.EqualTo("May only use Hold actions."));
        }

        [Test]
        public void Strider_IgnoresTheDifficultCap_ButImpassableStillBlocksIt()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> strider = MakeUnit(CoreRuleCatalog.Strider);
            DataBinding<UnitData> plain = MakeUnit(null);
            var difficult = new List<ITerrain> { new TerrainData(ETerrainType.Difficult, new RectangularZone(2, 4, -2, 2)) };
            var wall = new List<ITerrain> { new TerrainData(ETerrainType.Impassible, new RectangularZone(4, 6, -2, 2)) };

            Assert.That(Validate(ctx, plain, difficult), Is.False, "control: a 10in move through Difficult breaks the 6in cap");
            Assert.That(Validate(ctx, strider, difficult), Is.True, "Strider ignores the effects of Difficult terrain");
            Assert.That(Validate(ctx, strider, wall), Is.False,
                "'Difficult terrain' only: Strider is not Flying, Impassable still stops it");
        }

        [Test]
        public async Task Immobile_WithAnEnemyInStrikeRange_LosesMoveAndCharge()
        {
            var player = new PlayerID(Guid.NewGuid());
            DataBinding<UnitData> unit = MakeImmobileUnit(player);
            var enemyPlayer = new PlayerID(Guid.NewGuid());
            var enemyModel = new ModelData(0.5f, new List<Weapon>(), new Position(3f, 0f), _store);
            var enemy = new UnitData(enemyPlayer, "Grunts", quality: 4, defense: 4, modelBindings:
                new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(enemyModel)) });
            _store.Create(new ArmyData(enemyPlayer,
                new List<DataBinding<UnitData>> { _store.GetDataBinding<UnitData>(_store.Create(enemy)) }));

            var capturer = new CapturingStringSelectionRequester("Pass");
            var ctx = new TriggeredMoveTestContext(_store, capturer);
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToMovement.Bind("Move");
            stage.ToCharge.Bind("Charge");
            stage.ToShoot.Bind("Shoot");
            stage.ToCustomAction.Bind("Custom");
            stage.ToDisembark.Bind("Disembark");
            stage.ToEmbark.Bind("Embark");
            stage.ToReconcileEndOfActivation.Bind("Done");
            await stage.Enter(unitCtx);

            ChooseActionRequest req = capturer.Captured!;
            Assert.That(req.ValidOptions, Does.Not.Contain(ChooseActionStage.MOVEMENT_CHOICE_NAME));
            Assert.That(req.ValidOptions, Does.Not.Contain(ChooseActionStage.CHARGE_CHOICE_NAME),
                "the enemy is 1in away (inside the 3in strike range), so Charge would be offered - but Immobile 'may only use Hold actions'");
            Assert.That(req.InvalidOptions.Any(o => o.Option == ChooseActionStage.CHARGE_CHOICE_NAME
                                                     && o.Reason == "Immobile."), Is.True,
                "and the menu says why: " + string.Join(" | ", req.InvalidOptions.Select(o => $"{o.Option}={o.Reason}")));
        }

        // --- helpers ---------------------------------------------------------------------------------------

        // Validates a 10in straight move with the flags the live resolvers derive from the unit's own rules.
        private bool Validate(WoundTestContext ctx, DataBinding<UnitData> unit, List<ITerrain> terrain)
        {
            var move = new ModelMoveEntry(unit.GetValue().ModelBindings[0], new List<Position> { new Position(10f, 0f) });
            return MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move }, maxDistanceInches: 20f,
                new List<EnemyModelFootprint>(),
                MovementRuleQueries.CanMoveThroughEnemies(unit.GetValue(), ctx.RuleEvaluator),
                MovementRuleQueries.IgnoresDifficultTerrain(unit.GetValue(), ctx.RuleEvaluator),
                MovementRuleQueries.IgnoresAllTerrain(unit.GetValue(), ctx.RuleEvaluator),
                terrain, out _);
        }

        private DataBinding<UnitData> MakeUnit(SpecialRuleDefinition? rule)
        {
            var model = new ModelData(0.75f, new List<Weapon>(), new Position(0f, 0f), _store);
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Unit", quality: 4, defense: 4, modelBindings:
                new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) });
            if (rule != null) unit.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        // The RestrictActionsRuleIntegrationTests unit: Immobile plus a trivial custom action so ChooseAction
        // issues its menu instead of short-circuiting to Pass.
        private DataBinding<UnitData> MakeImmobileUnit(PlayerID player)
        {
            var model = new ModelData(0.5f, new List<Weapon> { new Weapon("Fists", 0f, 1, 0) }, new Position(1f, 0f), _store);
            var unit = new UnitData(player, "Immobile Unit", quality: 4, defense: 4, modelBindings:
                new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule("Immobile", CoreRuleCatalog.Immobile));

            var ability = new ActivatedAbility(EHookID.Activation_OnActionChoice, new Cost.OncePerActivation(),
                new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                new Effect.GrantToken(new TokenType("CustomFired"), new ValueSource.Literal(1),
                    new TokenClearTrigger.ManualOnly()),
                new Condition.Always());
            binding.GetValue().AttachRuleDefinition(new ResolvedRule("Test Cast",
                new SpecialRuleDefinition("Test Cast", Array.Empty<HookEntry>(), new[] { ability })));

            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
