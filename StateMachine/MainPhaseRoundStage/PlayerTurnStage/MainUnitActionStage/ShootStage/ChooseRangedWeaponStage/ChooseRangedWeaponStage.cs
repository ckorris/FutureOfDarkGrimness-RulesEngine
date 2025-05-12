using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{

    public class ChooseRangedWeaponStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnChoseWeapon;

        public ChooseRangedWeaponStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnChoseWeapon = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            if (context.AvailableWeapons.Count == 0)
            {
                throw new Exception($"Available weapon dictionary was empty when entering {nameof(ChooseRangedWeaponStage)}.");
            }

            //TODO: Handle situations like Deadly, where you have to use a specific weapon first.
            IReadOnlyDictionary<IWeapon, int> availableWeapons = new ConcurrentDictionary<IWeapon, int>(context.AvailableWeapons);
            IReadOnlyDictionary<IWeapon, int> unavailableWeapons = new ConcurrentDictionary<IWeapon, int>(context.AlreadyUsedWeapons);

            //TODO: Since we don't store weapons in bindings, we're hackedly using their stats names, which have no
            //protection against identical names.
            List<(string, IWeapon)> validOptions = new List<(string, IWeapon)>();
            List<StringSelectionRequest.InvalidOption> invalidOptions = new List<StringSelectionRequest.InvalidOption>();

            foreach (KeyValuePair<IWeapon, int> kvp in availableWeapons)
            {
                validOptions.Add((kvp.Key.GetWeaponNameAndStats(kvp.Value), kvp.Key));
            }

            foreach (KeyValuePair<IWeapon, int> kvp in unavailableWeapons)
            {
                invalidOptions.Add(new StringSelectionRequest.InvalidOption(kvp.Key.GetWeaponNameAndStats(kvp.Value),
                    "The unit has already attacked with this weapon."));
            }

            StringSelectionRequest request = new StringSelectionRequest(context.AttackingUnit.PlayerID,
                "Choose weapon:", validOptions.Select(option => option.Item1).ToList(), invalidOptions);

            string chosenWeaponStatsName = await GameContext.PlayerRequester.RequestDecision<StringSelectionRequest, string>(
                context.AttackingUnit.PlayerID, request);

            IWeapon chosenWeapon = validOptions.First(option => option.Item1 == chosenWeaponStatsName).Item2;

            context.SetAttackWeapon(chosenWeapon, out int weaponCount);
            GameContext.Log($"Chose weapon: {chosenWeapon.Name}. Count: {weaponCount}.");

            OnChoseWeapon.Activate(context);
        }
    }
}