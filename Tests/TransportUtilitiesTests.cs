using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using NUnit.Framework;

namespace FDG.Tests
{
    // Foundation unit tests for the Transport(X) core rule (#035 slice A). These are authored TDD-first,
    // against a deliberately-stubbed TransportUtilities, so they run RED until the implementation pass.
    // They pin the decisions recorded in WorkItems/035-transport.md:
    //   - capacity is the Transport rule's Arg(0); space cost is 1 per standard model, 1 per Hero (within
    //     the Tough(6) cap), 3 per non-Hero Tough model;
    //   - ride eligibility caps Heroes at Tough(6), non-Heroes at Tough(3);
    //   - occupancy is TOKEN-derived (a cross-unit EmbarkedIn token owned by the transport) — the
    //     transport stores no list, and embarked units stay OFF the table (models at origin) so the
    //     targeting/activation exclusions fall out of the existing GetIsOnBattlefield() filter;
    //   - GetEffectivePosition resolves an embarked unit's location to its transport's (the opt-in seam).
    [TestFixture]
    public class TransportUtilitiesTests
    {
        private GameDataStore _store = null!;

        // Transport capability is now answered by the rule graph rather than by a rule-identity test, so
        // these need a live evaluator. No resolver: the Transport rule is statically attached, and only
        // granted-rule read-back needs one.
        private RuleEvaluator _evaluator = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _evaluator = new RuleEvaluator(new FixedDiceRoller(4));
        }

        // --- Identity & capacity -----------------------------------------------------------------

        [Test]
        public void IsTransport_True_WhenUnitCarriesTransportRule()
        {
            UnitData transport = MakeTransport(NewPlayer(), capacity: 6);
            Assert.That(TransportUtilities.IsTransport(transport, _evaluator), Is.True);
        }

        [Test]
        public void IsTransport_False_ForPlainUnit()
        {
            UnitData plain = MakeUnit(NewPlayer(), modelCount: 5);
            Assert.That(TransportUtilities.IsTransport(plain, _evaluator), Is.False);
        }

        [Test]
        public void GetCapacity_ReturnsTransportRuleArgZero()
        {
            UnitData transport = MakeTransport(NewPlayer(), capacity: 11);
            Assert.That(TransportUtilities.GetCapacity(transport, _evaluator), Is.EqualTo(11));
        }

        [Test]
        public void GetCapacity_Zero_ForNonTransport()
        {
            UnitData plain = MakeUnit(NewPlayer(), modelCount: 3);
            Assert.That(TransportUtilities.GetCapacity(plain, _evaluator), Is.EqualTo(0));
        }

        // --- Space cost --------------------------------------------------------------------------

        [Test]
        public void GetModelSpaceCost_StandardModel_IsOne()
        {
            Assert.That(TransportUtilities.GetModelSpaceCost(MakeModel(tough: 1), isHero: false), Is.EqualTo(1));
        }

        [Test]
        public void GetModelSpaceCost_NonHeroToughTwoModel_IsThree()
        {
            Assert.That(TransportUtilities.GetModelSpaceCost(MakeModel(tough: 2), isHero: false), Is.EqualTo(3));
        }

        [Test]
        public void GetModelSpaceCost_NonHeroToughThreeModel_IsThree()
        {
            Assert.That(TransportUtilities.GetModelSpaceCost(MakeModel(tough: 3), isHero: false), Is.EqualTo(3));
        }

        // Per the Transport(X) rule's worked example, a Hero within its Tough(6) ride cap occupies 1 space
        // regardless of Tough — a Tough(3) or Tough(6) Hero is 1, not 3.
        [Test]
        public void GetModelSpaceCost_HeroToughThree_IsOne()
        {
            Assert.That(TransportUtilities.GetModelSpaceCost(MakeModel(tough: 3), isHero: true), Is.EqualTo(1));
        }

        [Test]
        public void GetModelSpaceCost_HeroToughSix_IsOne()
        {
            Assert.That(TransportUtilities.GetModelSpaceCost(MakeModel(tough: 6), isHero: true), Is.EqualTo(1));
        }

        [Test]
        public void GetUnitSpaceCost_SumsLivingModels()
        {
            UnitData unit = MakeUnit(NewPlayer(), modelCount: 5); // five standard models
            Assert.That(TransportUtilities.GetUnitSpaceCost(unit), Is.EqualTo(5));
        }

