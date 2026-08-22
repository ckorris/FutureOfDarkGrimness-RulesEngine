using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #376 Bloodthirsty Fighter: "for each unmodified roll of 1 that enemies roll when blocking hits
    // from this model's weapons in melee, this model may roll +1 attack with that weapon. This rule
    // doesn't apply to newly generated attacks." Owner ruling: the follow-up is a REAL bonus batch -
    // the full hit -> save -> wound chain with the same weapon (ResolveBonusMeleeAttacksStage inside
    // the melee swing chain) - and "may" is auto-taken (extra attacks are never harmful).
    //
    // Three layers: the effect's histogram read, AssignWoundsStage's posting (and its refusal for a
    // bonus batch - the no-chaining guard), and the full swing chain driven through the real
    // StrikeBackStage, where a scripted dice sequence proves the earned batch rolls exactly once.
    [TestFixture]
    public class BloodthirstyFighterRuleIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        // The shipped def's shape: melee-gated AddBonusAttack at save-complete, Actor seat.
        private static SpecialRuleDefinition Probe() => new("Bloodthirsty Probe",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                    new Condition.IsMelee(),
                    new Effect.AddBonusAttack(OnRollValue: 1, Count: 1),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>());

        // ---- Layer 1: the effect reads the block histogram ------------------------------------------

        [Test]
        public void Effect_EarnsOneAttackPerNaturalBlockOne_InMeleeOnly()
        {
            var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            DataBinding<UnitData> attacker = MakeUnit(1, Blade());
            DataBinding<UnitData> defender = MakeUnit(3, null);
            attacker.GetValue().AttachRuleDefinition(new ResolvedRule("Bloodthirsty Probe", Probe()));

            var sink = new BonusAttackSink();
            sink.ApplyFrom(evaluator.EvaluateAll(
                new SaveRollCompleteContext(attacker.GetValue(), defender.GetValue(),
                    Faces(1, 1, 3), IsMelee: true),
                RuleParticipant.Actor(attacker.GetValue())));
            Assert.That(sink.TotalBonusAttacks, Is.EqualTo(2f), "two natural 1s = two follow-ups.");

            sink = new BonusAttackSink();
            sink.ApplyFrom(evaluator.EvaluateAll(
                new SaveRollCompleteContext(attacker.GetValue(), defender.GetValue(),
                    Faces(1, 1, 3), IsMelee: false),
                RuleParticipant.Actor(attacker.GetValue())));
            Assert.That(sink.TotalBonusAttacks, Is.EqualTo(0f), "shooting blocks earn nothing.");
        }

        // ---- Layer 3: the full swing, through the real strike-back chain ----------------------------

        // The gold path. The striker-back has 2 attacks and the probe rule; the script forces: hit
        // roll all 6s (2 hits) -> block roll all 1s (2 wounds AND 2 earned follow-ups) -> bonus hit
        // roll all 6s (2 hits) -> bonus block roll all 1s (2 more wounds, whose natural 1s must earn
        // NOTHING). A fifth roll would throw (the script is exhausted) - the no-chaining guard is
        // proven by termination, and the wound total by the defender's ledger.
        [Test]
        public async Task StrikeBackSwing_RollsTheEarnedBatchOnce_AndNeverChains()
        {
            var requester = new MeleeRequester();
            var roller = new ScriptedFaceRoller(6, 1, 6, 1);
            var ctx = new WoundTestContext(_store, requester, roller);
            DataBinding<UnitData> charger = MakeUnit(10, Blade());   // absorbs 4 wounds
            DataBinding<UnitData> charged = MakeUnit(1, Blade(attacks: 2));
            charged.GetValue().AttachRuleDefinition(new ResolvedRule("Bloodthirsty Probe", Probe()));

            var melee = new CombatActionContext(ctx, charger, isMelee: true, isCharging: true);
            melee.SetDefender(charged);
            melee.SetInRangeAttackers(charger.ModelBindings());
            melee.SetInRangeDefenders(charged.ModelBindings());

            var strikeBack = new StrikeBackStage(ctx, new NoOpLayer<ICombatActionContext>());
            strikeBack.FinishedStrikingBack.Bind("done");
            strikeBack.OnAttackerKilled.Bind("killed");
            await strikeBack.Enter(melee);

            Assert.That(charger.RemainingWounds(), Is.EqualTo(6f),
                "2 base wounds + 2 follow-up wounds = 4 of 10; more means the bonus chained, fewer " +
                "means the earned batch never rolled.");
            Assert.That(roller.RollsMade, Is.EqualTo(4),
                "hit, block, bonus hit, bonus block - and nothing after the bonus block's own 1s.");
            Assert.That(requester.WoundRequests.Select(r => r.TotalWoundsToAssign),
                Is.EqualTo(new[] { 2f, 2f }), "two separate batches of 2 wounds each.");
        }

        // Without the rule the same script never gets past roll 2 - the control for the test above.
        [Test]
        public async Task WithoutTheRule_TheSameBlocksEarnNothing()
        {
            var requester = new MeleeRequester();
            var roller = new ScriptedFaceRoller(6, 1);
            var ctx = new WoundTestContext(_store, requester, roller);
            DataBinding<UnitData> charger = MakeUnit(10, Blade());
            DataBinding<UnitData> charged = MakeUnit(1, Blade(attacks: 2)); // no probe rule

            var melee = new CombatActionContext(ctx, charger, isMelee: true, isCharging: true);
            melee.SetDefender(charged);
            melee.SetInRangeAttackers(charger.ModelBindings());
            melee.SetInRangeDefenders(charged.ModelBindings());

            var strikeBack = new StrikeBackStage(ctx, new NoOpLayer<ICombatActionContext>());
            strikeBack.FinishedStrikingBack.Bind("done");
            strikeBack.OnAttackerKilled.Bind("killed");
            await strikeBack.Enter(melee);

            Assert.That(charger.RemainingWounds(), Is.EqualTo(8f), "just the 2 base wounds.");
            Assert.That(roller.RollsMade, Is.EqualTo(2));
        }

        // The dead-defender guard: the base batch wipes the target, so the earned follow-ups lapse
        // (nothing to attack) and the chain terminates without a bonus hit roll.
        [Test]
        public async Task FollowUpsLapse_WhenTheBaseBatchKillsTheDefender()
        {
            var requester = new MeleeRequester();
            var roller = new ScriptedFaceRoller(6, 1);
            var ctx = new WoundTestContext(_store, requester, roller);
            DataBinding<UnitData> charger = MakeUnit(2, Blade());    // dies to 2 wounds
            DataBinding<UnitData> charged = MakeUnit(1, Blade(attacks: 2));
            charged.GetValue().AttachRuleDefinition(new ResolvedRule("Bloodthirsty Probe", Probe()));

            var melee = new CombatActionContext(ctx, charger, isMelee: true, isCharging: true);
            melee.SetDefender(charged);
            melee.SetInRangeAttackers(charger.ModelBindings());
            melee.SetInRangeDefenders(charged.ModelBindings());

            var strikeBack = new StrikeBackStage(ctx, new NoOpLayer<ICombatActionContext>());
            strikeBack.FinishedStrikingBack.Bind("done");
            strikeBack.OnAttackerKilled.Bind("killed");
            await strikeBack.Enter(melee);

            Assert.That(charger.RemainingWounds(), Is.EqualTo(0f));
            Assert.That(roller.RollsMade, Is.EqualTo(2),
                "the earned follow-ups lapse against a destroyed target - no bonus hit roll.");
        }

        // ---- Helpers --------------------------------------------------------------------------------

        private static DiceResults Faces(params int[] faces)
        {
            var perSide = new float[6];
            foreach (int face in faces) perSide[face - 1] += 1f;
            return new DiceResults(perSide);
        }

        private static Weapon Blade(int attacks = 1) =>
            new Weapon("Blade", rangeInches: 0f, attacks: attacks, armorPenetration: 0);

        private DataBinding<UnitData> MakeUnit(int modelCount, Weapon? weapon)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.75f,
                    weapons: weapon == null
                        ? new List<Weapon>()
                        : new List<Weapon> { new Weapon(weapon.Name, weapon.RangeInches, weapon.Attacks,
                            weapon.ArmorPenetration) },
                    initialPosition: new Position(0, 0),
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Unit", quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        /// <summary>Answers the strike-back chain's menus; records wound assignments in order.</summary>
        private sealed class MeleeRequester : IPlayerRequestByID
        {
            public List<AssignWoundsRequest> WoundRequests { get; } = new List<AssignWoundsRequest>();

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case AssignWoundsRequest woundRequest:
                        WoundRequests.Add(woundRequest);
                        var result = new AssignWoundsResults(woundRequest.UnitReceivingWounds,
                            woundRequest.TotalWoundsToAssign);
                        result.AutoFill();
                        return Task.FromResult((TReply)(object)result);
                    case StringSelectionRequest menu:
                        return Task.FromResult((TReply)(object)menu.ValidOptions[0]);
                    case YesNoRequest:
                        return Task.FromResult((TReply)(object)false);
                    default:
                        throw new InvalidOperationException("Unexpected request: " + request.GetType());
                }
            }
        }

        /// <summary>One scripted face per ROLL CALL (all dice of that call land on it); throws when the
        /// script is exhausted, so an unexpected extra roll fails loudly instead of silently looping.</summary>
        private sealed class ScriptedFaceRoller : IDiceRoller
        {
            private readonly int[] _faces;
            public int RollsMade { get; private set; }

            public ScriptedFaceRoller(params int[] faces) => _faces = faces;

            public IDiceResults Roll(int sideCount, float rollCount)
            {
                if (RollsMade >= _faces.Length)
                    throw new InvalidOperationException(
                        $"Roll #{RollsMade + 1} was not scripted - an unexpected extra dice roll.");
                float[] perSide = new float[sideCount];
                perSide[_faces[RollsMade++] - 1] = rollCount;
                return new DiceResults(perSide);
            }
        }
    }
}
