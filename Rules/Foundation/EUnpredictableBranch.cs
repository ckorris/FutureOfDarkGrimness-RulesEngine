namespace FDG.Rules.Foundation
{
    /// <summary>
    /// #197 (P15): the outcome of an Unpredictable unit's per-attack-action die. The rule reads "roll one
    /// die: on a 1-3 the models get AP(+1), on a 4-6 they get +1 to hit instead" - a randomized branch
    /// between two already-built modifiers. The die is DECISIVE (one concrete face even under the
    /// probabilistic roller, like a morale test) and is rolled once for the whole attack action, then
    /// carried down to each weapon's hit/save contexts so both the +1-hit hook (72) and the AP/save hook
    /// (73) read the SAME branch - independent rolls would give both-or-neither instead of exactly one.
    /// </summary>
    public enum EUnpredictableBranch
    {
        /// <summary>No Unpredictable rule applied to this attack (the common case) - no die was rolled.</summary>
        None = 0,

        /// <summary>Rolled 4-6: the attack gets +1 to hit.</summary>
        HitBonus = 1,

        /// <summary>Rolled 1-3: the attack gets AP(+1) (a -1 to the defender's save roll).</summary>
        ApBonus = 2,
    }
}