        [Test]
        public void GetUnitSpaceCost_ToughModels_CountThreeEach()
        {
            UnitData unit = MakeUnit(NewPlayer(), modelCount: 2, tough: 3); // two Tough(3) models
            Assert.That(TransportUtilities.GetUnitSpaceCost(unit), Is.EqualTo(6));
        }

        [Test]
        public void GetUnitSpaceCost_IgnoresDeadModels()
        {
            UnitData unit = MakeUnit(NewPlayer(), modelCount: 3); // cost 3 alive
            unit.Models[0].DealWounds(1f);                        // kill one standard model
            Assert.That(TransportUtilities.GetUnitSpaceCost(unit), Is.EqualTo(2));
        }

        // --- Ride eligibility (Tough caps) -------------------------------------------------------

        [Test]
        public void CanModelRide_StandardModel_True()
        {
            Assert.That(TransportUtilities.CanModelRide(MakeModel(tough: 1), isHero: false), Is.True);
        }

        [Test]
        public void CanModelRide_NonHero_WithinToughThree_True()
        {
            Assert.That(TransportUtilities.CanModelRide(MakeModel(tough: 3), isHero: false), Is.True);
        }

        [Test]
        public void CanModelRide_NonHero_OverToughThree_False()
        {
            Assert.That(TransportUtilities.CanModelRide(MakeModel(tough: 4), isHero: false), Is.False);
        }

        [Test]
        public void CanModelRide_Hero_WithinToughSix_True()
        {
            Assert.That(TransportUtilities.CanModelRide(MakeModel(tough: 6), isHero: true), Is.True);
        }

        [Test]
        public void CanModelRide_Hero_OverToughSix_False()
        {
            Assert.That(TransportUtilities.CanModelRide(MakeModel(tough: 7), isHero: true), Is.False);
        }

        // --- Unit embark eligibility (integrated) ------------------------------------------------

        [Test]
        public void CanUnitEmbark_True_WhenWithinCapacityAndSamePlayer()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            UnitData squad = MakeUnit(player, modelCount: 5); // cost 5 <= 6

            bool ok = TransportUtilities.CanUnitEmbark(squad, transport, All(transport, squad), _evaluator, out string reason);

