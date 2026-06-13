using FDG.Data;

namespace FDG.Utilities
{
    public static class UnitDataBindingExtension
    {
        public static string Name(this DataBinding<UnitData> unitBinding)
        {
            return unitBinding.GetValue().Name;
        }

        public static PlayerID PlayerID(this DataBinding<UnitData> unitBinding)
        {
            return unitBinding.GetValue().PlayerID;
        }

        public static int Quality(this DataBinding<UnitData> unitBinding)
        {
            return unitBinding.GetValue().Quality;
        }

        public static int Defense(this DataBinding<UnitData> unitBinding)
        {
            return unitBinding.GetValue().Defense;
        }

        public static List<DataBinding<ModelData>> ModelBindings(this DataBinding<UnitData> unitBinding)
        {
            return unitBinding.GetValue().ModelBindings;
        }

        public static float RemainingWounds(this DataBinding<UnitData> unitBinding)
        {
            return unitBinding.GetValue().RemainingWounds;
        }

        public static bool GetIsAlive(this DataBinding<UnitData> unitBinding)
        {
            return unitBinding.GetValue().GetIsAlive();
        }

        public static bool GetIsDead(this DataBinding<UnitData> unitBinding)
        {
            return unitBinding.GetValue().GetIsDead();
        }
    }
}
