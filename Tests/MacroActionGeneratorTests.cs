using FDG.Ai.Tactician;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 A3c — the goal enumerator on authored scenes: one behavioral test per intent family,
    // the diversity-pruning guarantee, engine-validity of every emitted move, and the GATING
    // generator-level hallway probe (plan sec. 8 A3 verify / 6.2).
    [TestFixture]
    public class MacroActionGeneratorTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private RuleEvaluator _evaluator = null!;
        private PlayerID _us;
        private PlayerID _them;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public void Enumerate_AlwaysIncludesHold_EvenOnAnEmptyTable()
        {
            var unit = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 20f);

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, unit);

            Assert.That(actions.Count(a => a.Intent == EMacroIntent.Hold), Is.EqualTo(1));
            Assert.That(actions.Single(a => a.Intent == EMacroIntent.Hold).Feasibility,
                Is.EqualTo(EFeasibility.Reachable));
        }

        [Test]
        public void RushObjective_InReach_EndsWithinSeizeRadius()
        {
            MakeObjective(new Position(28f, 20f));
            var unit = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 20f); // 8" away, rush 12

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, unit);
            MacroAction rush = actions.First(a => a.Intent == EMacroIntent.RushObjective);

            Assert.That(rush.Feasibility, Is.EqualTo(EFeasibility.Reachable));
            Assert.That(Distance(rush.ProjectedCentroid, new Position(28f, 20f)),
                Is.LessThanOrEqualTo(TacticalAnalysis.ObjectiveSeizureRadiusInches));
        }

        [Test]
        public void EngageAtRange_KiteBand_MovesAwayFromTheMeleeThreat()
        {
            // Melee-only enemy 8" away: its threat is charge reach (12"); our rifle reaches 24".
            // The kite band exists and the candidate moves AWAY from the enemy - that is the point.
            var unit = MakeUnit(_us, 3, Rifle(range: 24f), atX: 20f, atZ: 20f);
            var horde = MakeUnit(_them, 5, Blade(), atX: 28f, atZ: 20f);

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, unit);
            MacroAction? kite = actions.FirstOrDefault(a =>
                a.Intent == EMacroIntent.EngageAtRange && a.Band == ERangeBand.SafeShooting);

            Assert.That(kite, Is.Not.Null, "a kite band exists when our reach exceeds their threat");
            float before = Distance(new Position(20f, 20f), Centroid(horde));
            float after = Distance(kite!.ProjectedCentroid, Centroid(horde));
            Assert.That(after, Is.GreaterThan(before), "kiting must open the distance");
            Assert.That(after, Is.LessThanOrEqualTo(24f), "while staying inside our own reach");
        }

        [Test]
        public void ChargeToContact_EnemyInReach_EndsInBaseContact()
        {
            var unit = MakeUnit(_us, 3, Blade(), atX: 20f, atZ: 20f);
            MakeUnit(_them, 3, Blade(), atX: 27f, atZ: 20f); // 7" away, charge 12

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, unit);
            MacroAction charge = actions.First(a => a.Intent == EMacroIntent.ChargeToContact);

            Assert.That(charge.Feasibility, Is.EqualTo(EFeasibility.Reachable));
            float gap = MovementPlanner.MinEnemyGap(charge.Move,
                MovementPlanner.LiveEnemyFootprints(_tableState, _us));
            Assert.That(gap, Is.LessThanOrEqualTo(0.25f), "a reachable charge ends in contact");
        }

        [Test]
        public void ChargeToContact_CheapNearbyEnemy_SurvivesValuePruning()
        {
            // #361 facet 1, the Hive Lord shape: a melee monster with a cheap enemy in clean charge
            // reach while three expensive units sit far away. Value-only top-3 pruning dropped the
            // ONLY chargeable enemy from every targeted family - the nearest-enemies union must
            // keep it, and its charge must grade Reachable.
            var monster = MakeUnit(_us, 1, Blade(), atX: 20f, atZ: 20f, woundsPerModel: 6);
            var cheap = MakeUnit(_them, 1, Rifle(), atX: 29f, atZ: 20f); // 9" away, charge 12
            MakeUnit(_them, 8, Rifle(ap: 2), atX: 52f, atZ: 10f, woundsPerModel: 2);
            MakeUnit(_them, 8, Rifle(ap: 2), atX: 52f, atZ: 25f, woundsPerModel: 2);
            MakeUnit(_them, 8, Rifle(ap: 2), atX: 52f, atZ: 40f, woundsPerModel: 2);

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, monster);
            MacroAction? charge = actions.FirstOrDefault(a =>
                a.Intent == EMacroIntent.ChargeToContact
                && ReferenceEquals(a.TargetEnemy, cheap.GetValue()));

            Assert.That(charge, Is.Not.Null,
                "the enemy standing next to us must be evaluated no matter how cheap it is:\n"
                + string.Join("\n", actions.Select(a => a.Rationale)));
            Assert.That(charge!.Feasibility, Is.EqualTo(EFeasibility.Reachable),
                "9\" with a clean lane is a reachable charge");
        }

        [Test]
        public void ChargeFamily_RanksNearestEnemyFirst_WhenFeasibilityTies()
        {
            // #361: two enemies both in clean charge reach - the nearer, cheaper one must head the
            // family. In-family pruning ranks by feasibility with the GENERATION order as tiebreak,
            // and value-first generation let a high-value target eat the family's budget slot while
            // the enemy standing next to us was cut (the Hive Lord save's APC, round two).
            var monster = MakeUnit(_us, 1, Blade(), atX: 20f, atZ: 20f, woundsPerModel: 6);
            var near = MakeUnit(_them, 1, Rifle(), atX: 27f, atZ: 24f);
            MakeUnit(_them, 8, Rifle(ap: 2), atX: 30f, atZ: 16f, woundsPerModel: 2);

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, monster);
            List<MacroAction> charges = actions.Where(a => a.Intent == EMacroIntent.ChargeToContact).ToList();

            Assert.That(charges, Has.Count.GreaterThanOrEqualTo(2),
                "both in-reach enemies keep a charge candidate");
            Assert.That(charges[0].TargetEnemy, Is.SameAs(near.GetValue()),
                "the family's first slot belongs to the nearest enemy, not the most valuable:\n"
                + string.Join("\n", charges.Select(c => c.Rationale)));
        }

        [Test]
        public void ChargeToContact_BigRectBaseParkedByRotatedTerrain_StillMakesProgress()
        {
            // #361 facet 2, the Hive Lord wedge (geometry lifted from the save): a 3.62"x4.72" rect
            // base parked 0.03" from a rotated tank-trap bar. The swept-base validator (#341) is
            // right that ANY north/east move clips the bar - but planning terrain clearance with the
            // INSCRIBED BaseRadiusInches (1.81 vs the corner's true 2.98 reach) routed corridors the
            // base cannot take, every backoff arc failed, and the charge against an enemy 10" away
            // graded Blocked at the unit's own centroid. Clearance must be circumscribed: the route
            // then bends west around the bar and the charge makes real progress.
            var monster = MakeRectBaseUnit(_us, widthInches: 3.6220472f, heightInches: 4.7244096f,
                Blade(), atX: 48.51327f, atZ: 17.038704f, woundsPerModel: 6);
            _store.Create(new TerrainData(ETerrainType.Impassible, new RotatedZoneWrapper(
                new RectangularZone(49.38774f, 54.38774f, 20.907413f, 21.907413f),
                225f, new Float2(51.88774f, 21.407413f))));
            var apc = MakeUnit(_them, 1, Rifle(), atX: 42.6f, atZ: 25.5f);
            Position enemyPos = Centroid(apc);
            Position start = new Position(48.51327f, 17.038704f);

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, monster);
            MacroAction charge = actions.First(a => a.Intent == EMacroIntent.ChargeToContact);

            Assert.That(charge.Feasibility, Is.Not.EqualTo(EFeasibility.Blocked),
                "a reachable enemy behind a routable bar must not grade Blocked-at-zero, end="
                + $"({charge.ProjectedCentroid.x:F1},{charge.ProjectedCentroid.z:F1})");
            float closed = Distance(start, enemyPos) - Distance(charge.ProjectedCentroid, enemyPos);
            Assert.That(closed, Is.GreaterThanOrEqualTo(3f),
                $"the charge must make real progress toward contact, closed only {closed:F2}\"");
        }

        [Test]
        public void ChargeToContact_RoutedAroundTerrain_RefinesToBaseContact()
        {
            // #361 facet 4: the same wedge scene as the routed-progress pin, but "made progress" is
            // not enough - the routed goal (circumscribed radii, always legal) plus the validated
            // NudgeToContact must land the monster in base contact, so the fighting charge reaches
            // the scorer as a Charge action with melee credit, not a rush-to-standoff.
            var monster = MakeRectBaseUnit(_us, widthInches: 3.6220472f, heightInches: 4.7244096f,
                Blade(), atX: 48.51327f, atZ: 17.038704f, woundsPerModel: 6);
            _store.Create(new TerrainData(ETerrainType.Impassible, new RotatedZoneWrapper(
                new RectangularZone(49.38774f, 54.38774f, 20.907413f, 21.907413f),
                225f, new Float2(51.88774f, 21.407413f))));
            MakeUnit(_them, 1, Rifle(), atX: 42.6f, atZ: 25.5f);

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, monster);
            MacroAction charge = actions.First(a => a.Intent == EMacroIntent.ChargeToContact);

            Assert.That(charge.ActionType, Is.EqualTo(EActionType.Charge),
                $"the charge must be playable AS a charge, got {charge.ActionType} "
                + $"({charge.Feasibility}, end=({charge.ProjectedCentroid.x:F1},{charge.ProjectedCentroid.z:F1}))");
            Assert.That(charge.Feasibility, Is.EqualTo(EFeasibility.Reachable));
            float gap = MovementPlanner.MinEnemyGap(charge.Move,
                MovementPlanner.LiveEnemyFootprints(_tableState, _us));
            Assert.That(gap, Is.LessThanOrEqualTo(0.25f),
                $"the routed charge must end in base contact, gap={gap:F2}\"");
        }

        [Test]
        public void FallBack_OpensDistanceFromTheThreat()
        {
            var unit = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 20f);
            var threat = MakeUnit(_them, 5, Blade(), atX: 26f, atZ: 20f);

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, unit);
            MacroAction fallBack = actions.First(a => a.Intent == EMacroIntent.FallBack);

            Assert.That(Distance(fallBack.ProjectedCentroid, Centroid(threat)),
                Is.GreaterThan(Distance(new Position(20f, 20f), Centroid(threat)) + 3f),
                "falling back must meaningfully open the gap");
        }

        [Test]
        public void Block_LineFormsAcrossTheThreatLane()
        {
            // Threat to the east, precious cargo (high-value friend) to the west: the block line
            // should land between them, spread perpendicular to the lane (i.e. along Z).
            var unit = MakeUnit(_us, 4, Blade(), atX: 30f, atZ: 30f);
            MakeUnit(_us, 5, Rifle(ap: 2), atX: 10f, atZ: 20f, woundsPerModel: 3); // the asset
            MakeUnit(_them, 5, Blade(), atX: 40f, atZ: 20f);                        // the threat

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, unit);
            MacroAction block = actions.First(a => a.Intent == EMacroIntent.Block);

            // Between: projected centroid's x sits inside the lane segment (10..40) near its middle.
            Assert.That(block.ProjectedCentroid.x, Is.InRange(15f, 35f));
            // Line shape: destination spread along Z exceeds spread along X (lane runs mostly east-west).
            var ends = block.Move.Select(e => e.Positions[^1]).ToList();
            float spanX = ends.Max(p => p.x) - ends.Min(p => p.x);
            float spanZ = ends.Max(p => p.z) - ends.Min(p => p.z);
            Assert.That(spanZ, Is.GreaterThan(spanX), "a barrier is a line across the lane, not a block");
        }

        [Test]
        public void Concentrate_MovesTowardTheArmyCentroid()
        {
            var unit = MakeUnit(_us, 3, Rifle(), atX: 60f, atZ: 40f);
            MakeUnit(_us, 5, Rifle(), atX: 15f, atZ: 15f);
            MakeUnit(_us, 5, Blade(), atX: 20f, atZ: 25f);

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, unit);
            MacroAction mass = actions.First(a => a.Intent == EMacroIntent.Concentrate);

            var armyCenter = new Position(17.5f, 20f);
            Assert.That(Distance(mass.ProjectedCentroid, armyCenter),
                Is.LessThan(Distance(new Position(60f, 40f), armyCenter) - 6f),
                "concentrating must close most of a rush move toward the mass");
        }

        // The GATING probe (plan 6.2 'hallway', at the generator level per A3 verify): an objective
        // beyond a narrow corridor - a corridor-traversing candidate must be emitted.
        [Test]
        public void HallwayProbe_ObjectiveBeyondCorridor_TraversingCandidateEmitted()
        {
            _store.Create(new TerrainData(ETerrainType.Impassible, new RectangularZone(24f, 28f, 0f, 22f)));
            _store.Create(new TerrainData(ETerrainType.Impassible, new RectangularZone(24f, 28f, 26f, 48f)));
            MakeObjective(new Position(40f, 24f));
            var unit = MakeUnit(_us, 3, Rifle(), atX: 16f, atZ: 24f);

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, unit);
            var objectiveRuns = actions.Where(a =>
                a.Intent is EMacroIntent.AdvanceOnObjective or EMacroIntent.RushObjective).ToList();

            Assert.That(objectiveRuns, Is.Not.Empty);
            float before = Distance(new Position(16f, 24f), new Position(40f, 24f));
            float bestAfter = objectiveRuns.Min(a => Distance(a.ProjectedCentroid, new Position(40f, 24f)));
            Assert.That(bestAfter, Is.LessThan(before - 6f),
                "a corridor-traversing candidate must make most of a move's progress toward the objective");
        }

        [Test]
        public void Pruning_SmallBudget_StillKeepsOneCandidatePerFamily()
        {
            // A rich scene that produces every family, then a budget smaller than the family count.
            MakeObjective(new Position(30f, 30f));
            _store.Create(new TerrainData(ETerrainType.Cover, new RectangularZone(22f, 26f, 22f, 26f)));
            var unit = MakeUnit(_us, 4, RifleAndBlade(), atX: 20f, atZ: 20f);
            MakeUnit(_us, 5, Rifle(ap: 2), atX: 10f, atZ: 12f, woundsPerModel: 2);
            MakeUnit(_them, 5, Blade(), atX: 32f, atZ: 20f);

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, unit,
                candidateBudget: 6);

            var families = actions.Select(a => a.Intent).Distinct().ToList();
            foreach (EMacroIntent family in new[]
            {
                EMacroIntent.Hold, EMacroIntent.AdvanceOnObjective, EMacroIntent.RushObjective,
                EMacroIntent.EngageAtRange, EMacroIntent.ChargeToContact, EMacroIntent.FallBack,
                EMacroIntent.SeekCoverFrom, EMacroIntent.Block, EMacroIntent.Escort,
                EMacroIntent.Concentrate,
            })
            {
                Assert.That(families, Does.Contain(family),
                    $"the diversity rule guarantees family {family} survives the budget");
            }
        }

        [Test]
        public void EveryEmittedMove_PassesTheEngineValidator()
        {
            MakeObjective(new Position(30f, 30f));
            var unit = MakeUnit(_us, 4, RifleAndBlade(), atX: 20f, atZ: 20f);
            MakeUnit(_us, 3, Rifle(), atX: 12f, atZ: 12f);
            MakeUnit(_them, 5, Blade(), atX: 32f, atZ: 20f);

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, unit);
            var enemies = MovementPlanner.LiveEnemyFootprints(_tableState, _us);
            var terrain = _tableState.Terrain.Objects.ToList();

            foreach (MacroAction action in actions)
            {
                bool valid = MovementUtilities.ValidatePaths(action.Move,
                    _ => new ModelMoveBudget(24f, 24f), // generous: geometry/cohesion is what's under test
                    enemies, false, false, false, terrain, out List<ReasonForInvalidMove> reasons);
                Assert.That(valid, Is.True,
                    $"{action.Rationale}: emitted move must be engine-valid ({string.Join(", ", reasons)})");
            }
        }

        [Test]
        public void ChargeThroughAFriendly_PlansAValidMove()
        {
            // #216: a friendly parked on the charge lane's endpoint. Friendly-blind planning built
            // a charge the #205 stacking check rejects at resolve time - and the rejection silently
            // degraded the whole activation to the solo resolver. The emitted candidate must
            // already be valid against friendly footprints (the backoff ladder shortens it).
            var brawlers = MakeUnit(_us, 3, Blade(), atX: 20f, atZ: 24f);
            MakeUnit(_us, 3, Rifle(), atX: 29.5f, atZ: 24f); // squatting on the contact point
            MakeUnit(_them, 3, Rifle(), atX: 32f, atZ: 24f);

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, brawlers);
            MacroAction charge = actions.First(a => a.Intent == EMacroIntent.ChargeToContact);
            var enemies = MovementPlanner.LiveEnemyFootprints(_tableState, _us);
            var friendlies = MovementPlanner.LiveFriendlyFootprints(_tableState, _us,
                brawlers.GetValue().ID);

            bool valid = MovementUtilities.ValidatePaths(charge.Move,
                _ => new ModelMoveBudget(24f, 24f), enemies, false, false, false,
                _tableState.Terrain.Objects.ToList(), out List<ReasonForInvalidMove> reasons,
                friendlies);
            Assert.That(valid, Is.True,
                $"the charge plan must not end stacked on the friendly ({string.Join(", ", reasons)})");
        }

        [Test]
        public void MoveToCast_CasterOutOfSpellRange_MovesIntoRange()
        {
            // A Foe-affinity 12" spell, target 20" away: the set-up move closes toward cast range.
            var caster = MakeUnit(_us, 1, Rifle(), atX: 10f, atZ: 24f);
            caster.GetValue().Tokens.AddToken(new Token(
                TokenType.SpellTokens, 3, new TokenClearTrigger.ManualOnly()));
            var enemy = MakeUnit(_them, 3, Blade(), atX: 30f, atZ: 24f);
            MakeArmyWithSpell(_us, caster, new TargetSelector(
                12f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: true));

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, caster);
            MacroAction? cast = actions.FirstOrDefault(a => a.Intent == EMacroIntent.MoveToCast);

            Assert.That(cast, Is.Not.Null, "an affordable out-of-range spell generates a set-up move");
            float before = Distance(new Position(10f, 24f), Centroid(enemy));
            float after = Distance(cast!.ProjectedCentroid, Centroid(enemy));
            Assert.That(after, Is.LessThan(before), "the set-up move closes toward cast range");
        }

        [Test]
        public void MoveToCast_NotGenerated_WithoutSpellTokens()
        {
            var unit = MakeUnit(_us, 1, Rifle(), atX: 10f, atZ: 24f);
            MakeUnit(_them, 3, Blade(), atX: 30f, atZ: 24f);
            MakeArmyWithSpell(_us, unit, new TargetSelector(
                12f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: true));

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, unit);

            Assert.That(actions.Any(a => a.Intent == EMacroIntent.MoveToCast), Is.False,
                "a unit with no spell tokens is not a caster");
        }

        [Test]
        public void DeliverCargo_LoadedTransport_RoutesTowardAnObjective()
        {
            MakeObjective(new Position(50f, 24f));
            var transport = MakeUnit(_us, 1, Rifle(), atX: 15f, atZ: 24f);
            transport.GetValue().AttachRuleDefinition(new ResolvedRule("Transport",
                CoreRuleCatalog.Transport, new RuleArgument[] { new RuleArgument.Int(6) }));
            var cargo = MakeUnit(_us, 3, Blade(), atX: 15f, atZ: 24f);
            cargo.GetValue().Tokens.AddToken(new Token(
                TokenType.EmbarkedIn, 1, new TokenClearTrigger.ManualOnly(),
                OwnerUnitID: transport.GetValue().ID));

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, transport);
            MacroAction? deliver = actions.FirstOrDefault(a => a.Intent == EMacroIntent.DeliverCargo);

            Assert.That(deliver, Is.Not.Null, "a loaded transport gets a delivery intent");
            Assert.That(Distance(deliver!.ProjectedCentroid, new Position(50f, 24f)),
                Is.LessThan(Distance(new Position(15f, 24f), new Position(50f, 24f)) - 6f));
        }

        [Test]
        public void DeliverCargo_NotGenerated_ForEmptyTransport()
        {
            MakeObjective(new Position(50f, 24f));
            var transport = MakeUnit(_us, 1, Rifle(), atX: 15f, atZ: 24f);
            transport.GetValue().AttachRuleDefinition(new ResolvedRule("Transport",
                CoreRuleCatalog.Transport, new RuleArgument[] { new RuleArgument.Int(6) }));

            List<MacroAction> actions = MacroActionGenerator.Enumerate(_evaluator, _tableState, transport);

            Assert.That(actions.Any(a => a.Intent == EMacroIntent.DeliverCargo), Is.False,
                "an empty transport has no cargo plan to serve");
        }

        private void MakeArmyWithSpell(PlayerID owner, DataBinding<UnitData> unit,
            TargetSelector target)
        {
            var army = new ArmyData(owner, new List<DataBinding<UnitData>> { unit });
            army.SetSpells(new List<RuntimeSpell>
            {
                new RuntimeSpell(new SpellDefinition("Test Bolt", 2, target,
                    new Effect.DealHits(3, Array.Empty<string>())),
                    Array.Empty<ResolvedRule>()),
            });
            _store.Create(army);
        }

        // --- fixtures ---------------------------------------------------------------------------------

        private static Weapon Rifle(int ap = 0, float range = 24f) =>
            new Weapon("Rifle", rangeInches: range, attacks: 1, armorPenetration: ap);

        private static Weapon Blade() =>
            new Weapon("Blade", rangeInches: 0f, attacks: 2, armorPenetration: 0);

        private static List<Weapon> RifleAndBlade() => new() { Rifle(), Blade() };

        private void MakeObjective(Position position) =>
            _store.Create(new ObjectiveData(position, _store));

        // A single-model unit on a rectangular base (#361: the Hive Lord class of wedge repro).
        private DataBinding<UnitData> MakeRectBaseUnit(PlayerID owner, float widthInches,
            float heightInches, Weapon weapon, float atX, float atZ, int woundsPerModel = 1)
        {
            var model = new ModelData(new RectangleBase(widthInches, heightInches),
                new List<Weapon> { weapon }, new Position(atX, atZ), _store);
            if (woundsPerModel > 1) model.SetMaxWounds(woundsPerModel);
            var binding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(owner, "RectBase", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { binding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private DataBinding<UnitData> MakeUnit(PlayerID owner, int modelCount, object weapons,
            float atX, float atZ, int woundsPerModel = 1)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                List<Weapon> loadout = weapons switch
                {
                    Weapon single => new List<Weapon> { single },
                    List<Weapon> list => new List<Weapon>(list),
                    _ => new List<Weapon>(),
                };
                var model = new ModelData(
                    baseRadiusInches: 0.5f,
                    weapons: loadout,
                    initialPosition: new Position(atX + (i % 2) * 1.1f, atZ + (i / 2) * 1.1f),
                    gameDataStore: _store);
                if (woundsPerModel > 1) model.SetMaxWounds(woundsPerModel);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, "TestUnit", quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private static Position Centroid(DataBinding<UnitData> unit)
        {
            var alive = unit.GetValue().Models.Where(m => m.GetIsAlive()).ToList();
            return new Position(alive.Average(m => m.Position.x), alive.Average(m => m.Position.z));
        }

        private static float Distance(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
