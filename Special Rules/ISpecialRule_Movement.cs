
using FDG.Stages;

namespace FDG
{
    public interface ISpecialRule_Movement : ISpecialRule
    {
        public void ProcessMovementContextPrecursor(ref MovementContextPrecursor precursor);
    }

    public abstract class SpecialRule_Movement : SpecialRule, ISpecialRule_Movement
    {
        public abstract void ProcessMovementContextPrecursor(ref MovementContextPrecursor precursor);
    }
}