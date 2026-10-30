using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FDG.Replay
{
    /// <summary>Writes the <c>.fdgreplay</c> container: gzip(UTF-8 JSON Lines), header first.</summary>
    public static class ReplayWriter
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// Writes <paramref name="header"/> and then every event, one JSON object per line, gzipped into
        /// <paramref name="output"/>. The stream is left open (callers own it - a file, or a MemoryStream
        /// whose bytes go into a save).
        /// </summary>
        public static void WriteGzip(ReplayHeader header, IEnumerable<ReplayEvent> events, Stream output)
        {
            using GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
            using StreamWriter writer = new StreamWriter(gzip, Utf8NoBom, bufferSize: 1 << 16);

            WriteLine(writer, header);
            foreach (ReplayEvent replayEvent in events)
            {
                WriteLine(writer, replayEvent);
            }
        }

        private static void WriteLine(StreamWriter writer, object value)
        {
            // JSON escapes every newline inside a string value, so one object is always exactly one line.
            writer.Write(JsonConvert.SerializeObject(value, ReplayFormat.JsonSettings));
            writer.Write('\n');
        }
    }

    /// <summary>A whole replay in memory: the header and every event, in <c>Seq</c> order as written.</summary>
    public sealed class ReplayDocument
    {
        public ReplayHeader Header { get; }

        public IReadOnlyList<ReplayEvent> Events { get; }

        public ReplayDocument(ReplayHeader header, IReadOnlyList<ReplayEvent> events)
        {
            Header = header;
            Events = events;
        }
    }

    /// <summary>
    /// Reads the <c>.fdgreplay</c> container. Minimal by design (#450): it parses and refuses a format
    /// mismatch; build-stamp warnings, indexing and playback are #454's.
    /// </summary>
    public static class ReplayReader
    {
        public static ReplayDocument ReadGzip(Stream input)
        {
            try
            {
                using GZipStream gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
                using StreamReader reader = new StreamReader(gzip, Encoding.UTF8);
                return Read(reader);
            }
            catch (InvalidDataException notGzip)
            {
                throw new ReplayFormatException("Not a replay file (the data is not gzip-compressed).", notGzip);
            }
        }

        private static ReplayDocument Read(StreamReader reader)
        {
            string? headerLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(headerLine))
            {
                throw new ReplayFormatException("Replay is empty (no header line).");
            }

            // Check the version on the raw object BEFORE binding it, so a future header shape is refused
            // with the version message rather than whatever binding error that shape would produce.
            JObject headerObject = ParseLine(headerLine, lineNumber: 1);
            int? version = headerObject.Value<int?>(nameof(ReplayHeader.FormatVersion));
            if (version != ReplayFormat.CurrentVersion)
            {
                throw new ReplayFormatException(
                    $"Replay format {version?.ToString() ?? "(none)"}; this build reads {ReplayFormat.CurrentVersion}.");
            }

            JsonSerializer serializer = JsonSerializer.Create(ReplayFormat.JsonSettings);
            ReplayHeader header = headerObject.ToObject<ReplayHeader>(serializer)
                ?? throw new ReplayFormatException("Replay header is null.");

            List<ReplayEvent> events = new List<ReplayEvent>();
            int lineNumber = 1;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (line.Length == 0)
                {
                    continue;
                }

                ReplayEvent? replayEvent;
                try
                {
                    replayEvent = JsonConvert.DeserializeObject<ReplayEvent>(line, ReplayFormat.JsonSettings);
                }
                catch (JsonException malformed)
                {
                    throw new ReplayFormatException($"Replay line {lineNumber} is not a valid event: {malformed.Message}", malformed);
                }
                catch (ReplayFormatException badKind)
                {
                    throw new ReplayFormatException($"Replay line {lineNumber}: {badKind.Message}", badKind);
                }

                if (replayEvent == null)
                {
                    throw new ReplayFormatException($"Replay line {lineNumber} is null.");
                }

                events.Add(replayEvent);
            }

            return new ReplayDocument(header, events);
        }

        private static JObject ParseLine(string line, int lineNumber)
        {
            try
            {
                using JsonTextReader reader = new JsonTextReader(new StringReader(line))
                {
                    DateParseHandling = DateParseHandling.None,
                };
                return JObject.Load(reader);
            }
            catch (JsonException malformed)
            {
                throw new ReplayFormatException($"Replay line {lineNumber} is not a JSON object: {malformed.Message}", malformed);
            }
        }
    }
}
