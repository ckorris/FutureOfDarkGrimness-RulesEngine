using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Utilities
{
    public static class DisplayStringUtilities
    {
        public static string BuildModelString(IModel model)
        {
            if (model.Weapons.Count == 0)
            {
                return " --- "; //Weird, but I guess possible.
            }

            StringBuilder sb = new StringBuilder(BuildWeaponString(model.Weapons[0]));

            for (int i = 1; i < model.Weapons.Count; i++)
            {
                sb.Append(" | ");
                sb.Append(BuildWeaponString(model.Weapons[i]));
            }

            return sb.ToString();
        }

        public static string BuildWeaponString(Weapon weapon)
        {
            StringBuilder sb = new StringBuilder($"{weapon.Name} - A{weapon.Attacks} AP{weapon.ArmorPenetration} ");

            foreach (SpecialRule rule in weapon.SpecialRules)
            {
                sb.Append($"{rule.ToString()} ");
            }

            return sb.ToString();
        }
    }
}
