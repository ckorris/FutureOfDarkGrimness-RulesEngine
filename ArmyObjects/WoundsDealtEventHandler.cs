
namespace FDG
{
    public delegate void WoundsDealtEventHandler(WoundsDealtEventArgs args);

    public record WoundsDealtEventArgs
    {
        public float WoundsDealt;
        public float NewRemainingWounds;
        public bool IsNowDead;

        public WoundsDealtEventArgs(float woundsDealt, float newRemainingWounds, bool isNowDead)
        {
            WoundsDealt = woundsDealt;
            NewRemainingWounds = newRemainingWounds;
            IsNowDead = isNowDead;
        }
    }
}
