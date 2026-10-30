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
    // #434 - the GDF supplement sweep's re-authored and new definitions that sit on existing primitives, through the
    // REAL stages where the effect lands (the RangeConditionalDefensiveRuleIntegrationTests / WeaponRules36Ap setups):
    // Spell Resistance and Guardian's AP(-1) at the wound/save stages, Dodge and Entrenched at DetermineHitRollStage,
    // Shatter / Sergeant / Piercing Marksman / Versatile Expert at RollToHitStage -> DetermineSaveRollsNeededStage,
    // Fortified Growth's round-start clock and AP reduction, Grounded Precision's terrain-only gate, and Infiltrating
    // Precision Strike's arrival-round +1 - each with its gate met and a control. Units are Quality 4 / Defense 4 and
    // the weapon AP(0) unless a case says otherwise, so a save threshold of 4 is "no modifier".
    [TestFixture]
    public class GdfSweep36RuleIntegrationTests
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

        // --- Spell Resistance -----------------------------------------------------------------------------------

        [Test]
        public async Task SpellResistance_SpellWound_IsIgnoredOnTwoPlus()
        {
            float wounds = await WoundsFromOneFailedSave(MakeUnit("Witches", 10f, 1, GdfSweep36Definitions.SpellResistance()),
                distance: 15f, isSpell: true);

            Assert.That(wounds, Is.EqualTo(1f / 6f).Within(0.0001f), "5 in 6 spell wounds ignored");
        }

        [TestCase(2f)]
        [TestCase(20f)]
        public async Task SpellResistance_OtherWounds_AreIgnoredOnSixPlus_AtAnyRange(float distance)
        {
            float wounds = await WoundsFromOneFailedSave(MakeUnit("Witches", 10f, 1, GdfSweep36Definitions.SpellResistance()),
                distance);

            Assert.That(wounds, Is.EqualTo(5f / 6f).Within(0.0001f),
                "unlike Knightborn / Warding, no 'shot from over 9\"' clause on the 6+ arm");
        }

        [Test]
        public async Task SpellResistance_InMelee_IsIgnoredOnSixPlus()
        {
            float wounds = await WoundsFromOneFailedSave(MakeUnit("Witches", 10f, 1, GdfSweep36Definitions.SpellResistance()),
                distance: 0.5f, melee: true);

            Assert.That(wounds, Is.EqualTo(5f / 6f).Within(0.0001f));
        }

        [Test]
        public async Task SpellResistance_OnAMinority_IgnoresNothing()
        {
            DataBinding<UnitData> unit = MakeUnit("Witches", 10f, 3, rule: null);
            unit.GetValue().ModelBindings[0].GetValue()
                .AttachRuleDefinition(new ResolvedRule("Spell Resistance", GdfSweep36Definitions.SpellResistance()));

            Assert.That(await WoundsFromOneFailedSave(unit, 15f, isSpell: true), Is.EqualTo(1f).Within(0.0001f));
        }

        // --- Dodge ----------------------------------------------------------------------------------------------

        [Test]
        public async Task Dodge_EnemiesGetMinusOneToHitInMelee()
        {
            DataBinding<UnitData> defender = MakeUnit("Dancers", 10f, 3, GdfSweep36Definitions.Dodge());

            Assert.That(await HitNeeded(defender, 0.5f, melee: true), Is.EqualTo(5));
        }

        [Test]
        public async Task Dodge_WhenShot_DoesNothing()
        {
            DataBinding<UnitData> defender = MakeUnit("Dancers", 10f, 3, GdfSweep36Definitions.Dodge());

            Assert.That(await HitNeeded(defender, 12f, melee: false), Is.EqualTo(4), "'-1 to hit rolls in melee'");
        }

        [Test]
        public async Task Dodge_WhileFatigued_DoesNothing()
        {
            DataBinding<UnitData> defender = MakeUnit("Dancers", 10f, 3, GdfSweep36Definitions.Dodge());
            FatigueUtilities.ApplyFatigued(defender.GetValue());

            Assert.That(await HitNeeded(defender, 0.5f, melee: true), Is.EqualTo(4), "'as long as they aren't Fatigued'");
        }

        [Test]
        public async Task Dodge_WhileShaken_DoesNothing()
        {
            // Shaken always counts as Fatigued in melee (#089) - the same predicate Counter and Brace read.
            DataBinding<UnitData> defender = MakeUnit("Dancers", 10f, 3, GdfSweep36Definitions.Dodge());
            defender.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));

            Assert.That(await HitNeeded(defender, 0.5f, melee: true), Is.EqualTo(4));
        }

        [Test]
        public async Task Dodge_OnAMinority_DoesNothing()
        {
            DataBinding<UnitData> defender = MakeUnit("Dancers", 10f, 3, rule: null);
            defender.GetValue().ModelBindings[0].GetValue()
                .AttachRuleDefinition(new ResolvedRule("Dodge", GdfSweep36Definitions.Dodge()));

            Assert.That(await HitNeeded(defender, 0.5f, melee: true), Is.EqualTo(4));
        }

        // --- Entrenched -----------------------------------------------------------------------------------------

        [TestCase(13f, false, 5)]
        [TestCase(11f, false, 4)]
        [TestCase(13f, true, 4)]
        public async Task Entrenched_UnmovedAndShotFromOverTwelve_EnemiesGetMinusOne(float distance, bool moved, int expected)
        {
            DataBinding<UnitData> defender = MakeUnit("Diggers", 10f, 3, GdfSweep36Definitions.Entrenched());
            if (moved) defender.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.MovedThisRound));

            Assert.That(await HitNeeded(defender, distance, melee: false), Is.EqualTo(expected),
                "3.6: over 12\" (was 9\") and -1 (was -2), only while the unit hasn't moved this round");
        }

        // --- Guardian / Primeborn -------------------------------------------------------------------------------

        [TestCase(2f)]
        [TestCase(20f)]
        public async Task Guardian_ReducesIncomingAp_AtAnyRange(float distance)
        {
            DataBinding<UnitData> defender = MakeUnit("Custodians", 10f, 1, GdfSweep36Definitions.Guardian());
            var ap1 = new Weapon("AP gun", 24f, 1, armorPenetration: 1);

            Assert.That(await SaveNeeded(ap1, defender, distance), Is.EqualTo(4),
                "AP(1) counts as AP(0) - 3.6 dropped the 'shot or charged from over 9\"' clause");
        }

        [Test]
        public async Task Guardian_Control_WithoutTheRule_TheApLands()
        {
            DataBinding<UnitData> defender = MakeUnit("Custodians", 10f, 1, rule: null);
            Assert.That(await SaveNeeded(new Weapon("AP gun", 24f, 1, armorPenetration: 1), defender, 2f), Is.EqualTo(5));
        }

        // --- Shatter / Sergeant ---------------------------------------------------------------------------------

        [Test]
        public async Task Shatter_OnANaturalSix_HitsGetApPlusThree()
        {
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new FixedDiceRoller(6));
            Weapon hammer = WeaponWith(GdfSweep36Definitions.Shatter());

            RollToHitResults hits = await Hits(hammer, MakeDefender(), 10f);

            Assert.That(hits.SuccessfulHitList.Single().SaveModifier, Is.EqualTo(-3));
        }

        [Test]
        public async Task Shatter_OffASix_AddsNothing_EvenAgainstToughTargets()
        {
            // The 3.5 Tough(3)+ gate is gone: a 5 against a Tough(6) target gets no AP.
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new FixedDiceRoller(5));
            Weapon hammer = WeaponWith(GdfSweep36Definitions.Shatter());

            RollToHitResults hits = await Hits(hammer, MakeDefender(tough: 6), 10f);

            Assert.That(hits.SuccessfulHitList.All(g => g.SaveModifier == 0), Is.True);
        }

        [Test]
        public async Task Sergeant_TheCarryingWeapon_GetsApPlusOne_InMeleeAndShooting()
        {
            Weapon champion = WeaponWith(GdfSweep36Definitions.Sergeant());

            Assert.That(await SaveNeeded(champion, MakeDefender(), 10f), Is.EqualTo(5), "shooting");
            Assert.That(await SaveNeeded(champion, MakeDefender(), 0.5f, isMelee: true), Is.EqualTo(5), "melee");
            Assert.That(await SaveNeeded(PlainWeapon(), MakeDefender(), 10f), Is.EqualTo(4),
                "control: a weapon without the champion's copy");
        }

        // --- Piercing Marksman / Piercing Charge ----------------------------------------------------------------

        [TestCase(10f, false, 5)]
        [TestCase(8f, false, 4)]
        [TestCase(0.5f, true, 4)]
        public async Task PiercingMarksman_ApPlusOne_OnlyShootingOverNine(float distance, bool melee, int expected)
        {
            DataBinding<UnitData> shooter = MakeUnit("Marksmen", 0f, 1, GdfSweep36Definitions.PiercingMarksman());

            Assert.That(await SaveNeeded(PlainWeapon(), MakeDefender(), distance, attacker: shooter, isMelee: melee),
                Is.EqualTo(expected));
        }

        [Test]
        public void PiercingMarksman_ASpellsHits_AreNotItsWeapons()
        {
            DataBinding<UnitData> caster = MakeUnit("Marksmen", 0f, 1, GdfSweep36Definitions.PiercingMarksman());
            DataBinding<UnitData> target = MakeDefender();
            Place(target, 15f);

            SyntheticHitResolution.Result spell = SyntheticHitResolution.Resolve(_ctx, caster.GetValue(),
                target.GetValue(), baseHits: 3, new Weapon("Bolt", 0f, 3, 0), isSpell: true);
            SyntheticHitResolution.Result notSpell = SyntheticHitResolution.Resolve(_ctx, caster.GetValue(),
                target.GetValue(), baseHits: 3, new Weapon("Bolt", 0f, 3, 0), isSpell: false);

            Assert.That(spell.SaveModifier, Is.EqualTo(0), "a spell from 15\" is not shooting with this model's weapons");
            Assert.That(notSpell.SaveModifier, Is.EqualTo(-1), "control: the same synthetic hits, not a spell");
        }

        [Test]
        public async Task PiercingCharge_ApPlusOne_OnlyWhenCharging()
        {
            DataBinding<UnitData> charger = MakeUnit("Chargers", 0f, 1, GdfSweep36Definitions.PiercingCharge());

            Assert.That(await SaveNeeded(PlainWeapon(), MakeDefender(), 0.5f, attacker: charger, isMelee: true,
                charging: true), Is.EqualTo(5));
            Assert.That(await SaveNeeded(PlainWeapon(), MakeDefender(), 0.5f, attacker: charger, isMelee: true,
                charging: false), Is.EqualTo(4), "striking back is not charging");
        }

        // --- Versatile Expert -----------------------------------------------------------------------------------

        [Test]
        public void VersatileExpert_OffersTwoLabelledPicks_AtActivationStart()
        {
            var harness = new TestRuleHarness();
            harness.Register(GdfSweep36Definitions.VersatileExpert());
            harness.Register(GdfSweep36Definitions.VersatileExpertPiercing());
            harness.Register(GdfSweep36Definitions.VersatileExpertPrecision());
            IUnit unit = harness.BuildUnit("P1", 3, "Versatile Expert");

            IReadOnlyList<AbilityOffer> offers = harness.OfferAbilities(new ActivationStartContext(unit));

            Assert.That(offers.Select(o => o.Ability.Label), Is.EqualTo(new[] { "AP(+1)", "+1 to hit" }));
            Assert.That(offers.Select(o => ((Effect.AddRule)o.Ability.Effect).Scope),
                Is.All.EqualTo(ELifetime.ThisActivation));
        }

        [TestCase(2f, false)]
        [TestCase(0.5f, true)]
        public async Task VersatileExpert_TheApPick_HasNoRangeGate(float distance, bool melee)
        {
            DataBinding<UnitData> attacker = MakeUnit("Experts", 0f, 1, GdfSweep36Definitions.VersatileExpertPiercing());

            Assert.That(await SaveNeeded(PlainWeapon(), MakeDefender(), distance, attacker: attacker, isMelee: melee),
                Is.EqualTo(5), "unlike Versatile Warrior, AP(+1) at any range, shooting or in melee");
        }

        [Test]
        public async Task VersatileExpert_TheHitPick_IsPlusOneToHit_InMelee()
        {
            DataBinding<UnitData> attacker = MakeUnit("Experts", 0f, 1, GdfSweep36Definitions.VersatileExpertPrecision());
            DataBinding<UnitData> defender = MakeDefender();

            Assert.That(await HitNeeded(defender, 0.5f, melee: true, attacker: attacker), Is.EqualTo(3));
        }

        // --- Fortified Growth -----------------------------------------------------------------------------------

        [Test]
        public void FortifiedGrowth_TicksAtRoundStart_UpToFour_ButNotWhileShaken()
        {
            var harness = new TestRuleHarness();
            harness.Register(GdfSweep36Definitions.FortifiedGrowth());
            IUnit unit = harness.BuildUnit("P1", 3, "Fortified Growth");

            for (int round = 0; round < 5; round++)
            {
                OperationApplier.ApplyTokenOperations(harness.Evaluate(unit, ERuleSeat.Actor, new RoundStartContext(unit)));
            }
            Assert.That(unit.Tokens.GetTokenCount(GdfSweep36Definitions.FortifiedGrowthMarker), Is.EqualTo(4),
                "one per round start, 'up to a max. of four tokens'");

            IUnit shaken = harness.BuildUnit("P1", 3, "Fortified Growth");
            shaken.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
            OperationApplier.ApplyTokenOperations(harness.Evaluate(shaken, ERuleSeat.Actor, new RoundStartContext(shaken)));
            Assert.That(shaken.Tokens.GetTokenCount(GdfSweep36Definitions.FortifiedGrowthMarker), Is.EqualTo(0),
                "'... if it's on the table and isn't Shaken'");
        }

        [TestCase(1, 6)]
        [TestCase(2, 5)]
        [TestCase(4, 4)]
        public async Task FortifiedGrowth_EveryTwoTokens_AttackersGetApMinusOne(int tokens, int expectedSave)
        {
            DataBinding<UnitData> defender = MakeUnit("Growers", 10f, 1, GdfSweep36Definitions.FortifiedGrowth());
            for (int i = 0; i < tokens; i++)
                defender.GetValue().Tokens.AddToken(new Token(GdfSweep36Definitions.FortifiedGrowthMarker, 1,
                    new TokenClearTrigger.ManualOnly()));

            Assert.That(await SaveNeeded(new Weapon("AP gun", 24f, 1, armorPenetration: 2), defender, 10f),
                Is.EqualTo(expectedSave), "AP(2) against Defense 4: one token does nothing, two take 1, four take 2");
        }

        // --- Grounded Precision ---------------------------------------------------------------------------------

        [Test]
        public void GroundedPrecision_AModelCarriedCopy_StillGetsPlusOne_WhenMostModelsAreInTerrain()
        {
            // 3.6 dropped "where most models have this rule": one model's copy (a joined champion) fires on the terrain
            // gate alone. 3 models at the origin, inside a 3" terrain disc.
            var harness = new TestRuleHarness();
            harness.Register(GdfSweep36Definitions.GroundedPrecision());
            IUnit unit = harness.BuildUnit("P1", 3);
            IModel carrier = unit.Models[0];
            ((ModelData)carrier).AttachRuleDefinition(new ResolvedRule("Grounded Precision",
                GdfSweep36Definitions.GroundedPrecision()));
            IUnit enemy = harness.BuildUnit("P2", 1);
            var terrain = new ITerrain[] { new TerrainData(ETerrainType.Cover, new CircularZone(new Float2(0f, 0f), 3f)) };

            int Net(IReadOnlyList<ITerrain>? pieces)
            {
                var context = new HitRollModifierContext(unit, enemy, DistanceInches: 12f, TerrainPieces: pieces);
                var sink = new RollModifierSink();
                sink.ApplyFrom(harness.EvaluateAll(context, RuleParticipant.Actor(unit, models: new[] { carrier })));
                return sink.Net(ERollKind.Hit);
            }

            Assert.That(Net(terrain), Is.EqualTo(1));
            Assert.That(Net(null), Is.EqualTo(0), "control: no terrain within 1\"");
        }

        // --- Infiltrating Precision Strike ----------------------------------------------------------------------

        [TestCase(true, true, 3)]
        [TestCase(false, true, 4)]
        [TestCase(true, false, 4)]
        public async Task InfiltratingPrecisionStrike_PlusOneInMelee_OnTheRoundItArrives(bool arrived, bool melee,
            int expected)
        {
            DataBinding<UnitData> attacker = MakeUnit("Infiltrators", 0f, 1,
                GdfSweep36Definitions.InfiltratingPrecisionStrike());
            if (arrived) attacker.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.ArrivedFromReserve));

            Assert.That(await HitNeeded(MakeDefender(), melee ? 0.5f : 12f, melee, attacker: attacker), Is.EqualTo(expected));
        }

        [Test]
        public void InfiltratingPrecisionStrike_DefersDeploymentLikeInfiltrate()
        {
            HookEntry defer = GdfSweep36Definitions.InfiltratingPrecisionStrike().Passive
                .Single(e => e.HookID == EHookID.Deployment_OnPreDeploymentSelect);

            Assert.That(defer.Effect, Is.EqualTo(new Effect.DeferDeployment(EDeferTiming.LaterRound, PlacementRangeInches: 3f)),
                "Infiltrate's own entry: later round, over 3\" from enemy units");
        }

        // --- helpers --------------------------------------------------------------------------------------------

        private async Task<int> HitNeeded(DataBinding<UnitData> defender, float distance, bool melee,
            DataBinding<UnitData>? attacker = null)
        {
            attacker ??= MakeUnit("Attacker", 0f, 1, rule: null);
            Place(defender, distance);

            var stage = new DetermineHitRollStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, new Weapon("Gun", melee ? 0f : 24f, 1, 0),
                weaponCount: 1, attackerMoved: false, isMelee: melee);
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True);
            return result.HitRollNeeded;
        }

        private async Task<int> SaveNeeded(Weapon weapon, DataBinding<UnitData> defender, float distance,
            DataBinding<UnitData>? attacker = null, bool isMelee = false, bool charging = false)
        {
            attacker ??= MakeUnit("Attacker", 0f, 1, rule: null);
            RollToHitResults hits = await Hits(weapon, defender, distance, attacker, isMelee, charging);

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
            DataBinding<UnitData>? attacker = null, bool isMelee = false, bool charging = false)
        {
            attacker ??= MakeUnit("Attacker", 0f, 1, rule: null);
            Place(defender, distance);

            var stage = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee, isCharging: charging);
            metadata.AddResult(new DetermineHitRollResults(4, 1));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True);
            return result;
        }

        private async Task<float> WoundsFromOneFailedSave(DataBinding<UnitData> defender, float distance,
            bool melee = false, bool isSpell = false)
        {
            DataBinding<UnitData> attacker = MakeUnit("Attacker", 0f, 1, rule: null);
            Place(defender, distance);

            var stage = new AssignWoundsStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            var metadata = new CombatMetadata(_ctx, attacker, defender, new Weapon("Gun", 24f, 1, 0), weaponCount: 1,
                attackerMoved: false, isMelee: melee, isCharging: melee, isSpell: isSpell);
            var failed = new List<FailedSaveInfo>
            {
                new FailedSaveInfo(TestDice.Faces(2), new PendingSaveRolls(TestDice.Faces(2), 4)),
            };
            metadata.AddResult(new RollToSaveResults(new List<SuccessfulSaveInfo>(), failed));
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out AssignWoundsResults result), Is.True);
            return result.TotalWoundsToAssign;
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
