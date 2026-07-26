using FDG.Data;
using FDG.Rules.Tokens;

namespace FDG.Network.Synchronization
{
    /// <summary>
    /// #190: A unit's/model's token state lives in a <see cref="TokenContainer"/> FIELD on
    /// <see cref="UnitData"/>/<see cref="ModelData"/> and is mutated in place (AddToken/RemoveTokens). Unlike
    /// position/facing/wounds - each of which is its own store entry, so <c>SetValue</c> fires the update
    /// broadcast - a token change never re-Sets the owning store entry, so it never reaches
    /// <see cref="GameDataUpdateSender"/>. A remote client therefore only ever sees the join-snapshot's
    /// tokens, frozen for the rest of the game (the "Fatigued never clears" field report).
    ///
    /// This host-side helper subscribes to every unit's/model's token change events and, on any add /
    /// count-change / removal, re-Sets the owning entry through the store with its own (unchanged) instance.
    /// That fires the ordinary <c>OnAnyUpdatedTyped -> OnDataUpdatedAsJson</c> path; because the container
    /// serializes as part of the object (<c>[JsonProperty] _tokens</c>), the resulting
    /// <see cref="Messages.DataMessages.UpdateSingleDataMessage"/> carries the current tokens and the
    /// client's <c>SetValueWithJson</c> resyncs them.
    ///
    /// Host-only by construction (only the host runs FDGServer / owns a <see cref="GameDataUpdateSender"/>).
    /// The re-Set fires exactly one broadcast and never touches the container again, so there is no
    /// echo/loop; deserialization on the client sets the token list directly (no AddToken), so it raises no
    /// events there either.
    /// </summary>
    internal class TokenChangeBroadcaster
    {
        private readonly IReadWriteableGameDataStore _store;

        // Guards against hooking the same container twice. A container instance is created exactly once, so
        // reference identity is the right key: the resume path enumerates existing objects while the
        // new-game path hooks on creation, and this stops the two from ever double-subscribing one object.
        private readonly HashSet<ITokenContainer> _hooked = new();

        public TokenChangeBroadcaster(IReadWriteableGameDataStore store)
        {
            _store = store;

            // New-game path: units/models are created AFTER this ctor (CreateArmies), so catch them as they
            // are added to the store.
            _store.SubscribeToOnCreated<UnitData>(HookUnit);
            _store.SubscribeToOnCreated<ModelData>(HookModel);

            // Resume path: the store arrives already populated (GameSaveSerializer runs before FDGServer), so
            // OnCreated already fired for every loaded object before this helper existed. Hook them now.
            foreach (DataBinding<UnitData> unit in _store.GetAllDataBindings<UnitData>())
            {
                HookUnit(unit.Reference, unit.GetValue());
            }
            foreach (DataBinding<ModelData> model in _store.GetAllDataBindings<ModelData>())
            {
                HookModel(model.Reference, model.GetValue());
            }
        }

        private void HookUnit(DataReference reference, UnitData unit)
            => Hook(unit.Tokens, reference, () => _store.SetValue(reference, unit));

        private void HookModel(DataReference reference, ModelData model)
            => Hook(model.Tokens, reference, () => _store.SetValue(reference, model));

        private void Hook(ITokenContainer container, DataReference reference, Action reBroadcast)
        {
            if (!_hooked.Add(container))
            {
                return;
            }

            // The owning entry can be gone by the time a token event fires (an object destroyed while its
            // tokens are being cleared); a re-Set on an invalid reference would throw. The client learns of
            // that removal via the object's own destruction broadcast, so skipping here loses nothing.
            void Guarded()
            {
                if (_store.IsValid(reference, out _))
                {
                    reBroadcast();
                }
            }

            container.OnTokenAdded += _ => Guarded();
            container.OnTokenRemoved += _ => Guarded();
            container.OnTokenCountChanged += _ => Guarded();
        }
    }
}
