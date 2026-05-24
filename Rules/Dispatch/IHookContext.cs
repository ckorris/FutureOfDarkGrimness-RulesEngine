using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

public interface IHookContext
{
    public EHookID Hook { get; }
}