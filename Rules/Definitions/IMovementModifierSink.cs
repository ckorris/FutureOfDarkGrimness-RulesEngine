namespace FDG.Rules.Definitions;

public interface IMovementModifierSink
{
    void Add(EActionType action, float deltaInches);
}