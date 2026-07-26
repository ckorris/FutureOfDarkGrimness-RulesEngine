using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // "Before attacking" abilities now surface as named Choose Action menu options (like Cast), not as a
    // separate post-attack prompt. The AI doesn't yet reason about when a buff/mark is worth spending, so it
    // must not fire one blindly: given a menu that mixes ability names with a real action or Pass, it picks
    // the known option, never the ability. (A real "buff self / mark nearest" policy is a future refinement.)
    [TestFixture]
    public class AiBeforeAttackPolicyTests
    {
        [Test]
        public async Task ChooseAction_PrefersPassOverBeforeAttackAbilities()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var resolver = new AiStringSelectionResolver(new TableState(store), new PlayerID(Guid.NewGuid()));

            var request = new StringSelectionRequest(new PlayerID(Guid.NewGuid()), "Choose Action",
                new List<string> { "Regeneration Buff", "Precision Fighting Mark", ChooseActionStage.PASS_CHOICE_NAME },
                new List<StringSelectionRequest.InvalidOption>());

            string choice = await resolver.Resolve(request);

            Assert.That(choice, Is.EqualTo(ChooseActionStage.PASS_CHOICE_NAME),
                "the AI declines before-attack abilities rather than firing one blindly on an arbitrary target");
        }

        [Test]
        public async Task ChooseAction_PrefersARealActionOverBeforeAttackAbilities()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var resolver = new AiStringSelectionResolver(new TableState(store), new PlayerID(Guid.NewGuid()));

            var request = new StringSelectionRequest(new PlayerID(Guid.NewGuid()), "Choose Action",
                new List<string> { "Regeneration Buff", ChooseActionStage.CHARGE_CHOICE_NAME },
                new List<StringSelectionRequest.InvalidOption>());

            string choice = await resolver.Resolve(request);

            Assert.That(choice, Is.EqualTo(ChooseActionStage.CHARGE_CHOICE_NAME),
                "a real action always beats an unreasoned-about ability");
        }
    }
}
