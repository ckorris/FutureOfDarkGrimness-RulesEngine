using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Applies the synchronous token state-mutations in a rule-operation queue to live unit/model containers:
/// the cost grants/consumes the activated-ability dispatcher emits (<see cref="RuleOperation.GrantTokenToUnit"/>,
/// <see cref="RuleOperation.ConsumeTokensFromUnit"/>, and their model-scoped twins), plus the same ops produced
/// by GrantToken/Aura effects. The non-executable, non-sink counterpart to <see cref="OperationExecutor"/>:
/// these ops carry their own target reference, so no engine services are needed — unlike
/// <see cref="ExecutableOperation"/>s, which call into <see cref="IOperationServices"/>.
///
/// Stages that resolve an ability (<see cref="RuleEvaluator.ResolveAbility"/>) run this over the queue so the
/// once-per-X cost gate is actually closed; without it the marker the gate reads is never granted (it is not an
/// <see cref="ExecutableOperation"/>, so <see cref="OperationExecutor"/> skips it).
/// </summary>
public static class OperationApplier
{
    public static void ApplyTokenOperations(IEnumerable<RuleOperation> operations)
    {
        foreach (RuleOperation operation in operations)
        {
            switch (operation)
            {
                case RuleOperation.GrantTokenToUnit grant:
                    grant.Unit.Tokens.AddToken(grant.TokenToGrant);
                    break;
                case RuleOperation.GrantTokenToModel grant:
                    grant.Model.Tokens.AddToken(grant.TokenToGrant);
                    break;
                case RuleOperation.ConsumeTokensFromUnit consume:
                    consume.Unit.Tokens.RemoveTokens(consume.TType, consume.Count);
                    break;
                case RuleOperation.ConsumeTokensFromModel consume:
                    consume.Model.Tokens.RemoveTokens(consume.TType, consume.Count);
                    break;
                case RuleOperation.ConsumeRuleGrant consumeGrant:
                    consumeGrant.Unit.Tokens.RemoveTokensWithPayload(TokenType.RuleGrant, consumeGrant.Payload, 1);
                    break;
                case RuleOperation.InvokeHeal heal:
                    // #100 #2e — Mend's heal. Self-contained (mutates the model directly, no services), so it
                    // belongs with the token ops rather than the executable/service ops. Clamp to the wounds
                    // actually taken so a heal never pushes a model above its max.
                    float healable = System.Math.Min(heal.Amount, heal.Target.WoundsDealt);
                    if (healable > 0f)
                    {
                        heal.Target.DealWounds(-healable);
                    }
                    break;
            }
        }
    }
}
