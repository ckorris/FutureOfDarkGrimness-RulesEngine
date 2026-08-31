using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #381 (AoF Retreating Strike): the move-end strike. Proves the
    // ability is offered at Movement_OnMoveResolved only for a unit that fought melee this round and is
    // not Shaken (both plain data conditions on the def - the shape AofRuleSupplement.json authors), that
    // its once-per-round cost is a RoundEnd marker, that the dice count is carriers x X, and that both
    // trigger arms - the unit's own move action (RetreatingStrikeMoveStage) and a recorded post-combat
    // move (RetreatingStrikePostCombatStage) - roll the pool and land the 6+ successes as DIRECT wounds
    // through the real AssignWounds stage, skipping the save. Also pins the owner ruling's exclusions:
    // a zero-length move and a melee with no post-combat move stay dark.
    [TestFixture]
    public class RetreatingStrikeRuleIntegrationTests
    {
        private static readonly TokenType UsedMarker = new("AbilityUsed:Retreating Strike");

        // The def the AoF supplement authors as JSON, mirrored in C# (the CrossingAttack test pattern):
        // an activated DealAutoWounds ability at the move-resolved hook, gated on having been in melee
        // this round and on not being Shaken, once per round, one enemy within 3", X dice per carrier.
        private static SpecialRuleDefinition RetreatingStrikeDef { get; } = new SpecialRuleDefinition(
            "Retreating Strike",
            System.Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Movement_OnMoveResolved, new Cost.OncePerRound(),
                    new TargetSelector(3f, 1, 1, ETargetAffinity.Foe, false),
                    new Effect.DealAutoWounds(new ValueSource.Arg(0), SuccessThreshold: 6),
                    new Condition.And(
                        new Condition.TokenPresent(TokenType.WasInMeleeThisRound),
                        new Condition.Not(new Condition.TokenPresent(TokenType.Shaken)))),
            },
            Valence: EValence.Positive,
            Description: "Once per round, when this unit ends a move within 3\" of enemy units after " +
                "being in melee, pick one of them and roll dice - each 6+ deals one unsaveable wound.");

        private GameDataStore _store = null!;
        private PlayerID _mover;
        private PlayerID _foe;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _mover = new PlayerID(System.Guid.NewGuid());
            _foe = new PlayerID(System.Guid.NewGuid());
        }

        // The was-in-melee gate: the same unit, the same hook - no offer before the melee fact is
        // stamped, one offer after. This is the data half of the owner ruling ("after being in melee"
        // is a round-scoped token, not a trigger wired per melee).
        [Test]
        public void Dispatch_OfferedOnlyAfterMeleeThisRound()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> mover = MakeStriker("Corsairs", strikeX: 1, new Position(0f, 0f));

            Assert.That(ctx.RuleEvaluator.GatherOffers(new MoveResolvedContext(mover.GetValue())),
                Is.Empty, "no melee this round - no strike offer");

            StampWasInMelee(mover);

            Assert.That(ctx.RuleEvaluator.GatherOffers(new MoveResolvedContext(mover.GetValue())).Count,
                Is.EqualTo(1), "after fighting melee this round the strike is offered at move-end");
        }

        // Shaken blocks the strike (an Active Special Rule, per the ruling) - authored as data
        // (Not(TokenPresent(Shaken))), so it must bite at the offer.
        [Test]
        public void Dispatch_ShakenBlocksTheOffer()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> mover = MakeStriker("Corsairs", strikeX: 1, new Position(0f, 0f));
            StampWasInMelee(mover);
            mover.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));

            Assert.That(ctx.RuleEvaluator.GatherOffers(new MoveResolvedContext(mover.GetValue())),
                Is.Empty, "a Shaken unit cannot use Retreating Strike");
        }

        // Once per round: the cost marker rides a RoundEnd clear (NEVER ActivationEnd - the post-combat
        // arm fires during the ENEMY's activation, and the end-of-activation sweep only visits the
        // activated unit), and a paid marker suppresses the next offer.
        [Test]
        public void Dispatch_OncePerRound_MarkerSuppressesSecondOffer()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> mover = MakeStriker("Corsairs", strikeX: 1, new Position(0f, 0f));
            DataBinding<UnitData> enemy = MakeEnemy("Grunts", new Position(2f, 0f));
            StampWasInMelee(mover);

            IReadOnlyList<AbilityOffer> offers = ctx.RuleEvaluator.GatherOffers(
                new MoveResolvedContext(mover.GetValue()));
            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.ResolveAbility(offers[0],
                new[] { (IUnit)enemy.GetValue() });

            RuleOperation.GrantTokenToUnit marker = ops.OfType<RuleOperation.GrantTokenToUnit>()
                .Single(op => op.TokenToGrant.Type == UsedMarker);
            Assert.That(marker.TokenToGrant.ClearTrigger, Is.InstanceOf<TokenClearTrigger.RoundEnd>(),
                "the once-per-round marker clears at round end, not activation end");

            OperationApplier.ApplyTokenOperations(ops);
            Assert.That(ctx.RuleEvaluator.GatherOffers(new MoveResolvedContext(mover.GetValue())),
                Is.Empty, "the strike is spent for the round");
        }

        // Dice count is carriers x X: the rule sits on the UNIT, so every living model rolls its X.
        // The book data carries X=1 on multi-model units ("one die per model") and X=3 on the
        // single-model beasts ("three dice") - both shapes fall out of the same arg.
        [Test]
        public void Dispatch_DiceCount_IsLivingModelsTimesArg()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> squad = MakeStriker("Dark Warriors", strikeX: 1,
                new Position(0f, 0f), new Position(1f, 0f), new Position(2f, 0f));
            DataBinding<UnitData> beast = MakeStriker("Hydra", strikeX: 3, new Position(20f, 0f));
            DataBinding<UnitData> enemy = MakeEnemy("Grunts", new Position(2f, 2f));
            StampWasInMelee(squad);
            StampWasInMelee(beast);

            IReadOnlyList<RuleOperation> squadOps = ctx.RuleEvaluator.ResolveAbility(
                ctx.RuleEvaluator.GatherOffers(new MoveResolvedContext(squad.GetValue()))[0],
                new[] { (IUnit)enemy.GetValue() });
            IReadOnlyList<RuleOperation> beastOps = ctx.RuleEvaluator.ResolveAbility(
                ctx.RuleEvaluator.GatherOffers(new MoveResolvedContext(beast.GetValue()))[0],
                new[] { (IUnit)enemy.GetValue() });

            Assert.That(squadOps.OfType<RuleOperation.InvokeDealAutoWounds>().Single().DiceCount,
                Is.EqualTo(3), "Retreating Strike(1) on a 3-model unit rolls 3 dice");
            Assert.That(beastOps.OfType<RuleOperation.InvokeDealAutoWounds>().Single().DiceCount,
                Is.EqualTo(3), "Retreating Strike(3) on a single-model unit rolls 3 dice");
        }

        // End-to-end through the move-action arm: every die a 6, so Retreating Strike(1) on one model
        // deals 1 wound. The wound takes NO save - a defense-4 model saving on a rolled 6 would block it
        // if a save were rolled, so the wound landing proves the save stage is skipped.
        [Test]
        public async Task MoveArm_Accept_DealsUnsaveableWound_AndSpendsTheRound()
        {
            var requester = new StrafeRequester(accept: true);
            var ctx = new WoundTestContext(_store, requester, new AllSixesDiceRoller());
            DataBinding<UnitData> mover = MakeStriker("Corsairs", strikeX: 1, new Position(0f, 0f));
            MakeEnemy("Grunts", new Position(2f, 0f), new Position(2f, 1f));
            StampWasInMelee(mover);

            await RunMoveArm(ctx, mover, new Position(1f, 0f));

            Assert.That(requester.WoundRequest, Is.Not.Null, "the accepted strike lands as a wound assignment");
            Assert.That(requester.WoundRequest!.TotalWoundsToAssign, Is.EqualTo(1f),
                "one carrier at X=1 rolls one 6 = 1 wound; a rolled-6 armor save would block it if one " +
                "ran, so the wound landing proves the save is skipped");
            Assert.That(mover.GetValue().Tokens.HasToken(UsedMarker), Is.True,
                "using the strike spends the once-per-round marker");
        }

        // Cancelling the target pick declines the ability entirely: no roll, no cost.
        [Test]
        public async Task MoveArm_Decline_NoWoundsNoCost()
        {
            var requester = new StrafeRequester(accept: false);
            var ctx = new WoundTestContext(_store, requester, new AllSixesDiceRoller());
            DataBinding<UnitData> mover = MakeStriker("Corsairs", strikeX: 1, new Position(0f, 0f));
            MakeEnemy("Grunts", new Position(2f, 0f));
            StampWasInMelee(mover);

            await RunMoveArm(ctx, mover, new Position(1f, 0f));

            Assert.That(requester.SelectionAsked, Is.True, "the optional strike is offered as a pick");
            Assert.That(requester.WoundRequest, Is.Null, "declining resolves no attack");
            Assert.That(mover.GetValue().Tokens.HasToken(UsedMarker), Is.False, "declining spends nothing");
        }

        // #333 doctrine: a submitted-but-zero-length path is not a move, so "ends its move" never
        // happens and the hook stays dark - no prompt at all.
        [Test]
        public async Task MoveArm_ZeroLengthMove_NoOffer()
        {
            var requester = new StrafeRequester(accept: true);
            var ctx = new WoundTestContext(_store, requester, new AllSixesDiceRoller());
            DataBinding<UnitData> mover = MakeStriker("Corsairs", strikeX: 1, new Position(0f, 0f));
            MakeEnemy("Grunts", new Position(2f, 0f));
            StampWasInMelee(mover);

            await RunMoveArm(ctx, mover, new Position(0f, 0f));

            Assert.That(requester.SelectionAsked, Is.False, "a zero-length move is not a move-end");
            Assert.That(mover.GetValue().Tokens.HasToken(UsedMarker), Is.False);
        }

        // No enemy within the ability's 3" - nothing to strike, no prompt, budget kept.
        [Test]
        public async Task MoveArm_NoEnemyInRange_NoPrompt()
        {
            var requester = new StrafeRequester(accept: true);
            var ctx = new WoundTestContext(_store, requester, new AllSixesDiceRoller());
            DataBinding<UnitData> mover = MakeStriker("Corsairs", strikeX: 1, new Position(0f, 0f));
            MakeEnemy("Grunts", new Position(20f, 0f));
            StampWasInMelee(mover);

            await RunMoveArm(ctx, mover, new Position(1f, 0f));

            Assert.That(requester.SelectionAsked, Is.False, "no eligible target - no offer");
            Assert.That(mover.GetValue().Tokens.HasToken(UsedMarker), Is.False);
        }

        // The post-combat arm fires only off the recorded mover (the PostCombatMoveGate hand-off) and
        // consumes it, so the strike reads the REAL final positions of a Harassing-style move.
        [Test]
        public async Task PostCombatArm_FiresForRecordedMover_AndConsumesTheHandOff()
        {
            var requester = new StrafeRequester(accept: true);
            var ctx = new WoundTestContext(_store, requester, new AllSixesDiceRoller());
            DataBinding<UnitData> mover = MakeStriker("Corsairs", strikeX: 1, new Position(0f, 0f));
            MakeEnemy("Grunts", new Position(2f, 0f), new Position(2f, 1f));
            StampWasInMelee(mover);

            var combatContext = new CombatActionContext(ctx, mover, isMelee: true);
            combatContext.PostCombatMovers.Add(mover);

            var stage = new RetreatingStrikePostCombatStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnBatchDone.Bind("batch"); // a terminal here; the real graph loops it back to the stage
            stage.OnStrikeResolved.Bind("done");
            await stage.Enter(combatContext);

            Assert.That(requester.WoundRequest, Is.Not.Null, "the recorded post-combat mover strikes");
            Assert.That(requester.WoundRequest!.TotalWoundsToAssign, Is.EqualTo(1f));
            Assert.That(combatContext.PostCombatMovers, Is.Empty, "the hand-off is consumed on entry");
        }

        // #391: BOTH combatants Harassed (a mirror match) - each recorded mover gets its own strike,
        // one wound pipeline per pass (the OnBatchDone loop; here bound to a terminal and re-entered
        // manually, as the real graph's self-binding does).
        [Test]
        public async Task PostCombatArm_TwoMovers_EachStrikes()
        {
            var requester = new StrafeRequester(accept: true);
            var ctx = new WoundTestContext(_store, requester, new AllSixesDiceRoller());
            DataBinding<UnitData> first = MakeStriker("Corsairs", strikeX: 1, new Position(0f, 0f));
            DataBinding<UnitData> second = MakeStriker("Witches", strikeX: 1, new Position(0f, 1f));
            DataBinding<UnitData> enemy = MakeEnemy("Grunts", new Position(2f, 0f), new Position(2f, 1f),
                new Position(2f, 2f));
            StampWasInMelee(first);
            StampWasInMelee(second);

            var combatContext = new CombatActionContext(ctx, first, isMelee: true);
            combatContext.PostCombatMovers.Add(first);
            combatContext.PostCombatMovers.Add(second);

            var stage = new RetreatingStrikePostCombatStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnBatchDone.Bind("batch"); // a terminal here; the real graph loops it back to the stage
            stage.OnStrikeResolved.Bind("done");
            await stage.Enter(combatContext); // first mover's strike (ends at the batch terminal)
            await stage.Enter(combatContext); // the loop's re-entry: second mover's strike
            await stage.Enter(combatContext); // drained - exits through OnStrikeResolved

            Assert.That(enemy.RemainingWounds(), Is.EqualTo(1f),
                "each of the two movers dealt its own 1 unsaveable wound (3 - 2 = 1)");
            Assert.That(first.GetValue().Tokens.HasToken(UsedMarker), Is.True);
            Assert.That(second.GetValue().Tokens.HasToken(UsedMarker), Is.True);
            Assert.That(combatContext.PostCombatMovers, Is.Empty);
        }

        // The owner ruling's central exclusion: a melee where nothing repositioned (in particular the
        // charger's forced 1" move-back, which never reaches the gate) records no mover, so the
        // post-combat arm stays dark even with an eligible enemy in range.
        [Test]
        public async Task PostCombatArm_NoRecordedMove_NoOffer()
        {
            var requester = new StrafeRequester(accept: true);
            var ctx = new WoundTestContext(_store, requester, new AllSixesDiceRoller());
            DataBinding<UnitData> mover = MakeStriker("Corsairs", strikeX: 1, new Position(0f, 0f));
            MakeEnemy("Grunts", new Position(2f, 0f));
            StampWasInMelee(mover);

            var combatContext = new CombatActionContext(ctx, mover, isMelee: true);

            var stage = new RetreatingStrikePostCombatStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnBatchDone.Bind("batch");
            stage.OnStrikeResolved.Bind("done");
            await stage.Enter(combatContext);

            Assert.That(requester.SelectionAsked, Is.False,
                "no chosen post-combat move happened - the forced move-back is not a trigger");
        }

        // The gate now reports whether the unit actually repositioned - the fact the Post*Stages hand to
        // the strike stage. A real move returns true; a declined (zero-length) one returns false.
        [Test]
        public async Task Gate_ReportsReposition_MovedTrueDeclinedFalse()
        {
            var moved = new CannedMovePathRequester(dx: 2f, dz: 0f);
            var movedCtx = new TriggeredMoveTestContext(_store, moved);
            DataBinding<UnitData> harasser = MakeStriker("Corsairs", strikeX: 1, new Position(0f, 0f));
            harasser.GetValue().AttachRuleDefinition(new ResolvedRule("Harassing", CoreRuleCatalog.Harassing));
            IUnit u = harasser.GetValue();

            bool movedResult = await PostCombatMoveGate.OfferIfAvailable(movedCtx, u,
                movedCtx.RuleEvaluator.EvaluateAll(new PostMeleeActionContext(u), RuleParticipant.Actor(u)));
            Assert.That(movedResult, Is.True, "a real reposition reports true");

            // Same unit next round (clear the budget), declining this time.
            u.Tokens.RemoveTokens(TokenType.PostCombatMoveUsed);
            var declined = new CannedMovePathRequester(dx: 0f, dz: 0f);
            var declinedCtx = new TriggeredMoveTestContext(_store, declined);
            bool declinedResult = await PostCombatMoveGate.OfferIfAvailable(declinedCtx, u,
                declinedCtx.RuleEvaluator.EvaluateAll(new PostMeleeActionContext(u), RuleParticipant.Actor(u)));
            Assert.That(declinedResult, Is.False, "a declined (zero-length) move reports false");
        }

        // The hand-off itself: PostMeleeStage records the charged unit as PostCombatMover exactly when
        // the gate's Harassing move really repositioned it - the line RetreatingStrikePostCombatStage
        // keys off. A declined move records nothing.
        [Test]
        public async Task PostMeleeStage_RecordsMover_OnlyWhenTheGateMoved()
        {
            DataBinding<UnitData> charger = MakeEnemy("Chargers", new Position(0f, 0f));

            foreach ((float dx, bool expectRecorded) in new[] { (2f, true), (0f, false) })
            {
                var requester = new CannedMovePathRequester(dx: dx, dz: 0f);
                var ctx = new TriggeredMoveTestContext(_store, requester);
                DataBinding<UnitData> defender = MakeStriker($"Harassers{dx}", strikeX: 1,
                    new Position(dx + 1f, 40f));
                defender.GetValue().AttachRuleDefinition(
                    new ResolvedRule("Harassing", CoreRuleCatalog.Harassing));

                var combatContext = new CombatActionContext(ctx, charger, isMelee: true, isCharging: true);
                combatContext.SetDefender(defender);

                var stage = new PostMeleeStage(ctx, new NoOpLayer<ICombatActionContext>());
                stage.ToFinished.Bind("done");
                await stage.Enter(combatContext);

                if (expectRecorded)
                {
                    Assert.That(combatContext.PostCombatMovers, Is.EqualTo(new[] { defender }),
                        "a real Harassing reposition records the charged unit for the strike stage");
                }
                else
                {
                    Assert.That(combatContext.PostCombatMovers, Is.Empty,
                        "a declined (zero-length) Harassing move records nothing");
                }
            }
        }

        // The melee-end stamp: both combatants get the round-scoped was-in-melee fact - including a
        // defender that neither charged nor struck back (which Fatigued does NOT cover).
        [Test]
        public async Task ApplyFatigue_StampsWasInMeleeOnBothCombatants()
        {
            var ctx = new WoundTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> charger = MakeStriker("Corsairs", strikeX: 1, new Position(0f, 0f));
            DataBinding<UnitData> defender = MakeEnemy("Grunts", new Position(1f, 0f));

            var combatContext = new CombatActionContext(ctx, charger, isMelee: true, isCharging: true);
            combatContext.SetDefender(defender);

            var stage = new ApplyFatigueStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnFatigueApplied.Bind("done");
            await stage.Enter(combatContext);

            Assert.That(charger.GetValue().Tokens.HasToken(TokenType.WasInMeleeThisRound), Is.True,
                "the charger fought melee this round");
            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.WasInMeleeThisRound), Is.True,
                "the passive defender fought melee this round too, though it is not Fatigued");
            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.False,
                "a defender that never struck back is not Fatigued - the tokens are distinct facts");
        }

        private static async Task RunMoveArm(WoundTestContext ctx, DataBinding<UnitData> mover,
            Position destination)
        {
            var moveContext = new MovementActionContext(ctx, mover);
            moveContext.SubmitValidPathTemplate(new List<ModelMoveEntry>
            {
                new ModelMoveEntry(mover.GetValue().ModelBindings[0], new List<Position> { destination })
            });

            var stage = new RetreatingStrikeMoveStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnStrikeResolved.Bind("done");
            await stage.Enter(moveContext);
        }

        private static void StampWasInMelee(DataBinding<UnitData> unit)
        {
            unit.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.WasInMeleeThisRound));
        }

        private sealed class AllSixesDiceRoller : IDiceRoller
        {
            public IDiceResults Roll(int sideCount, float rollCount)
            {
                float[] perSide = new float[sideCount];
                perSide[sideCount - 1] = rollCount;
                return new DiceResults(perSide);
            }
        }

        private DataBinding<UnitData> MakeStriker(string name, int strikeX, params Position[] positions)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            foreach (Position pos in positions)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(_mover, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule("Retreating Strike",
                RetreatingStrikeDef, new RuleArgument[] { new RuleArgument.Int(strikeX) }));
            _store.Create(new ArmyData(_mover, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private DataBinding<UnitData> MakeEnemy(string name, params Position[] positions)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            foreach (Position pos in positions)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(_foe, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_foe, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
