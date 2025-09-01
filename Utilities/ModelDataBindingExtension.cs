using FDG.Data;

namespace FDG.Utilities
{
    public static class ModelDataBindingExtension
    {
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
    }
}
