using FDG.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG
{
    public interface IModelTemplate
    {
        float BaseRadiusInches { get; }

        List<Weapon> Weapons { get; }

        List<SpecialRule> SpecialRules { get; }
    }

    public class ModelTemplate : IModelTemplate
    {
        public float BaseRadiusInches { get; private set; }

        public List<Weapon> Weapons { get; private set; }

        public List<SpecialRule> SpecialRules { get; private set; }

        public ModelTemplate(float baseRadiusInches, List<Weapon> weapons, List<SpecialRule> specialRules)
        {
            BaseRadiusInches = baseRadiusInches;
            Weapons = weapons;
            SpecialRules = specialRules;
        }
    }
}
