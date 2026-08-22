using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #100 #14b — attacker-bonus markers (the Tag/Target/Spotter family): tokens placed on an enemy
    // that make friendly attacks against it better. Owner-ruled 2026-07-22: the spend is PROMPTED
    // (the attacking player picks how many markers to remove), not auto-spent. Three pieces:
    //   - Effect.GrantTokenOnRoll (Spotter's "on a 4+ place a marker", decisive die);
    //   - TargetMarkerSpend (persistent count + prompted spendable claim);
    //   - the hit/save stages folding the net with their own sign conventions.
    [TestFixture]
    public class TargetBonusMarkerTests
    {
        // --- GrantTokenOnRoll ---

        [Test]
        public async Task GrantTokenOnRoll_AtOrAboveThreshold_PlacesTheToken()
        {
            (TestGameContext ctx, IUnit target) = Setup(dieFace: 4);

            await Execute(ctx, target, minRoll: 4);

            Assert.That(target.Tokens.GetTokenCount(TokenType.SpendableHitBonus), Is.EqualTo(1));
        }

        [Test]
        public async Task GrantTokenOnRoll_BelowThreshold_PlacesNothing()
        {
            (TestGameContext ctx, IUnit target) = Setup(dieFace: 3);

            await Execute(ctx, target, minRoll: 4);

            Assert.That(target.Tokens.HasToken(TokenType.SpendableHitBonus), Is.False,
                "A 3 on a 4+ placement roll must place no marker.");
        }

        // --- GrantTokenOnRoll: the failure arm (#376 Reckless Piercing) ---

        [Test]
        public async Task GrantTokenOnRoll_OnAMiss_AppliesTheFailureArm_ToTheSameUnit()
        {
            (TestGameContext ctx, IUnit target) = Setup(dieFace: 1);
            var exposed = new TokenType("BackfireMarker");

            await ExecuteWithFailureArm(ctx, target, minRoll: 2, exposed);

            Assert.That(target.Tokens.HasToken(TokenType.SpendableHitBonus), Is.False,
                "a 1 on a 2+ gamble places no boon.");
            Assert.That(target.Tokens.GetTokenCount(exposed), Is.EqualTo(1),
                "the SAME die's failure arm lands the backfire marker instead.");
        }

        [Test]
        public async Task GrantTokenOnRoll_OnAPass_SkipsTheFailureArm()
        {
            (TestGameContext ctx, IUnit target) = Setup(dieFace: 2);
            var exposed = new TokenType("BackfireMarker");

            await ExecuteWithFailureArm(ctx, target, minRoll: 2, exposed);

            Assert.That(target.Tokens.GetTokenCount(TokenType.SpendableHitBonus), Is.EqualTo(1));
            Assert.That(target.Tokens.HasToken(exposed), Is.False,
                "one die, two exclusive outcomes - never both.");
        }

        private static Task ExecuteWithFailureArm(TestGameContext ctx, IUnit target, int minRoll,
            TokenType exposed)
        {
            var boon = new Token(TokenType.SpendableHitBonus, 1, new TokenClearTrigger.ManualOnly());
            var onFailure = new Effect.GrantToken(exposed, new ValueSource.Literal(1),
                new TokenClearTrigger.ManualOnly());
            return OperationExecutor.Execute(
                new[] { new RuleOperation.InvokeGrantTokenOnRoll(target, boon, minRoll, onFailure) },
                new GameOperationServices(ctx));
        }

        [Test]
        public async Task GrantTokenOnRoll_IsDecisive_UnderTheProbabilisticRoller_AndActuallyVaries()
        {
            // Same dice-invariant pin as ClearTokenOnRoll: binary outcome, so the roll must commit to
            // one face even under the probabilistic roller, and across seeds both outcomes must occur.
            var outcomes = new HashSet<int>();
            for (int seed = 0; seed < 40; seed++)
            {
                (TestGameContext ctx, IUnit target) = Setup(new ProbabilisticDiceRoller(seed));
                await Execute(ctx, target, minRoll: 4);

                int count = target.Tokens.GetTokenCount(TokenType.SpendableHitBonus);
                Assert.That(count, Is.EqualTo(0).Or.EqualTo(1),
                    "The marker is placed or it is not; a fraction means the roll was not decisive.");
                outcomes.Add(count);
            }

            Assert.That(outcomes, Is.EquivalentTo(new[] { 0, 1 }),
                "Across 40 seeds a 4+ placement must sometimes pass and sometimes fail.");
        }

        // --- TargetMarkerSpend: persistent markers ---

        [Test]
        public async Task PersistentMarkers_AreCounted_NeverPrompted_NeverRemoved()
        {
            (TestGameContext ctx, IUnit defender) = Setup(dieFace: 4,
                requester: new ThrowingRequester());
            defender.Tokens.AddToken(new Token(TokenType.PersistentHitBonus, 2,
                new TokenClearTrigger.ManualOnly()));

            int net = await TargetMarkerSpend.ConsumeNet(ctx, NewPlayer(), defender, ERollKind.Hit);

            Assert.That(net, Is.EqualTo(2));
            Assert.That(defender.Tokens.GetTokenCount(TokenType.PersistentHitBonus), Is.EqualTo(2),
                "Target-family markers persist; only the spendable kind is ever removed.");
        }

        [Test]
        public async Task NoMarkers_NoPromptAndZeroNet()
        {
            (TestGameContext ctx, IUnit defender) = Setup(dieFace: 4,
                requester: new ThrowingRequester());

            Assert.That(await TargetMarkerSpend.ConsumeNet(ctx, NewPlayer(), defender, ERollKind.Hit),
                Is.EqualTo(0));
        }

        // --- TargetMarkerSpend: prompted spend ---

        [Test]
        public async Task SpendableMarkers_PromptTheAttacker_SpendAllListedFirst()
        {
            var requester = new ScriptedSelectionRequester(pickIndex: 0);
            (TestGameContext ctx, IUnit defender) = Setup(dieFace: 4, requester: requester);
            defender.Tokens.AddToken(new Token(TokenType.SpendableHitBonus, 3,
                new TokenClearTrigger.ManualOnly()));

            int net = await TargetMarkerSpend.ConsumeNet(ctx, NewPlayer(), defender, ERollKind.Hit);

            Assert.That(net, Is.EqualTo(3), "The first option must be spend-all (the automated default).");
            Assert.That(defender.Tokens.HasToken(TokenType.SpendableHitBonus), Is.False);
            Assert.That(requester.SeenOptions, Has.Count.EqualTo(4),
                "3 markers offer four choices: spend 3, 2, 1, or 0.");
        }

        [Test]
        public async Task SpendableMarkers_PartialSpend_RemovesOnlyTheChosenCount()
        {
            // Options are descending (3, 2, 1, 0) — index 2 picks "spend 1".
            var requester = new ScriptedSelectionRequester(pickIndex: 2);
            (TestGameContext ctx, IUnit defender) = Setup(dieFace: 4, requester: requester);
            defender.Tokens.AddToken(new Token(TokenType.SpendableApBonus, 3,
                new TokenClearTrigger.ManualOnly()));

            int net = await TargetMarkerSpend.ConsumeNet(ctx, NewPlayer(), defender, ERollKind.Save);

            Assert.That(net, Is.EqualTo(1));
            Assert.That(defender.Tokens.GetTokenCount(TokenType.SpendableApBonus), Is.EqualTo(2),
                "Choosing to spend 1 of 3 markers must leave the other 2 for a later attack.");
        }

        [Test]
        public async Task SpendZero_LeavesMarkersAndAddsNothing()
        {
            var requester = new ScriptedSelectionRequester(pickIndex: 2); // options: 2, 1, 0
            (TestGameContext ctx, IUnit defender) = Setup(dieFace: 4, requester: requester);
            defender.Tokens.AddToken(new Token(TokenType.SpendableHitBonus, 2,
                new TokenClearTrigger.ManualOnly()));

            int net = await TargetMarkerSpend.ConsumeNet(ctx, NewPlayer(), defender, ERollKind.Hit);

            Assert.That(net, Is.EqualTo(0));
            Assert.That(defender.Tokens.GetTokenCount(TokenType.SpendableHitBonus), Is.EqualTo(2));
        }

        [Test]
        public async Task MixedMarkers_PersistentAndSpent_BothCount()
        {
            var requester = new ScriptedSelectionRequester(pickIndex: 0); // spend all (1)
            (TestGameContext ctx, IUnit defender) = Setup(dieFace: 4, requester: requester);
            defender.Tokens.AddToken(new Token(TokenType.PersistentHitBonus, 2,
                new TokenClearTrigger.ManualOnly()));
            defender.Tokens.AddToken(new Token(TokenType.SpendableHitBonus, 1,
                new TokenClearTrigger.ManualOnly()));

            int net = await TargetMarkerSpend.ConsumeNet(ctx, NewPlayer(), defender, ERollKind.Hit);

            Assert.That(net, Is.EqualTo(3), "2 persistent + 1 spent = +3.");
            Assert.That(defender.Tokens.GetTokenCount(TokenType.PersistentHitBonus), Is.EqualTo(2));
            Assert.That(defender.Tokens.HasToken(TokenType.SpendableHitBonus), Is.False);
        }

        // --- Stage integration: the fold is CONSUMED (the #196 lesson) ---

        [Test]
        public async Task HitStage_FoldsDefenderMarkers_IntoTheThreshold()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new TestGameContext(store, new FixedDiceRoller(4),
                playerRequester: new ScriptedSelectionRequester(pickIndex: 0));

            DataBinding<UnitData> attacker = MakeUnit(store, "A", 1);
            DataBinding<UnitData> defender = MakeUnit(store, "D", 1);
            defender.GetValue().Tokens.AddToken(new Token(TokenType.PersistentHitBonus, 1,
                new TokenClearTrigger.ManualOnly()));
            defender.GetValue().Tokens.AddToken(new Token(TokenType.SpendableHitBonus, 1,
                new TokenClearTrigger.ManualOnly()));

            DetermineHitRollResults result = await RunHitStage(ctx, attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(2),
                "Quality 4 with +1 persistent and +1 spent marker must need 2s to hit.");
            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.SpendableHitBonus), Is.False);
            Assert.That(defender.GetValue().Tokens.GetTokenCount(TokenType.PersistentHitBonus),
                Is.EqualTo(1));
        }

        [Test]
        public async Task SaveStage_RaisesTheDefendersThreshold_ByTheMarkerNet()
        {
            int withMarkers = await RunSaveStage(markerCount: 2);
            int without = await RunSaveStage(markerCount: 0);

            Assert.That(withMarkers, Is.EqualTo(without + 2),
                "Two persistent AP markers must raise the save threshold by exactly 2.");
        }

        // --- Helpers ---

        private static PlayerID NewPlayer() => new(Guid.NewGuid());

        private static (TestGameContext, IUnit) Setup(int dieFace,
            IPlayerRequestByID? requester = null) => Setup(new FixedDiceRoller(dieFace), requester);

        private static (TestGameContext, IUnit) Setup(IDiceRoller roller,
            IPlayerRequestByID? requester = null)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new TestGameContext(store, roller, playerRequester: requester);
            return (ctx, MakeUnit(store, "Target", 1).GetValue());
        }

        private static Task Execute(TestGameContext ctx, IUnit target, int minRoll)
        {
            var token = new Token(TokenType.SpendableHitBonus, 1, new TokenClearTrigger.ManualOnly());
            return OperationExecutor.Execute(
                new[] { new RuleOperation.InvokeGrantTokenOnRoll(target, token, minRoll) },
                new GameOperationServices(ctx));
        }

        private static async Task<DetermineHitRollResults> RunHitStage(TestGameContext ctx,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var stage = new DetermineHitRollStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: false, isCharging: false);
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True,
                "stage must store a DetermineHitRollResults in metadata.");
            return result;
        }

        private static async Task<int> RunSaveStage(int markerCount)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var ctx = new TestGameContext(store, new FixedDiceRoller(4),
                playerRequester: new ThrowingRequester());

            DataBinding<UnitData> attacker = MakeUnit(store, "A", 1);
            DataBinding<UnitData> defender = MakeUnit(store, "D", 1);
            if (markerCount > 0)
            {
                defender.GetValue().Tokens.AddToken(new Token(TokenType.PersistentApBonus, markerCount,
                    new TokenClearTrigger.ManualOnly()));
            }

            var stage = new DetermineSaveRollsNeededStage<ICombatMetadata>(ctx,
                new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: false, isCharging: false);
            var hits = new RollToHitResults(
                new List<SuccessfulHitInfo> { new(new DiceResults(new float[6]), 0) },
                new List<FailedHitInfo>());
            metadata.AddResult(hits);
            metadata.AddResult(new CoverCheckResults(0)); // no cover
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineSaveRollNeededResults result), Is.True);
            return result.PendingSaveRollsList.Single().SaveNeeded;
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, string name, int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                    initialPosition: new Position(0, 0), gameDataStore: store);
                modelBindings.Add(store.GetDataBinding<ModelData>(store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: modelBindings);
            return store.GetDataBinding<UnitData>(store.Create(unit));
        }

        /// <summary> Fails the test if any prompt is sent — for paths that must not prompt. </summary>
        private sealed class ThrowingRequester : IPlayerRequestByID
        {
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
                => throw new InvalidOperationException(
                    "No prompt expected on this path, but one was sent: " + request.GetType().Name);
        }

        /// <summary> Answers any StringSelectionRequest with the option at a fixed index. </summary>
        private sealed class ScriptedSelectionRequester : IPlayerRequestByID
        {
            private readonly int _pickIndex;
            public List<string> SeenOptions { get; } = new();

            public ScriptedSelectionRequester(int pickIndex) => _pickIndex = pickIndex;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                var selection = (StringSelectionRequest)(object)request;
                SeenOptions.Clear();
                SeenOptions.AddRange(selection.ValidOptions);
                return Task.FromResult((TReply)(object)selection.ValidOptions[_pickIndex]);
            }
        }
    }
}
