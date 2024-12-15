
using FDG.Stages;

namespace FDG
{
    public class Fast : SpecialRule_Movement
    {
        public override void ProcessMovementContextPrecursor(ref MovementContextPrecursor precursor)
        {
            precursor.MaxAdvanceDistance += 2f;
            precursor.MaxChargeDistance += 4f;
        }
    }
}