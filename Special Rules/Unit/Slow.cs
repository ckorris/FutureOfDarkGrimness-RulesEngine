
using FDG.Stages;

namespace FDG
{
    public class Slow : SpecialRule_Movement
    {
        public override void ProcessMovementContextPrecursor(ref MovementContextPrecursor precursor)
        {
            precursor.MaxAdvanceDistance -= 2f;
            precursor.MaxRushDistance -= 4f;
            precursor.MaxChargeDistance -= 4f;
        }
    }
}