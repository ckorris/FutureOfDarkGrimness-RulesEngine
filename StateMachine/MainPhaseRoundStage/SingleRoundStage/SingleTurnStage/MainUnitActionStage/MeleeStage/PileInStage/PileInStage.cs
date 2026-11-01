using FDG.Data;

namespace FDG.Stages
{
    public class PileInStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnPiledIn;

        public PileInStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnPiledIn = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            GameContext.LogDebug("Entered pile in stage.");

            // 3.6 C1 (#417): "charged defenders move up to 3\" into contact" - the CHARGED unit piles in, whoever
            // strikes first. DetermineStrikeOrderStage runs before this stage and swaps AttackingUnit /
            // DefendingUnit for Counter (and Unwieldy), so the roles cannot be read here: after a swap they named
            // the charger as the defender and this stage walked the charger up to the unit it had just charged
            // ("Pile in: 5 defender model(s)" for a 5-model charger against 3 Gunners). The charger is the
            // context's immutable capture, as ApplyFatigueStage and PostMeleeStage read it; the charged unit is
            // whichever side it is not. A melee with no charger keeps the roles as they stand.
            DataBinding<UnitData> chargerBinding = context.ChargingUnit ?? context.AttackingUnit;
            DataBinding<UnitData> chargedBinding =
                chargerBinding.GetValue().ID == context.AttackingUnit.GetValue().ID
                    ? context.DefendingUnit
                    : context.AttackingUnit;
            var chargingUnit = chargerBinding.GetValue();
            var defendingUnit = chargedBinding.GetValue();

            // #159: every enemy of the defender OTHER than the charging unit — third parties, or a unit it is
            // already engaged with — is a hard obstacle the defender must not pile through on its way to the
            // charger. Without this a defender plowed straight into a different enemy's base (deeply overlapping).
            var otherEnemies = MovementUtilities.GetEnemyModelFootprints(
                chargedBinding, GameContext, excludeUnit: chargerBinding);

            // #205: the defender's FRIENDLY units are hard obstacles too - a pile-in may not stack the defender
            // on top of its own teammates any more than on a third-party enemy. Pile-in stops the mover short of
            // every obstacle footprint, so folding friendlies into the same list keeps it off them.
            var friendlyObstacles = MovementUtilities.GetFriendlyModelFootprints(chargedBinding, GameContext);
            var obstacles = otherEnemies.Concat(friendlyObstacles).ToList();

            var moves = PileInUtilities.ComputePileInMoves(
                chargingUnit.ModelBindings,
                defendingUnit.ModelBindings,
                GameContext.TableState.Terrain.Objects,
                obstacles);

            foreach (var move in moves)
            {
                move.Model.GetValue().SetPosition(move.NewPosition);
            }

            if (moves.Count > 0)
            {
                // #330: report how many defenders actually reached base contact, so a formation-y
                // result is visible in the log instead of hiding behind a bare move count.
                int inContact = PileInUtilities.CountDefendersInBaseContact(
                    chargingUnit.ModelBindings, defendingUnit.ModelBindings);
                GameContext.Log($"Pile in: {moves.Count} defender model(s) moved toward the charging unit ({inContact} in base contact).");
            }

            await OnPiledIn.Activate(context);
        }
    }
}
