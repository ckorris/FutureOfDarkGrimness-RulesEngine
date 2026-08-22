using FDG.Rules.Foundation;

namespace FDG.Rules.Definitions;

/// <summary>
/// The write face of the token-clear-roll fold (round-start Shaken recovery). An operation that
/// clears a token on a roll (<see cref="RuleOperation.ClearTokenOnRoll"/>) applies itself here via
/// <see cref="SinkOperation{TSink}.ApplyTo"/>, so the round-start stage learns each token type's
/// recovery threshold without inspecting concrete op types. Thresholds compose by MINIMUM per token
/// type - the WoundIgnoreSink discipline (#376 Vale Oath Boost): a base rule at 4+ plus a Boost at 3+
/// is ONE roll at 3+, never two chances. The roll itself lives with the stage (one decisive die per
/// token type), since rolling is execution and the sink stays a pure fold.
/// </summary>
public interface ITokenClearRollSink
{
    void ClearOn(TokenType tokenType, int minRoll);
}
