using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FDG.Replay
{
    /// <summary>
    /// The <c>.fdgreplay</c> container (#450, plan <c>docs/replay-plan.md</c> section 4.1): one gzip stream
    /// of UTF-8 JSON Lines. Line 1 is the <see cref="ReplayHeader"/>; every later line is one
    /// <see cref="ReplayEvent"/>, tagged with an explicit <c>Kind</c> string so the file stays greppable.
    /// <para>
    /// No compatibility is owed (plan rule 5): a <see cref="ReplayHeader.FormatVersion"/> other than
    /// <see cref="CurrentVersion"/> is refused by <see cref="ReplayReader"/>. Bump the version whenever the
    /// shape of the header or of any event changes; never write a migration.
    /// </para>
    /// </summary>
    public static class ReplayFormat
    {
        public const int CurrentVersion = 1;

        /// <summary>
        /// The running application's version string - the entry assembly's InformationalVersion, which
        /// <c>Directory.Build.props</c> drives for both projects (<c>0.2.0-dev</c> locally, the full
        /// <c>0.2.0+opr...git-...</c> stamp on CI). Falls back to the engine's when there is no entry
        /// assembly (some test hosts).
        /// </summary>
        public static string BuildVersion =>
            InformationalVersionOf(Assembly.GetEntryAssembly()) ?? EngineVersion;

        /// <summary>The engine assembly's InformationalVersion.</summary>
        public static string EngineVersion =>
            InformationalVersionOf(typeof(ReplayFormat).Assembly) ?? "unknown";

        private static string? InformationalVersionOf(Assembly? assembly) =>
            assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        /// <summary>
        /// Settings for the container's own lines. Deliberately NO <c>TypeNameHandling</c>: a replay file is
        /// untrusted input (it can come from anyone), and the polymorphism the format needs is the
        /// <c>Kind</c> discriminator, resolved against a fixed table (<see cref="ReplayEventKinds"/>). The
        /// embedded payload strings (store JSON, request/reply JSON, beat JSON, save JSON) are opaque here;
        /// whoever deserializes them later goes through the store's / wire's allowlist binder.
        /// Enums are written by name (#075 policy, and greppable); nulls are omitted to keep marker lines
        /// short.
        /// </summary>
        internal static JsonSerializerSettings JsonSettings { get; } = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            // Strings stay strings: with the default (DateTime) a log line that happens to look like a date
            // would come back re-formatted from the JObject the Kind converter loads.
            DateParseHandling = DateParseHandling.None,
            Converters = { new StringEnumConverter(), new ReplayEventJsonConverter() },
        };
    }

    /// <summary>File-extension constants, the sibling of <see cref="SaveLoad.GameSaveFile.EXTENSION_NO_PERIOD"/>.</summary>
    public static class ReplayFile
    {
        public const string EXTENSION_NO_PERIOD = "fdgreplay";

        public const string EXTENSION_WITH_PERIOD = "." + EXTENSION_NO_PERIOD;
    }

    /// <summary>A replay that this build cannot read: wrong format version, not gzip, or a malformed line.</summary>
    public sealed class ReplayFormatException : Exception
    {
        public ReplayFormatException(string message) : base(message) { }

        public ReplayFormatException(string message, Exception inner) : base(message, inner) { }
    }
}
