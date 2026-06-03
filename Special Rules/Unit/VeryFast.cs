

using FDG.Stages;

namespace FDG
{
    public class VeryFast : SpecialRule_Movement
    {
        public override void ProcessMovementContextPrecursor(ref MovementContextPrecursor precursor)
        {
            precursor.MaxAdvanceDistance += 4f;
            precursor.MaxRushDistance += 8f;
            precursor.MaxChargeDistance += 8f;
        }
    }
}
