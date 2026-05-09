namespace FDG.Ai
{
    public enum AiUnitArchetype
    {
        Shooting,
        Melee,
        Hybrid
    }

    public static class AiUnitClassifier
    {
        public static AiUnitArchetype Classify(UnitData unit)
        {
            var ranged = unit.GetRangedWeapons();
            var melee = unit.GetMeleeWeapons();

            if (ranged.Count == 0) return AiUnitArchetype.Melee;
            if (melee.Count == 0) return AiUnitArchetype.Shooting;

            // Higher attacks × (1 + AP) = better weapon, on average
            float rangedScore = ranged.Sum(w => w.Attacks * (1 + w.ArmorPenetration));
            float meleeScore  = melee.Sum(w => w.Attacks * (1 + w.ArmorPenetration));

            return meleeScore > rangedScore ? AiUnitArchetype.Hybrid : AiUnitArchetype.Shooting;
        }
    }
}
