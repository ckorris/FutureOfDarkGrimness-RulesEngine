using System.Collections.Generic;
using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions
{
    /// <summary>
    /// An army-wide spell (#033): a named, token-costed, target-selecting effect that any unit carrying
    /// Caster(X) may cast. Authored as serialized JSON on the army (<c>ArmyListFile.Spells</c>) from the
    /// same effect/target vocabulary special rules use — the specific spell is data, the vocabulary is C#.
    ///
    /// <list type="bullet">
    ///   <item><see cref="Threshold"/> — spell tokens spent to attempt the cast (its cost).</item>
    ///   <item><see cref="Target"/> — the legal targets (range, count, friend/foe/any, line of sight).</item>
    ///   <item><see cref="Effect"/> — applied to each chosen target when the 4+ casting roll succeeds
    ///         (a damage <see cref="Effect.DealHits"/> or a "gets RULE once" <see cref="Effect.AddRule"/>).</item>
    /// </list>
    ///
    /// Serializes with the #059 kind-schema: <see cref="Effect"/> is polymorphic, <see cref="Target"/> is a
    /// plain record, <see cref="Threshold"/>/<see cref="Name"/> are scalars. An availability gate isn't
    /// modelled (no corpus spell has one — affordability + a legal target is the gate); add a Condition
    /// field if one ever needs it.
    /// </summary>
    public sealed record SpellDefinition(string Name, int Threshold, TargetSelector Target, Effect Effect);
}
