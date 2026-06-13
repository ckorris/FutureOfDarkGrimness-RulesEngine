using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Network.Messages;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FDG.Tests
{
    // #059 workstream 5: a client sends its army (with #059 embedded rule definitions) to the host via
    // ArmyListUpdateMessage, which crosses the Newtonsoft TypeNameHandling.Auto message transport. The
    // STJ-only rule-definition graph does NOT round-trip through Newtonsoft directly (it chokes on the
    // computed RequiredCapabilities), so the army travels as an STJ-encoded string. These tests pin
    // that the message — and therefore the embedded rules — survives the actual transport.
    [TestFixture]
    public class ArmyListUpdateMessageTests
    {
        // Mirrors the network transport: MessageSerializer serializes with TypeNameHandling.Auto.
        private static readonly JsonSerializerSettings TransportSettings =
            new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };

        private static ArmyListFile ArmyWithEmbeddedRule()
        {
            SpecialRuleDefinition frenzied = new SpecialRuleDefinition("Frenzied",
                new List<HookEntry>
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.UnmodifiedRollEquals(6),
                        new Effect.AddExtraHit(OnRollValue: 6),
                        ELifetime.ThisAttack),
                },
                new List<ActivatedAbility>());

            return new ArmyListFile
            {
                Name = "Net Raiders",
                RuleDefinitions = new List<SpecialRuleDefinition> { frenzied },
            };
        }

        [Test]
        public void Message_RoundTripsThroughNewtonsoftTransport_PreservingEmbeddedRules()
        {
            ArmyListUpdateMessage sent = ArmyListUpdateMessage.FromArmy(new PlayerID(Guid.Empty), ArmyWithEmbeddedRule());

            // The army field is now an opaque string — Newtonsoft moves it with no rule graph to choke on.
            string wire = JsonConvert.SerializeObject(sent, TransportSettings);
            ArmyListUpdateMessage received =
                JsonConvert.DeserializeObject<ArmyListUpdateMessage>(wire, TransportSettings)!;

            ArmyListFile army = received.DecodeArmy();

            Assert.That(army.Name, Is.EqualTo("Net Raiders"));
            Assert.That(army.RuleDefinitions, Has.Count.EqualTo(1));

            SpecialRuleDefinition rule = army.RuleDefinitions.Single();
            Assert.That(rule.Name, Is.EqualTo("Frenzied"));
            Assert.That(rule.Passive, Has.Count.EqualTo(1));
            Assert.That(rule.Passive[0].Condition, Is.TypeOf<Condition.UnmodifiedRollEquals>());
            Assert.That(rule.Passive[0].Effect, Is.TypeOf<Effect.AddExtraHit>());
        }

        [Test]
        public void DecodeArmy_AfterFromArmy_RebuildsAnEquivalentArmy()
        {
            ArmyListFile original = ArmyWithEmbeddedRule();

            ArmyListFile decoded = ArmyListUpdateMessage.FromArmy(new PlayerID(Guid.Empty), original).DecodeArmy();

            // Structural equivalence via the canonical STJ form (records with collections aren't value-equal).
            Assert.That(JsonConvert.SerializeObject(decoded.RuleDefinitions.Select(r => r.Name)),
                Is.EqualTo(JsonConvert.SerializeObject(original.RuleDefinitions.Select(r => r.Name))));
            Assert.That(decoded.RuleDefinitions.Single().Passive, Has.Count.EqualTo(1));
        }
    }
}
