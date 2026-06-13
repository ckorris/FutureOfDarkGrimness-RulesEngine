using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FDG.Rules.Definitions;
using FDG.Rules.Serialization;
using NUnit.Framework;

namespace FDG.Tests
{
    // Step 5 reflection guard for the rule-definition JSON loader. The polymorphic vocabulary is
    // hand-maintained (~60 [JsonDerivedType] tags across six closed sum types), so it WILL drift — a new
    // subtype added without its tag, or a real field dropped by the RuleJson modifier. These two tests
    // turn both failure modes from "throws at runtime on some rarely-serialized rule" (as
    // TargetMajorityHasTough once would have) into a build-time failure. Reflection-driven, so they
    // auto-cover every base — present and future — with zero maintenance.
    [TestFixture]
    public class PolymorphicRuleRegistrationTests
    {
        // Every abstract record in the engine assembly that opts into STJ polymorphism.
        private static IReadOnlyList<Type> PolymorphicBases { get; } =
            typeof(SpecialRuleDefinition).Assembly.GetTypes()
                .Where(t => t.IsAbstract && t.GetCustomAttribute<JsonPolymorphicAttribute>(inherit: false) != null)
                .ToList();

        private static IEnumerable<Type> ConcreteSubtypesOf(Type baseType) =>
            baseType.Assembly.GetTypes().Where(t => !t.IsAbstract && t != baseType && baseType.IsAssignableFrom(t));

        [Test]
        public void EveryConcreteSubtypeOfAPolymorphicBaseHasADerivedTypeTag()
        {
            Assert.That(PolymorphicBases, Is.Not.Empty,
                "expected the rule sum types (ValueSource, Condition, Effect, ...) to carry [JsonPolymorphic].");

            List<string> missing = new();
            foreach (Type baseType in PolymorphicBases)
            {
                HashSet<Type> registered = baseType
                    .GetCustomAttributes<JsonDerivedTypeAttribute>()
                    .Select(a => a.DerivedType)
                    .ToHashSet();

                foreach (Type subtype in ConcreteSubtypesOf(baseType))
                {
                    if (!registered.Contains(subtype))
                    {
                        missing.Add($"{baseType.Name}.{subtype.Name} has no [JsonDerivedType] tag.");
                    }
                }
            }

            Assert.That(missing, Is.Empty, string.Join(Environment.NewLine, missing));
        }

        // Guards the DropComputedRuleProperties weakness: it drops every setter-less property, which is
        // correct only while every rule type is a record (state == constructor parameters). If a future
        // type's real data member is get-only and bound to a ctor, the modifier would silently drop it.
        // Assert instead that every constructor parameter survives as a serialized property.
        [Test]
        public void ModifierKeepsEveryConstructorParameter()
        {
            List<string> dropped = new();
            foreach (Type baseType in PolymorphicBases)
            {
                foreach (Type subtype in ConcreteSubtypesOf(baseType))
                {
                    ConstructorInfo? ctor = subtype.GetConstructors()
                        .OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
                    if (ctor is null || ctor.GetParameters().Length == 0) continue;

                    JsonTypeInfo info = RuleJson.Options.GetTypeInfo(subtype);
                    HashSet<string> keptProps = info.Properties.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (ParameterInfo param in ctor.GetParameters())
                    {
                        if (!keptProps.Contains(param.Name!))
                        {
                            dropped.Add($"{subtype.Name}.{param.Name} (a constructor parameter) is not serialized.");
                        }
                    }
                }
            }

            Assert.That(dropped, Is.Empty, string.Join(Environment.NewLine, dropped));
        }
    }
}
