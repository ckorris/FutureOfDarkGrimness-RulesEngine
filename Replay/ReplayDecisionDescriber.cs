using System.Globalization;
using FDG.Data;
using FDG.StageResolution.Requests;
using Newtonsoft.Json.Linq;

namespace FDG.Replay
{
    /// <summary>
    /// One ASCII line per recorded decision for a replay's step list (plan 4.3): "P1 activates Warriors (5)",
    /// "Warriors: Shoot", "Warriors move 5.2in", "3 wounds -> Grunts: 2 models". A request type it has no line
    /// for, or anything it cannot read, falls back to the request's display name - it never throws.
    /// <para>
    /// The request and reply are read as plain JSON token trees, as <see cref="DefaultStepFilter"/> reads them:
    /// no <c>$type</c> from the file is ever resolved, and a field a later build renamed costs a fallback line,
    /// not an exception. Names come from <c>storeAtStep</c> - the game state the decision was asked in, which
    /// holds every unit and model the request and reply refer to.
    /// </para>
    /// </summary>
    public static class ReplayDecisionDescriber
    {
        public static string Describe(ReplayStep step, IReadableGameDataStore storeAtStep, ReplayHeader? header = null) =>
            Describe(step.Decision, storeAtStep, header);

        /// <inheritdoc cref="ReplayDecisionDescriber"/>
        public static string Describe(ReplayRequest request, IReadableGameDataStore storeAtStep, ReplayHeader? header = null)
        {
            string fallback = request.Request.DisplayName;
            try
            {
                return ReplayText.Ascii(DescribeOrNull(request, storeAtStep, header) ?? fallback);
            }
            catch (Exception)
            {
                return ReplayText.Ascii(fallback);
            }
        }

        private static string? DescribeOrNull(ReplayRequest request, IReadableGameDataStore store, ReplayHeader? header)
        {
            if (request.Reply == null)
            {
                return null;
            }

            string type = request.Request.RequestType;
            string display = request.Request.DisplayName;
            JObject? ask = RecordedJson.TryParse(request.Request.RequestJson) as JObject;
            JToken? reply = RecordedJson.TryParse(request.Reply.ReplyJson);
            if (ask == null || reply == null)
            {
                return null;
            }

            if (Is<ChooseUnitToActivateRequest>(type))
            {
                UnitData? unit = Unit(store, RecordedJson.AsReference(reply));
                return unit == null
                    ? null
                    : $"{PlayerLabel(header, request.Request.PlayerId)} activates {unit.Name} ({unit.Models.Count(m => m.GetIsAlive())})";
            }

            if (Is<ChooseActionRequest>(type))
            {
                string? unitName = UnitById(store, ask["ActivatingUnitID"]?.Value<string>("ID"))?.Name;
                string? action = reply.Type == JTokenType.String ? reply.Value<string>() : null;
                return action == null ? null : $"{unitName ?? display}: {action}";
            }

            if (Is<ChooseRangedAttackRequest>(type))
            {
                string attacker = Unit(store, RecordedJson.AsReference(ask["AttackingUnit"]))?.Name ?? display;
                if (Selected(reply) is not JObject choice)
                {
                    return $"{attacker}: Back";
                }

                string weapon = choice["Weapon"]?.Value<string>("Name") ?? "a weapon";
                UnitData? target = Unit(store, RecordedJson.AsReference(choice["TargetUnit"]));
                return target == null ? $"{attacker} hold fire with {weapon}" : $"{attacker} shoot {target.Name} with {weapon}";
            }

            if (Is<DefineMovementPathRequest>(type))
            {
                string mover = Unit(store, RecordedJson.AsReference(ask["UnitDataBinding"]))?.Name ?? display;
                if (RecordedJson.AsArray(Selected(reply)) is not JArray moves)
                {
                    return $"{mover}: Back";
                }

                float longest = moves.Max(move => PathLength(store, move)) ?? 0f;
                return longest > 0.05f ? $"{mover} move {Inches(longest)}" : $"{mover} stay put";
            }

            if (Is<ChooseMeleeDefenderRequest>(type))
            {
                string? attacker = Unit(store, request.ActivatingUnit)?.Name;
                if (Selected(reply) is not JToken chosen)
                {
                    return $"{attacker ?? display}: Back";
                }

                string? target = Unit(store, RecordedJson.AsReference(chosen))?.Name ?? OptionName(ask, chosen);
                if (target == null) return null;
                return attacker == null ? $"Charge {target}" : $"{attacker} charge {target}";
            }

            if (Is<AssignWoundsRequest>(type))
            {
                string unitName = Unit(store, RecordedJson.AsReference(ask["UnitReceivingWounds"]))?.Name ?? "a unit";
                float wounds = RecordedJson.AsArray(reply["Packets"])?.Sum(p => p.Value<float?>("Wounds") ?? 0f) ?? 0f;
                int models = RecordedJson.AsArray(reply["Commits"])?
                    .Where(c => (c.Value<float?>("Landed") ?? 0f) > 0f)
                    .Select(c => RecordedJson.AsReference(c["Model"]))
                    .Distinct()
                    .Count() ?? 0;
                return $"{Count(wounds, "wound")} -> {unitName}: {Count(models, "model")}";
            }

            if (Is<YesNoRequest>(type))
            {
                return reply.Type == JTokenType.Boolean ? $"{display}: {(reply.Value<bool>() ? "Yes" : "No")}" : null;
            }

            if (Is<ChooseAbilityEffectRequest>(type))
            {
                int? index = reply.Type == JTokenType.Integer ? reply.Value<int>() : null;
                if (index == ChooseAbilityEffectRequest.CANCEL) return $"{display}: Back";
                JArray? options = RecordedJson.AsArray(ask["Options"]);
                string? label = index >= 0 && index < options?.Count ? options![index.Value]?.Value<string>("Label") : null;
                return label == null ? null : $"{display}: {label}";
            }

            if (Is<PlaceObjectiveRequest>(type))
            {
                float? x = reply.Value<float?>("x");
                float? z = reply.Value<float?>("z");
                return x == null || z == null ? null : $"{display} at ({Number(x.Value)}, {Number(z.Value)})";
            }

            // Every other pick from a list: a string choice (StringSelectionRequest), or a reference chosen from
            // named options (SelectionRequest<T> / CancellableSelectionRequest<T> and their subclasses).
            if (RecordedJson.AsArray(ask["ValidOptions"]) != null)
            {
                if (reply.Type == JTokenType.String)
                {
                    return $"{display}: {reply.Value<string>()}";
                }

                JToken? picked = reply is JObject wrapper && wrapper.ContainsKey("$type") ? Selected(reply) : reply;
                if (picked == null)
                {
                    return $"{display}: Back";
                }

                string? name = OptionName(ask, picked);
                return name == null ? null : $"{display}: {name}";
            }

            return null;
        }

