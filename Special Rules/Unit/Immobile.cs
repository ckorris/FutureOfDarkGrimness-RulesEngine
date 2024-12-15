
using FDG.Stages;

namespace FDG
{
    public class Immobile : SpecialRule_Movement
    {
        public override void ProcessMovementContextPrecursor(ref MovementContextPrecursor precursor)
        {
            precursor.CanMove = false;
        }
    }
}