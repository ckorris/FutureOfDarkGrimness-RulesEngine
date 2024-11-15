

namespace FDG
{
    public interface IModel
    {
        public const int DEFAULT_WOUND_COUNT = 1;

        public float TotalWounds { get; }

        public float WoundsDealt { get; set; }

        public Position Position { get; }

        public List<IWeapon> Weapons { get; }

        public void SetPosition(Position newPosition);

        public void DealWounds(float wounds);

        public event PositionChangedEventHandler OnPositionChanged;

        public event WoundsDealtEventHandler OnWoundsDealt;
    }

    public class Model : IModel
    {

        public float TotalWounds { get; }

        public float WoundsDealt { get; set; }

        public List<IWeapon> Weapons { get; }

        public Position Position { get; private set; }

        public event PositionChangedEventHandler OnPositionChanged;

        public event WoundsDealtEventHandler OnWoundsDealt;


        public Model(List<IWeapon> weapons, Position position)
        {
            Weapons = weapons;
            Position = position;
            //TODO: Not sure where Tough will come in for modifying wounds, but there should be a stage
            //that processes that kind of thing.

            TotalWounds = IModel.DEFAULT_WOUND_COUNT;
            WoundsDealt = 0;
        }

        public void SetPosition(Position newPosition)
        {
            Position oldPosition = Position;
            Position = newPosition;

            OnPositionChanged?.Invoke(new PositionChangedEventArgs(newPosition, oldPosition));
        }

        public void DealWounds(float wounds)
        {
            WoundsDealt += wounds;

            OnWoundsDealt?.Invoke(new WoundsDealtEventArgs(wounds, TotalWounds - WoundsDealt, WoundsDealt >= TotalWounds));
        }

    }

    public static class IModelExtensions
    {
        public static bool GetIsAlive(this IModel model)
        {
            return model.WoundsDealt < model.TotalWounds;
        }

        public static bool GetIsDead(this IModel model)
        {
            return model.WoundsDealt >= model.TotalWounds;
        }
    }
}
