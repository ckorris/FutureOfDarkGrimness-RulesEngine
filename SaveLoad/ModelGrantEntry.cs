namespace FDG.SaveLoad
{
    /// <summary>
    /// #407: "<see cref="Count"/> of this unit's models also have these rules". The army file is otherwise
    /// a unit-level aggregate, which turned a one-model upgrade such as an HDF "Weapon Team (..., Tough(3))"
    /// into Tough(3) on every model - 30 wounds on a 10-model squad. At army load the rules attach to the
    /// unit's LAST <see cref="Count"/> models (see <c>GameBootstrap.AttachRulesFromArmyList</c>), so the
    /// automatic wound order reaches them last.
    /// </summary>
    [Serializable]
    public class ModelGrantEntry
    {
        public int Count { get; set; }

        public List<SpecialRuleEntry> Rules { get; set; } = new List<SpecialRuleEntry>();
    }
}
