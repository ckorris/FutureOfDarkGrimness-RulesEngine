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
    // #100 #2 slice 2d — the AI's pre-attack policy. The AI doesn't yet reason about buffs/marks, so it
    // skips the pre-attack menu (picks Done), which keeps it from firing abilities on arbitrary targets and
    // means it never issues a pre-attack target request. (A real "buff self / mark nearest" policy is a
    // future refinement.) The #066 AI contract is "always produce a legal answer" — Done is always offered.
    [TestFixture]
    public class AiPreAttackPolicyTests
    {
        [Test]
        public async Task AiStringSelection_SkipsPreAttackAbilities()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var resolver = new AiStringSelectionResolver(new TableState(store), new PlayerID(Guid.NewGuid()));

            var request = new StringSelectionRequest(new PlayerID(Guid.NewGuid()), "Use a pre-attack ability?",
                new List<string> { "Furious Buff", "Mend", PreAttackStage.DONE_CHOICE },
                new List<StringSelectionRequest.InvalidOption>());

            string choice = await resolver.Resolve(request);

            Assert.That(choice, Is.EqualTo(PreAttackStage.DONE_CHOICE),
                "the AI declines pre-attack abilities rather than firing the first one blindly");
        }
    }
}