            Assert.That(ok, Is.True);
            Assert.That(reason, Is.Empty);
        }

        [Test]
        public void CanUnitEmbark_False_WhenOverCapacity()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 4);
            UnitData squad = MakeUnit(player, modelCount: 5); // cost 5 > 4

            bool ok = TransportUtilities.CanUnitEmbark(squad, transport, All(transport, squad), _evaluator, out string reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Is.Not.Empty);
        }

        [Test]
        public void CanUnitEmbark_False_WhenDifferentPlayer()
        {
            UnitData transport = MakeTransport(NewPlayer(), capacity: 6);
            UnitData enemy = MakeUnit(NewPlayer(), modelCount: 2); // different player

            bool ok = TransportUtilities.CanUnitEmbark(enemy, transport, All(transport, enemy), _evaluator, out string reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Is.Not.Empty);
        }

        [Test]
        public void CanUnitEmbark_False_WhenTargetIsNotATransport()
        {
            PlayerID player = NewPlayer();
            UnitData notATransport = MakeUnit(player, modelCount: 3);
            UnitData squad = MakeUnit(player, modelCount: 2);

            bool ok = TransportUtilities.CanUnitEmbark(squad, notATransport, All(notATransport, squad), _evaluator, out string reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Is.Not.Empty);
        }

        [Test]
        public void CanUnitEmbark_False_WhenAlreadyEmbarked()
        {
            PlayerID player = NewPlayer();
            UnitData transportA = MakeTransport(player, capacity: 6);
            UnitData transportB = MakeTransport(player, capacity: 6);
            UnitData squad = MakeUnit(player, modelCount: 2);

            TransportUtilities.Embark(squad, transportA);

            bool ok = TransportUtilities.CanUnitEmbark(squad, transportB,
                All(transportA, transportB, squad), _evaluator, out string reason);

            Assert.That(ok, Is.False, "a unit already aboard one transport can't embark another.");
            Assert.That(reason, Is.Not.Empty);
        }

        [Test]
        public void CanUnitEmbark_AccountsForExistingOccupants()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            UnitData aboard = MakeUnit(player, modelCount: 1, tough: 3); // occupies 3 spaces
            TransportUtilities.Embark(aboard, transport);

            UnitData tooBig = MakeUnit(player, modelCount: 5);  // cost 5, only 3 free -> no
            UnitData fits = MakeUnit(player, modelCount: 3);    // cost 3, exactly 3 free -> yes

            Assert.That(
                TransportUtilities.CanUnitEmbark(tooBig, transport, All(transport, aboard, tooBig), _evaluator, out _),
                Is.False, "5 spaces don't fit in the 3 left after a Tough(3) passenger.");
            Assert.That(
                TransportUtilities.CanUnitEmbark(fits, transport, All(transport, aboard, fits), _evaluator, out _),
                Is.True, "3 spaces fit exactly in the 3 remaining.");
        }

        // --- Mixed-Tough units (#098) ------------------------------------------------------------
        // The cases above test CanModelRide with an explicit isHero flag. These exercise CanUnitEmbark's
        // internal per-model hero detection (IsHeroModel) on a unit that MIXES ride caps — a joined
        // Hero (cap 6) alongside non-Hero grunts (cap 3) — plus the summed 3-space accounting.

        [Test]
        public void CanUnitEmbark_MixedUnit_HeroWithinSix_GruntsWithinThree_Embarks()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 9);
            // Hero Tough(6) (1 space, the Hero rate) + two grunts Tough(3) (3 spaces each) = 7 spaces. The
            // hero rides only because it's detected as the Hero and gets the cap-6 allowance — a non-Hero
            // Tough(6) model would be rejected at the cap-3 limit.
            UnitData squad = MakeHeroJoinedUnit(player, heroTough: 6, gruntToughs: new[] { 3, 3 });

            bool ok = TransportUtilities.CanUnitEmbark(squad, transport, All(transport, squad), _evaluator, out string reason);

            Assert.That(ok, Is.True, reason);
            Assert.That(reason, Is.Empty);
        }

        [Test]
        public void CanUnitEmbark_MixedUnit_HeroOverSix_Rejected()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 12);
            // Hero Tough(7) is over the Hero cap even though there's plenty of room.
            UnitData squad = MakeHeroJoinedUnit(player, heroTough: 7, gruntToughs: new[] { 3 });

            bool ok = TransportUtilities.CanUnitEmbark(squad, transport, All(transport, squad), _evaluator, out string reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Is.Not.Empty);
        }

        [Test]
        public void CanUnitEmbark_MixedUnit_GruntOverThree_Rejected()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 12);
            // The Hero (Tough 6) is fine, but a grunt at Tough(4) exceeds the non-Hero cap of 3.
            UnitData squad = MakeHeroJoinedUnit(player, heroTough: 6, gruntToughs: new[] { 4 });

            bool ok = TransportUtilities.CanUnitEmbark(squad, transport, All(transport, squad), _evaluator, out string reason);

            Assert.That(ok, Is.False, "a single over-cap grunt blocks the whole unit, even with the hero within cap.");
            Assert.That(reason, Is.Not.Empty);
        }

        [Test]
        public void CanUnitEmbark_NonHeroToughSix_InPlainUnit_Rejected()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 12);
            // A multi-model unit with a Tough(6) model but no joined Hero: nobody gets the cap-6 allowance,
            // so the Tough(6) model is over the non-Hero cap of 3.
            UnitData squad = MakeUnit(player, modelCount: 1, tough: 6);
            squad.ModelBindings.Add(MakeModelBinding(tough: 1)); // make it multi-model (no solo-hero path)

            bool ok = TransportUtilities.CanUnitEmbark(squad, transport, All(transport, squad), _evaluator, out string reason);

            Assert.That(ok, Is.False, "a Tough(6) non-Hero must not borrow the Hero ride cap.");
            Assert.That(reason, Is.Not.Empty);
        }

        [Test]
        public void CanUnitEmbark_SoloHeroToughSix_Embarks()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 3);
            // A lone Hero unit (one model + the Hero rule, no HeroAttachment) takes the cap-6 allowance via
            // the single-model branch of IsHeroModel.
            UnitData heroUnit = MakeSoloHeroUnit(player, tough: 6);

            bool ok = TransportUtilities.CanUnitEmbark(heroUnit, transport, All(transport, heroUnit), _evaluator, out string reason);

            Assert.That(ok, Is.True, reason);
        }

        [Test]
        public void GetUnitSpaceCost_MixedUnit_SumsPerModelCosts()
        {
            PlayerID player = NewPlayer();
            // Hero Tough(6) → 1 space (the Hero rate), plus two standard Tough(1) grunts → 1 each = 3 total.
            UnitData squad = MakeHeroJoinedUnit(player, heroTough: 6, gruntToughs: new[] { 1, 1 });

            Assert.That(TransportUtilities.GetUnitSpaceCost(squad), Is.EqualTo(3));
        }

        // --- Occupancy (token-derived) -----------------------------------------------------------

        [Test]
        public void Embark_StampsCrossUnitEmbarkedInToken_OwnedByTransport()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            UnitData squad = MakeUnit(player, modelCount: 3);

            TransportUtilities.Embark(squad, transport);

            Token token = squad.Tokens.GetAllTokens(TokenType.EmbarkedIn).Single();
            Assert.That(token.OwnerUnitID, Is.EqualTo(transport.ID),
                "the EmbarkedIn token lives on the occupant, owned by the transport.");
        }

        [Test]
        public void IsEmbarked_ReflectsEmbarkState()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            UnitData squad = MakeUnit(player, modelCount: 3);

            Assert.That(TransportUtilities.IsEmbarked(squad), Is.False);
            TransportUtilities.Embark(squad, transport);
            Assert.That(TransportUtilities.IsEmbarked(squad), Is.True);
        }

        [Test]
        public void GetTransportId_ReturnsOwner_OrNullWhenNotEmbarked()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            UnitData squad = MakeUnit(player, modelCount: 3);

            Assert.That(TransportUtilities.GetTransportId(squad), Is.Null);
            TransportUtilities.Embark(squad, transport);
            Assert.That(TransportUtilities.GetTransportId(squad), Is.EqualTo(transport.ID));
        }

        [Test]
        public void GetOccupants_ReturnsEmbarkedUnits()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            UnitData squad = MakeUnit(player, modelCount: 3);
            TransportUtilities.Embark(squad, transport);

            Assert.That(TransportUtilities.GetOccupants(transport, All(transport, squad)).ToList(),
                Is.EquivalentTo(new IUnit[] { squad }));
        }

        [Test]
        public void GetOccupants_Empty_WhenNoneEmbarked()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            UnitData squad = MakeUnit(player, modelCount: 3);

            Assert.That(TransportUtilities.GetOccupants(transport, All(transport, squad)), Is.Empty);
        }

        [Test]
        public void GetOccupants_ExcludesUnitsAboardOtherTransports()
        {
            PlayerID player = NewPlayer();
            UnitData transportA = MakeTransport(player, capacity: 6);
            UnitData transportB = MakeTransport(player, capacity: 6);
            UnitData inA = MakeUnit(player, modelCount: 2);
            UnitData inB = MakeUnit(player, modelCount: 2);
            TransportUtilities.Embark(inA, transportA);
            TransportUtilities.Embark(inB, transportB);

            Assert.That(TransportUtilities.GetOccupants(transportA, All(transportA, transportB, inA, inB)).ToList(),
                Is.EquivalentTo(new IUnit[] { inA }), "transport A sees only its own passenger.");
        }

        [Test]
        public void MultipleUnits_CanShareOneTransport()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            UnitData squadA = MakeUnit(player, modelCount: 2);
            UnitData squadB = MakeUnit(player, modelCount: 3);
            TransportUtilities.Embark(squadA, transport);
            TransportUtilities.Embark(squadB, transport);

            List<IUnit> all = All(transport, squadA, squadB);
            Assert.That(TransportUtilities.GetOccupants(transport, all).ToList(),
                Is.EquivalentTo(new IUnit[] { squadA, squadB }));
            Assert.That(TransportUtilities.GetOccupiedSpaces(transport, all), Is.EqualTo(5));
        }

        [Test]
        public void GetRemainingCapacity_SubtractsOccupiedFromCapacity()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            UnitData squad = MakeUnit(player, modelCount: 2); // cost 2
            TransportUtilities.Embark(squad, transport);

            Assert.That(TransportUtilities.GetRemainingCapacity(transport, All(transport, squad), _evaluator), Is.EqualTo(4));
        }

        [Test]
        public void Disembark_ClearsToken_AndFreesCapacity()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            UnitData squad = MakeUnit(player, modelCount: 2);
            TransportUtilities.Embark(squad, transport);

            TransportUtilities.Disembark(squad);

            Assert.That(TransportUtilities.IsEmbarked(squad), Is.False);
            Assert.That(TransportUtilities.GetOccupants(transport, All(transport, squad)), Is.Empty);
            Assert.That(TransportUtilities.GetRemainingCapacity(transport, All(transport, squad), _evaluator), Is.EqualTo(6));
        }

        // --- Off-table representation ------------------------------------------------------------

        [Test]
        public void EmbarkedUnit_StaysOffBattlefield()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            UnitData squad = MakeUnit(player, modelCount: 3); // models at origin

            TransportUtilities.Embark(squad, transport);

            Assert.That(squad.GetIsOnBattlefield(), Is.False,
                "an embarked unit's models stay at origin, so it's excluded from targeting/activation for free.");
        }

        [Test]
        public void Embark_DoesNotMoveOccupantModels()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            Place(transport, 10f, 5f);
            UnitData squad = MakeUnit(player, modelCount: 2);

            TransportUtilities.Embark(squad, transport);

            foreach (IModel model in squad.Models)
            {
                Assert.That(model.Position.x, Is.EqualTo(0f));
                Assert.That(model.Position.z, Is.EqualTo(0f));
            }
        }

        [Test]
        public void PlacedTransport_IsOnBattlefield()
        {
            UnitData transport = MakeTransport(NewPlayer(), capacity: 6);
            Place(transport, 10f, 5f);
            Assert.That(transport.GetIsOnBattlefield(), Is.True);
        }

        // --- Effective position (opt-in seam; build deferred to first consumer) ------------------

        [Test]
        public void GetEffectivePosition_ReturnsOwnPosition_WhenNotEmbarked()
        {
            UnitData unit = MakeUnit(NewPlayer(), modelCount: 1);
            Place(unit, 10f, 5f);

            Position? pos = TransportUtilities.GetEffectivePosition(unit, All(unit));

            Assert.That(pos.HasValue, Is.True);
            Assert.That(pos!.Value.x, Is.EqualTo(10f));
            Assert.That(pos.Value.z, Is.EqualTo(5f));
        }

        [Test]
        public void GetEffectivePosition_ReturnsTransportPosition_WhenEmbarked()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            Place(transport, 10f, 5f);
            UnitData squad = MakeUnit(player, modelCount: 2); // at origin
            TransportUtilities.Embark(squad, transport);

            Position? pos = TransportUtilities.GetEffectivePosition(squad, All(transport, squad));

            Assert.That(pos.HasValue, Is.True);
            Assert.That(pos!.Value.x, Is.EqualTo(10f), "an embarked unit's effective position is its transport's.");
            Assert.That(pos.Value.z, Is.EqualTo(5f));
        }

        [Test]
        public void GetEffectivePosition_Null_WhenEmbarkedTransportNotInSet()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            UnitData squad = MakeUnit(player, modelCount: 2);
            TransportUtilities.Embark(squad, transport);

            // The transport is deliberately omitted from the unit set.
            Position? pos = TransportUtilities.GetEffectivePosition(squad, All(squad));

            Assert.That(pos.HasValue, Is.False);
        }

        // --- Destruction spillout --------------------------------------------------------------
        // Slice E is mid-combat and interactive (interrupt resolution + ask the owner to place each
        // occupant within 6" of the wreck) — that orchestration is covered by a stage integration test
        // when the slice is built. The DETERMINISTIC consequences below are unit-testable now: who spills,
        // the within-6" placement constraint, and the per-occupant effects (un-embark + Shaken + a
        // dangerous-terrain test that wounds on a 1).

        [Test]
        public void GetOccupants_StillReportsPassengers_AfterTransportDies()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6); // single Tough(1) model
            Place(transport, 10f, 5f);
            UnitData squad = MakeUnit(player, modelCount: 3);
            TransportUtilities.Embark(squad, transport);

            transport.Models[0].DealWounds(1f); // destroy the transport's last model

            Assert.That(transport.GetIsDead(), Is.True);
            Assert.That(TransportUtilities.GetOccupants(transport, All(transport, squad)).ToList(),
                Is.EquivalentTo(new IUnit[] { squad }),
                "mid-combat spillout must still find the passengers when the transport is destroyed.");
        }

        [Test]
        public void IsWithinTransportRange_True_WithinSixInches()
        {
            Assert.That(
                TransportUtilities.IsWithinTransportRange(new Position(4f, 0f), new Position(0f, 0f)),
                Is.True);
        }

        [Test]
        public void IsWithinTransportRange_False_BeyondSixInches()
        {
            Assert.That(
                TransportUtilities.IsWithinTransportRange(new Position(8f, 0f), new Position(0f, 0f)),
                Is.False);
        }

        [Test]
        public void Spillout_ClearsEmbarkedState()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            UnitData squad = MakeUnit(player, modelCount: 3, tough: 2);
            TransportUtilities.Embark(squad, transport);

            TransportUtilities.ApplySpilloutEffects(squad, new FixedFaceDiceRoller(4)); // safe roll

            Assert.That(TransportUtilities.IsEmbarked(squad), Is.False,
                "a spilled-out unit is no longer aboard the destroyed transport.");
        }

        [Test]
        public void Spillout_MakesOccupantShaken()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            UnitData squad = MakeUnit(player, modelCount: 3, tough: 2);
            TransportUtilities.Embark(squad, transport);

            TransportUtilities.ApplySpilloutEffects(squad, new FixedFaceDiceRoller(4));

            Assert.That(squad.Tokens.HasToken(TokenType.Shaken), Is.True);
        }

        [Test]
        public void Spillout_DangerousTest_DealsOneWoundPerModel_OnRollOfOne()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            UnitData squad = MakeUnit(player, modelCount: 3, tough: 2); // 6 wounds total
            TransportUtilities.Embark(squad, transport);

            TransportUtilities.ApplySpilloutEffects(squad, new FixedFaceDiceRoller(1)); // every model rolls a 1

            Assert.That(squad.RemainingWounds, Is.EqualTo(3f),
                "each of the three models takes one dangerous-terrain wound.");
        }

        [Test]
        public void Spillout_DangerousTest_NoWound_OnSafeRoll()
        {
            PlayerID player = NewPlayer();
            UnitData transport = MakeTransport(player, capacity: 6);
            UnitData squad = MakeUnit(player, modelCount: 3, tough: 2); // 6 wounds total
            TransportUtilities.Embark(squad, transport);

            TransportUtilities.ApplySpilloutEffects(squad, new FixedFaceDiceRoller(4)); // no 1s

            Assert.That(squad.RemainingWounds, Is.EqualTo(6f), "a safe dangerous-terrain roll deals no wounds.");
        }

        // --- helpers -----------------------------------------------------------------------------

        private static PlayerID NewPlayer() => new PlayerID(Guid.NewGuid());

        private static List<IUnit> All(params IUnit[] units) => units.ToList();

        private UnitData MakeUnit(PlayerID player, int modelCount, int tough = 1)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                modelBindings.Add(MakeModelBinding(tough));
            }

            return new UnitData(player, "unit", quality: 4, defense: 4, modelBindings: modelBindings);
        }

        private UnitData MakeTransport(PlayerID player, int capacity)
        {
            UnitData transport = MakeUnit(player, modelCount: 1);
            transport.AttachRuleDefinition(new ResolvedRule(
                TransportUtilities.TransportRuleName, CoreRuleCatalog.Transport,
                new RuleArgument[] { new RuleArgument.Int(capacity) }));
            return transport;
        }

        // A unit of non-Hero grunts with a Hero model joined in (#006 AttachHero), mirroring the real
        // mixed-Tough composition: the joined hero rides under the cap-6 allowance, the grunts under cap-3.
        private UnitData MakeHeroJoinedUnit(PlayerID player, int heroTough, int[] gruntToughs)
        {
            List<DataBinding<ModelData>> grunts = gruntToughs.Select(MakeModelBinding).ToList();
            UnitData unit = new UnitData(player, "squad", quality: 4, defense: 4, modelBindings: grunts);

            DataBinding<ModelData> hero = MakeModelBinding(heroTough);
            HeroAttachment attachment = new HeroAttachment(hero.GetValue().ID, quality: 3, defense: 3, heroWounds: heroTough);
            unit.AttachHero(attachment, new List<DataBinding<ModelData>> { hero });
            return unit;
        }

        // A lone Hero: one model carrying the Hero rule, no HeroAttachment (the single-model IsHeroModel branch).
        private UnitData MakeSoloHeroUnit(PlayerID player, int tough)
        {
            UnitData unit = MakeUnit(player, modelCount: 1, tough: tough);
            unit.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero, Array.Empty<RuleArgument>()));
            return unit;
        }

        private ModelData MakeModel(int tough)
        {
            var model = new ModelData(baseRadiusInches: 0.75f, weapons: new List<Weapon>(),
                initialPosition: new Position(0, 0), gameDataStore: _store);
            model.SetMaxWounds(tough);
            return model;
        }

        private DataBinding<ModelData> MakeModelBinding(int tough)
        {
            ModelData model = MakeModel(tough);
            return _store.GetDataBinding<ModelData>(_store.Create(model));
        }

        private static void Place(IUnit unit, float x, float z) =>
            unit.Models[0].SetPosition(new Position(x, z));
    }
}
