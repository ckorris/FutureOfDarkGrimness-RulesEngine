using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #011 — ValidateMovingThroughEnemyUnits: a move may not pass through or stack on an enemy base, and a
    // non-charging model must end at least ENEMY_STANDOFF_DISTANCE_INCHES (base-to-base) from enemies.
    // Ending in/near base contact counts as charging and waives the standoff for that whole enemy unit.
    // All models here have radius 0.75", so base contact is at 1.5" centre-to-centre.
    [TestFixture]
    public class MoveThroughEnemyValidationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        [Test]
        public void PathPassesThroughEnemyBase_Rejected()
        {
            //Straight line from (0,0) to (10,0) runs over an enemy sitting at (5,0).
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(10, 0) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughEnemyUnit), Is.True);
        }

        [Test]
        public void PathSkirtsEnemyBase_Accepted()
        {
            //Travels along z=5, well clear of the enemy down at (5,0).
            DataBinding<ModelData> model = MakeModel(new Position(0, 5));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(10, 5) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        // #206 — a non-charge move MAY now end inside the standoff band (right up against an enemy). The move
        // validator no longer rejects it; the forced-charge obligation moves to ChooseActionStage.GetCanPass
        // (see ChooseActionPassDisableTests). Pass-through and ending-stacked are still enforced (below).
        [Test]
        public void NonChargeEndsInsideStandoff_Accepted()
        {
            //Ends at (2.6,0): 0.9" base-to-base from the enemy — inside the old 1" standoff, not stacked.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(2.6f, 0) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void ChargeEndsInBaseContact_Accepted()
        {
            //Ends at (3.5,0): exactly base-to-base with the enemy at (5,0). Legal charge into contact.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(3.5f, 0) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void ChargeOvershootsIntoOverlap_Rejected()
        {
            //Ends at (4.5,0): only 0.5" centre-to-centre — bases overlapping. Stacking is illegal even when charging.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(4.5f, 0) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughEnemyUnit), Is.True);
        }

        // #206 — with the standoff-band rejection gone, a charge that ends in contact with enemy A while landing
        // 0.8" from A's unit-mate B is simply legal (it was already legal via the old waiver; now there is no
        // band to waive). Kept as a regression guard that ending near several models of one enemy unit is fine.
        [Test]
        public void ChargeEndingNearSeveralModelsOfOneEnemyUnit_Accepted()
        {
            //Ends in contact with enemy A at (5,0); enemy B at (5.8,0) is part of the SAME unit and ends up
            //0.8" base-to-base away.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(3.5f, 0) });

            var enemyUnit = new List<EnemyModelFootprint>
            {
                new EnemyModelFootprint(new Position(5f, 0), 0.75f, unitKey: 0),
                new EnemyModelFootprint(new Position(5.8f, 0), 0.75f, unitKey: 0),
            };

            bool ok = MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxRushDistance: 12f, maxDistanceInches: 12f, enemyUnit, terrain: null, out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void AlreadyInsideStandoff_MovingAway_Accepted()
        {
            //Starts inside the standoff (0.5" base-to-base) and retreats. A move that doesn't close the gap
            //must not be trapped by the standoff rule.
            DataBinding<ModelData> model = MakeModel(new Position(3, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(1, 0) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void NoEnemyFootprints_NotRejected()
        {
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(5, 0) });

            bool ok = MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxRushDistance: 12f, maxDistanceInches: 12f,
                new List<EnemyModelFootprint>(), terrain: null, out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        // --- "Already in base contact" edge cases (float robustness). A model touching an enemy must not
        // be reported as having no legal move: it can always retreat, slide along, or hold. Contact is at
        // 1.5" centre-to-centre for two 0.75" bases; a charge may legally land up to 0.1" overlapped. ---

        [Test]
        public void StartsInBaseContact_MovesStraightAway_Accepted()
        {
            DataBinding<ModelData> model = MakeModel(new Position(3.5f, 0)); // touching the enemy at (5,0)
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(1f, 0) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void StartsInBaseContact_HoldsPosition_Accepted()
        {
            DataBinding<ModelData> model = MakeModel(new Position(3.5f, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(3.5f, 0) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void StartsInBaseContact_SlidesAlongStayingInContact_Accepted()
        {
            //Repositions sideways while still touching the enemy (pile-in-style shuffle).
            DataBinding<ModelData> model = MakeModel(new Position(3.5f, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(3.5f, 0.5f) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void StartsSlightlyOverlapping_MovesAway_Accepted()
        {
            //Worst legal post-charge state: 0.1" of base overlap (gap = -0.1). Retreating must still be legal
            //despite the start point sitting on the wrong side of the contact threshold.
            DataBinding<ModelData> model = MakeModel(new Position(3.6f, 0)); // 1.4" centre-to-centre = -0.1" gap
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(1f, 0) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void StartsInBaseContact_PushesDeeperIntoEnemy_Rejected()
        {
            //Negative control: a model in contact may not bulldoze further INTO the enemy base.
            DataBinding<ModelData> model = MakeModel(new Position(3.5f, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(4.2f, 0) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.False);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughEnemyUnit), Is.True);
        }

        // #029 — an Aircraft (uncontactable) can't be charged: a move that closes into base contact with it is
        // rejected, where the same move against a normal enemy is a legal charge.
        [Test]
        public void Uncontactable_CannotBeCharged_EndingInContactRejected()
        {
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(3.5f, 0) }); // ends in contact with (5,0)

            bool ok = Validate(move, Aircraft(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.False, "an Aircraft can't be charged — closing into contact with it is rejected.");
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.EndedTooCloseToEnemy), Is.True);
        }

        [Test]
        public void ContactableEnemy_SameContactMove_IsALegalCharge()
        {
            // Control: the identical move against a normal (contactable) enemy is a legal charge.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(3.5f, 0) });

            bool ok = Validate(move, Enemy(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        [Test]
        public void Uncontactable_MayBePassedUnder_NotEndingNear()
        {
            // A unit may move PAST/under an Aircraft (no through-block) as long as it doesn't end within 1" of it.
            DataBinding<ModelData> model = MakeModel(new Position(0, 0));
            ModelMoveEntry move = new ModelMoveEntry(model, new List<Position> { new Position(10, 0) }); // passes (5,0), ends far

            bool ok = Validate(move, Aircraft(new Position(5, 0)), out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        // #206 — a wide rectangular footprint ending inside the old standoff band (its flat 0.5" from an enemy)
        // is now accepted, exactly like the circular case: the standoff-band rejection is gone for contactable
        // enemies. (#150's shape-aware geometry still governs pass-through and ending-stacked — see the swept
        // test below.)
        [Test]
        public void RectangularMover_WideFootprintEndsInFormerStandoffBand_Accepted()
        {
            // A 6"×1" base ending at (5,0): its 3" half-width reaches X=8, leaving a 0.5" gap to an enemy circle
            // at (9.25,0) — inside the old 1" standoff band, but not stacked and not passed through.
            DataBinding<ModelData> rectModel = MakeModel(new RectangleBase(6f, 1f), new Position(0, 0));
            bool rectOk = Validate(new ModelMoveEntry(rectModel, new List<Position> { new Position(5f, 0) }),
                Enemy(new Position(9.25f, 0)), out var rectErrors);

            Assert.That(rectOk, Is.True, Why(rectErrors));

            // The circular case (which never tripped the band anyway) is likewise accepted.
            DataBinding<ModelData> circleModel = MakeModel(new CircleBase(0.5f), new Position(0, 0));
            bool circleOk = Validate(new ModelMoveEntry(circleModel, new List<Position> { new Position(5f, 0) }),
                Enemy(new Position(9.25f, 0)), out var circleErrors);

            Assert.That(circleOk, Is.True, Why(circleErrors));
        }

        [Test]
        public void RectangularMover_SweptFootprintClipsEnemy_FlaggedWhereBoundingCircleWouldPassThrough()
        {
            // A 6"×1" base travels straight up X=0 from (0,0) to (0,10), clearing the enemy at both endpoints
            // (start and end are far up/down the Z axis). But its 3" half-width sweeps a 6"-wide corridor, so it
            // clips the enemy circle at (2.5,0)… placed at (2.5,5) mid-path. The shape-aware swept pass-through
            // catches this (#150 "4c"); the model neither starts nor ends in contact, so only the mid-path
            // crossing can flag it.
            DataBinding<ModelData> rectModel = MakeModel(new RectangleBase(6f, 1f), new Position(0, 0));
            bool rectOk = Validate(new ModelMoveEntry(rectModel, new List<Position> { new Position(0f, 10f) }),
                Enemy(new Position(2.5f, 5f)), out var rectErrors);

            Assert.That(rectOk, Is.False, "the 6\"-wide swept footprint crosses the enemy mid-path.");
            Assert.That(rectErrors.Any(e => e.ErrorReasonType == EErrorReasonType.MovingThroughEnemyUnit), Is.True);

            // Control: a circle of the rectangle's inscribed radius (0.5") stays 2.5" off the X=0 line, so the
            // old bounding-disc pass-through would let this identical move through untouched.
            DataBinding<ModelData> circleModel = MakeModel(new CircleBase(0.5f), new Position(0, 0));
            bool circleOk = Validate(new ModelMoveEntry(circleModel, new List<Position> { new Position(0f, 10f) }),
                Enemy(new Position(2.5f, 5f)), out _);

            Assert.That(circleOk, Is.True, "a bounding-circle approximation misses the enemy entirely.");
        }

        [Test]
        public void StackedInsideLargeEnemyBase_Holds_Accepted()
        {
            // #159: a melee (pile-in) can leave a small model stacked concentric INSIDE a large enemy base.
            // On its next activation the model just holds. SurfaceGap2D used to report a contained circle as a
            // positive (clear) gap, so the move-through pass-through guard thought the model started/ended clear
            // and the swept-zone test then false-positived "moves through an enemy unit" on a zero-length move.
            // Holding (or any non-closer move) from an already-overlapping position must be legal.
            DataBinding<ModelData> model = MakeModel(new Position(0f, 0f)); // radius 0.75
            ModelMoveEntry hold = new ModelMoveEntry(model, new List<Position> { new Position(0f, 0f) });

            var bigEnemy = new List<EnemyModelFootprint>
            {
                new EnemyModelFootprint(new Position(0f, 0f), baseRadiusInches: 2.5f, unitKey: 0,
                    uncontactable: false, baseShape: new RectangleBase(5f, 5f), facing: new Float2(0f, 1f)),
            };

            bool ok = MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { hold },
                maxRushDistance: 12f, maxDistanceInches: 12f, bigEnemy, terrain: null, out var errors);

            Assert.That(ok, Is.True, Why(errors));
        }

        // #340: a leg's ROTATION is not validated - the base turns from the node it left to the node it is
        // arriving at somewhere along the way - so a leg is "through" an enemy only when its swept footprint
        // crosses at BOTH endpoint attitudes. #312 swept every leg at its ARRIVING attitude alone, which
        // applied a turn belonging to the node being placed to the ground the model set off from.
        [Test]
        public void SweptFootprintClipsEnemyOnlyAtTheArrivingAttitude_Accepted()
        {
            // The same 6"x1" base and the same enemy at (2.5,5) as the test above, but the model RESTS facing
            // +X: its 6" width then runs along Z, giving a 1"-wide corridor up the X=0 line that misses the
            // enemy entirely. Only the +Z attitude it turns to on arrival sweeps the 6"-wide corridor that
            // clips it - and the model neither starts nor ends in contact, so nothing else objects.
            DataBinding<ModelData> model = MakeModel(new RectangleBase(6f, 1f), new Position(0f, 0f));
            model.GetValue().SetFacing(new Float2(1f, 0f));

            var waypoints = new List<Position> { new Position(0f, 10f) };
            var move = new ModelMoveEntry(model, waypoints, MovementFacingUtilities.WaypointFacings(
                model.GetValue().Position, waypoints, model.GetValue().Facing, 0f));

            bool ok = Validate(move, Enemy(new Position(2.5f, 5f)), out var errors);

            Assert.That(ok, Is.True, "the leg is clear at the attitude the model departed with. " + Why(errors));
        }

        private static List<EnemyModelFootprint> Enemy(Position center)
            => new List<EnemyModelFootprint> { new EnemyModelFootprint(center, 0.75f, unitKey: 0) };

        private static List<EnemyModelFootprint> Aircraft(Position center)
            => new List<EnemyModelFootprint> { new EnemyModelFootprint(center, 0.75f, unitKey: 0, uncontactable: true) };

        private static bool Validate(ModelMoveEntry move, List<EnemyModelFootprint> enemies,
            out List<ReasonForInvalidMove> errors)
            => MovementUtilities.ValidatePaths(new List<ModelMoveEntry> { move },
                maxRushDistance: 12f, maxDistanceInches: 12f, enemies, terrain: null, out errors);

        private static string Why(List<ReasonForInvalidMove> errors)
            => "Unexpected errors: " + string.Join(", ", errors.Select(e => e.ErrorReasonType.ToString()));

        private DataBinding<ModelData> MakeModel(IBaseShape shape, Position initialPosition)
        {
            ModelData modelData = new ModelData(shape, new List<Weapon>(), initialPosition, _store);
            DataReference reference = _store.Create(modelData);
            return _store.GetDataBinding<ModelData>(reference);
        }

        private DataBinding<ModelData> MakeModel(Position initialPosition)
        {
            ModelData modelData = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: initialPosition,
                gameDataStore: _store);
            DataReference reference = _store.Create(modelData);
            return _store.GetDataBinding<ModelData>(reference);
        }
    }
}
