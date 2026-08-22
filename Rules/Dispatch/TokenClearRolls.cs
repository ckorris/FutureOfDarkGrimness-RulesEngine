using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Enacts the folded token-clear rolls for one unit's round-start evaluation: fold every
/// <see cref="RuleOperation.ClearTokenOnRoll"/> in <paramref name="operations"/> through a
/// <see cref="TokenClearRollSink"/> (best threshold per token type), then make ONE decisive roll per
/// token type via <see cref="IOperationServices.ClearTokenOnRoll"/> (which rolls, strips the tokens
/// on a pass, and presents the die beat + recovery banner). Extracted from the round-start stage so
/// the fold-then-single-roll contract is directly testable (#376 Vale Oath Boost).
/// </summary>
public static class TokenClearRolls
{
    public static async Task ResolveAsync(IUnit unit, IReadOnlyList<RuleOperation> operations,
        IOperationServices services)
    {
        var sink = new TokenClearRollSink();
        sink.ApplyFrom(operations);
        foreach ((TokenType tokenType, int minRoll) in sink.Entries)
        {
            await services.ClearTokenOnRoll(unit, tokenType, minRoll);
        }
    }
}
