using System;
using System.Text.Json;
using FDG.Rules.Serialization;
using FDG.SaveLoad;

namespace FDG.Network.Messages
{
    /// <summary>
    /// Carries a player's full army list (including #059 embedded rule definitions) from client to
    /// host. The army travels as a System.Text.Json string in <see cref="ArmyListJson"/>, NOT as a
    /// live <see cref="ArmyListFile"/>: the message transport is Newtonsoft <c>TypeNameHandling.Auto</c>
    /// and the STJ-only rule-definition graph (Condition/Effect/ValueSource/...) does not round-trip
    /// through it (Newtonsoft chokes on the computed <c>RequiredCapabilities</c> and emits fragile
    /// reflection <c>$type</c> names). Encoding the army with <see cref="RuleJson.Options"/> keeps each
    /// serializer at its strength — STJ owns the rich graph on both ends (the same kind-tagged bytes as
    /// the on-disk <c>.fdgarmy</c>), Newtonsoft just moves an opaque string. (#059 workstream 5.)
    ///
    /// Build with <see cref="FromArmy"/> and read back with <see cref="DecodeArmy"/> so the encode/
    /// decode contract lives here rather than at the lobby call sites.
    /// </summary>
    public record ArmyListUpdateMessage(PlayerID PlayerID, string ArmyListJson)
    {
        /// <summary> Encodes <paramref name="armyListFile"/> to a transport-safe STJ string. </summary>
        public static ArmyListUpdateMessage FromArmy(PlayerID playerID, ArmyListFile armyListFile) =>
            new ArmyListUpdateMessage(playerID, JsonSerializer.Serialize(armyListFile, RuleJson.Options));

        /// <summary> Decodes the carried army back into an <see cref="ArmyListFile"/>. </summary>
        public ArmyListFile DecodeArmy() =>
            JsonSerializer.Deserialize<ArmyListFile>(ArmyListJson, RuleJson.Options)
            ?? throw new InvalidOperationException(
                $"Army list payload for player {PlayerID} deserialized to null.");
    }
}
