using Newtonsoft.Json.Linq;

namespace FDG.Replay
{
    /// <summary>
    /// A cheap field diff between two recorded values of one store entry (#455): the names of the top-level JSON
    /// properties whose values differ. Used by <see cref="ReplayVerifier"/> to say what a divergent entry got
    /// wrong and by the CLI's <c>--events</c> to say what a <c>StoreUpdate</c> changed. Values are compared as
    /// token trees, so key order does not count as a change (1 and 1.0 do: the store writes a field one way).
    /// </summary>
    public static class ReplayJsonDiff
    {
        /// <summary>What a non-object value is called when it differs as a whole.</summary>
        public const string WholeValue = "(value)";

        /// <summary>
        /// The top-level properties that differ between <paramref name="before"/> and <paramref name="after"/>, in
        /// <paramref name="after"/>'s order, then the ones only <paramref name="before"/> had. When either side is not
        /// a JSON object (a bare position, a string entry), or does not parse, the answer is <see cref="WholeValue"/>
        /// if they differ at all. Empty when they are equal.
        /// </summary>
        public static IReadOnlyList<string> ChangedProperties(string? before, string? after)
        {
            JToken? old = before == null ? null : RecordedJson.TryParse(before);
            JToken? now = after == null ? null : RecordedJson.TryParse(after);
            if (old is JObject oldObject && now is JObject newObject)
            {
                var changed = new List<string>();
                foreach (JProperty property in newObject.Properties())
                {
                    if (JToken.DeepEquals(property.Value, oldObject[property.Name]) == false)
                    {
                        changed.Add(property.Name);
                    }
                }
                foreach (JProperty property in oldObject.Properties())
                {
                    if (newObject.ContainsKey(property.Name) == false)
                    {
                        changed.Add(property.Name);
                    }
                }
                return changed;
            }

            bool same = old != null && now != null ? JToken.DeepEquals(old, now) : before == after;
            return same ? Array.Empty<string>() : new[] { WholeValue };
        }
    }
}
