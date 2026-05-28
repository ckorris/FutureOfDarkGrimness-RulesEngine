using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;

namespace FDG.Tests.RulesHarness
{
    // Bare IHookContext carrying only a hook ID. Lets harness tests fire the bus
    // before any concrete payload-bearing context exists. Real contexts (e.g. a
    // hit-roll-complete context with attacker/defender/weapon) are added in
    // Phase 6 when a test needs their payload.
    internal sealed record TestHookContext(EHookID Hook) : IHookContext;
}
