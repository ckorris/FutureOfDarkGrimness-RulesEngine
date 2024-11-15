
namespace FDG
{
    public delegate void PositionChangedEventHandler(PositionChangedEventArgs args);
    
    public record PositionChangedEventArgs
    {
        public Position NewPosition;
        public Position OldPosition;

        public PositionChangedEventArgs(Position newPosition, Position oldPosition)
        {
            NewPosition = newPosition;
            OldPosition = oldPosition;
        }
    }
}
