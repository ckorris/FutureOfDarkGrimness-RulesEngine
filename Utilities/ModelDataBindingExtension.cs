using FDG.Data;

namespace FDG.Utilities
{
    public static class ModelDataBindingExtension
    {
        public static Position Position(this DataBinding<ModelData> modelBinding)
        {
            return modelBinding.GetValue().PositionBinding.GetValue();
        }

        public static DataBinding<Position> PositionBinding(this DataBinding<ModelData> modelBinding)
        {
            return modelBinding.GetValue().PositionBinding;
        }

        public static float TotalWounds(this DataBinding<ModelData> modelBinding)
        {
            return modelBinding.GetValue().TotalWounds;
        }

        public static float WoundsDealt(this DataBinding<ModelData> modelBinding)
        {
            return (modelBinding.GetValue() as IModel).WoundsDealt;
        }

        public static bool GetIsAlive(this DataBinding<ModelData> modelBinding)
        {
            return modelBinding.GetValue().GetIsAlive();
        }

        public static bool GetIsDead(this DataBinding<ModelData> modelBinding)
        {
            return modelBinding.GetValue().GetIsDead();
        }

        public static List<Weapon> Weapons(this DataBinding<ModelData> modelBinding)
        {
            return modelBinding.GetValue().Weapons;
        }
    }
}