        // --- Reading the recorded JSON ---------------------------------------------------------------------

        private static bool Is<TRequest>(string recordedType) => recordedType == typeof(TRequest).FullName;

        // A CancellableResult as the wire writes it: Selected<T> carries "Value"; Cancelled<T> has none.
        private static JToken? Selected(JToken reply) => reply is JObject result ? result["Value"] : null;

        // The Name the request gave the option whose Option reference the reply chose.
        private static string? OptionName(JObject ask, JToken chosen)
        {
            DataReference? reference = RecordedJson.AsReference(chosen);
            JArray? options = RecordedJson.AsArray(ask["ValidOptions"]);
            if (reference == null || options == null)
            {
                return null;
            }

            foreach (JToken option in options)
            {
                if (Equals(RecordedJson.AsReference(option["Option"]), reference))
                {
                    return option.Value<string>("Name");
                }
            }
            return null;
        }

        private static UnitData? Unit(IReadableGameDataStore store, DataReference? reference)
        {
            if (reference == null || store.IsTypeAssigned<UnitData>() == false || store.IsValid(reference.Value, out _) == false)
            {
                return null;
            }

            try
            {
                return store.GetValue<UnitData>(reference.Value);
            }
            catch (Exception)
            {
                return null; // a reference to some other type
            }
        }

        private static UnitData? UnitById(IReadableGameDataStore store, string? id)
        {
            if (id == null || Guid.TryParse(id, out Guid guid) == false || store.IsTypeAssigned<UnitData>() == false)
            {
                return null;
            }

            return store.GetAllValues<UnitData>().FirstOrDefault(unit => unit.ID.ID == guid);
        }

        // One model's recorded path, from where it stood when asked through each waypoint (2D, inches).
        private static float? PathLength(IReadableGameDataStore store, JToken move)
        {
            DataReference? modelRef = RecordedJson.AsReference(move["Model"]);
            JArray? points = RecordedJson.AsArray(move["Positions"]);
            if (modelRef == null || points == null || store.IsValid(modelRef.Value, out _) == false)
            {
                return null;
            }

            Position from = store.GetValue<ModelData>(modelRef.Value).Position;
            float length = 0f;
            foreach (JToken point in points)
            {
                float? x = point.Value<float?>("x");
                float? z = point.Value<float?>("z");
                if (x == null || z == null) return null;
                var to = new Position(x.Value, z.Value);
                length += Position.GetDistance2D(from, to);
                from = to;
            }
            return length;
        }

        private static string PlayerLabel(ReplayHeader? header, PlayerID player)
        {
            ReplaySlot? slot = header?.Slots.FirstOrDefault(s => s.PlayerId == player);
            return slot == null ? "P?" : $"P{slot.SlotId + 1}";
        }

        // Wounds can be fractional under probabilistic dice; whole numbers print without a decimal.
        private static string Number(float value) =>
            value.ToString(Math.Abs(value - MathF.Round(value)) < 0.05f ? "0" : "0.0", CultureInfo.InvariantCulture);

        private static string Inches(float value) => value.ToString("0.0", CultureInfo.InvariantCulture) + "in";

        private static string Count(float value, string noun) =>
            $"{Number(value)} {noun}{(Math.Abs(value - 1f) < 0.05f ? "" : "s")}";
    }
}
