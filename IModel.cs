using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FDG
{
    public interface IModel
    {
        public const int DEFAULT_WOUND_COUNT = 1;

        public int TotalWounds { get; }

        public int WoundsDealt { get; set; }

        public Position Position { get; set; }

        public List<IWeapon> Weapons { get; }
    }

    public class Model : IModel
    {
        public int TotalWounds { get; }

        public int WoundsDealt { get; set; }

        public List<IWeapon> Weapons { get; }

        public Position Position { get; set; }

        public Model(List<IWeapon> weapons, Position position)
        {
            Weapons = weapons;
            Position = position;
            //TODO: Not sure where Tough will come in for modifying wounds, but there should be a stage
            //that processes that kind of thing.

            TotalWounds = IModel.DEFAULT_WOUND_COUNT;
            WoundsDealt = 0;
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
