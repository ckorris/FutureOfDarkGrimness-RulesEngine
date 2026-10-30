using System.IO.Compression;
using System.Text;
using FDG.Data;
using FDG.Replay;
using FDG.StageResolution;
using NUnit.Framework;

namespace FDG.Tests
{
    // #450: the .fdgreplay container (plan docs/replay-plan.md section 4.1) - gzip(JSON Lines), header
    // first, one explicitly Kind-tagged event per line. These pin the shape every later replay ticket reads
    // (#451 client recording, #453 save embedding, #454 playback, #455 CLI tools).
    [TestFixture]
    public class ReplayFormatTests
    {
        private static readonly PlayerID PlayerA = new PlayerID(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        private static readonly PlayerID PlayerB = new PlayerID(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        private static readonly TaskID Task1 = new TaskID(Guid.Parse("33333333-3333-3333-3333-333333333333"));

        private static DataReference Ref(int type, int index, int generation) =>
            new DataReference { TypeID = new TypeID(type), Index = index, Generation = generation };

        private static ReplayHeader MakeHeader()
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.DiceSeed = 4242;
            settings.ArmyPoints = 2000;

            return new ReplayHeader
            {
                BuildVersion = "0.2.0-dev",
                EngineVersion = "0.2.0-dev",
                GameId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Seed = 4242,
                RandomnessType = ERandomnessType.Realistic,
                Settings = settings,
                Slots = new List<ReplaySlot>
                {
                    new ReplaySlot { SlotId = 0, PlayerId = PlayerA, Name = "Alpha", ArmyName = "Shooters", Team = 0, Controller = EReplayController.Human },
                    new ReplaySlot { SlotId = 1, PlayerId = PlayerB, Name = "Bravo (AI)", ArmyName = "Defenders", Team = 1, Controller = EReplayController.Ai },
                },
                RecordedBy = EReplayRecordedBy.Host,
                RecordedByPlayerId = PlayerA,
                StartedUtc = new DateTime(2026, 10, 30, 12, 34, 56, DateTimeKind.Utc),
            };
        }

        /// <summary>One event of every kind, with Seq dense and T increasing - what a recorder produces.</summary>
        private static List<ReplayEvent> OneOfEachKind() => new List<ReplayEvent>
        {
            new StoreAddEvent(Ref(5, 0, 1), "{\"Name\":\"Warriors\"}") { Seq = 0, T = 0 },
            new StoreUpdateEvent(Ref(5, 0, 1), "{\"Name\":\"Warriors\",\"Hit\":true}") { Seq = 1, T = 3 },
            new StoreRemoveEvent(Ref(7, 2, 4)) { Seq = 2, T = 10 },
            new SnapshotEvent(0, "{\"Version\":1,\"Entries\":[]}") { Seq = 3, T = 11 },
            new RequestEvent(Task1, PlayerA, "FDG.StageResolution.Requests.YesNoRequest", "System.Boolean",
                "Deciding Whether to Reactivate", "{\"$type\":\"YesNoRequest\",\"Question\":\"Go?\"}") { Seq = 4, T = 20 },
            new ReplyEvent(Task1, "true") { Seq = 5, T = 950 },
            new RequestErrorEvent(Task1, "resolver threw") { Seq = 6, T = 951 },
            new BeatEvent("{\"$type\":\"BannerBeat\",\"Text\":\"Round 1\"}") { Seq = 7, T = 960 },
            new LogEvent("Activating: Warriors.") { Seq = 8, T = 961 },
            new LogEvent("Entered stage X.") { Seq = 9, T = 962, Debug = true },
            new MarkerEvent(EReplayMarker.GameStart) { Seq = 10, T = 970 },
            new MarkerEvent(EReplayMarker.RoundStart) { Seq = 11, T = 971, Round = 1 },
            new MarkerEvent(EReplayMarker.ActivationStart) { Seq = 12, T = 972, Round = 1, Activation = 1, PlayerId = PlayerB },
            new MarkerEvent(EReplayMarker.ActivationStart) { Seq = 13, T = 973, Round = 1, Activation = 1, PlayerId = PlayerB, UnitRef = Ref(5, 0, 1) },
            new MarkerEvent(EReplayMarker.GameEnded) { Seq = 14, T = 99_000, Outcome = "Win", Message = "Alpha wins!" },
        };

        private static byte[] Write(ReplayHeader header, IEnumerable<ReplayEvent> events)
        {
            using MemoryStream stream = new MemoryStream();
            ReplayWriter.WriteGzip(header, events, stream);
            return stream.ToArray();
        }

        private static ReplayDocument Read(byte[] bytes)
        {
            using MemoryStream stream = new MemoryStream(bytes);
            return ReplayReader.ReadGzip(stream);
        }

        private static string[] DecompressLines(byte[] bytes)
        {
            using GZipStream gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using StreamReader reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }

        private static byte[] Gzip(string text)
        {
            using MemoryStream stream = new MemoryStream();
            using (GZipStream gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true))
            {
                byte[] raw = Encoding.UTF8.GetBytes(text);
                gzip.Write(raw, 0, raw.Length);
            }
            return stream.ToArray();
        }

        [Test]
        public void Header_RoundTrips()
        {
            ReplayHeader original = MakeHeader();

            ReplayHeader read = Read(Write(original, Array.Empty<ReplayEvent>())).Header;

            Assert.That(read.FormatVersion, Is.EqualTo(ReplayFormat.CurrentVersion));
            Assert.That(read.BuildVersion, Is.EqualTo("0.2.0-dev"));
            Assert.That(read.EngineVersion, Is.EqualTo("0.2.0-dev"));
            Assert.That(read.GameId, Is.EqualTo(original.GameId));
            Assert.That(read.Seed, Is.EqualTo(4242));
            Assert.That(read.RandomnessType, Is.EqualTo(ERandomnessType.Realistic));
            Assert.That(read.Settings.DiceSeed, Is.EqualTo(4242));
            Assert.That(read.Settings.ArmyPoints, Is.EqualTo(2000));
            Assert.That(read.RecordedBy, Is.EqualTo(EReplayRecordedBy.Host));
            Assert.That(read.RecordedByPlayerId, Is.EqualTo(PlayerA));
            Assert.That(read.StartedUtc, Is.EqualTo(original.StartedUtc));
            Assert.That(read.StartedUtc.Kind, Is.EqualTo(DateTimeKind.Utc));

            Assert.That(read.Slots, Has.Count.EqualTo(2));
            Assert.That(read.Slots[1].SlotId, Is.EqualTo(1));
            Assert.That(read.Slots[1].PlayerId, Is.EqualTo(PlayerB));
            Assert.That(read.Slots[1].Name, Is.EqualTo("Bravo (AI)"));
            Assert.That(read.Slots[1].ArmyName, Is.EqualTo("Defenders"));
            Assert.That(read.Slots[1].Team, Is.EqualTo(1));
            Assert.That(read.Slots[1].Controller, Is.EqualTo(EReplayController.Ai));
        }

        [Test]
        public void EveryEventKind_RoundTrips_WithSeqAndTIntact()
        {
            List<ReplayEvent> original = OneOfEachKind();

            ReplayDocument read = Read(Write(MakeHeader(), original));

            // Records compare by value, so this checks the concrete type, Seq, T and every payload field.
            Assert.That(read.Events, Is.EqualTo(original));
            Assert.That(read.Events.Select(e => e.Seq), Is.EqualTo(Enumerable.Range(0, original.Count).Select(i => (long)i)));
        }

        [Test]
        public void TheRoundTripFixture_CoversEveryRegisteredKind()
        {
            // A new event kind must come with round-trip coverage: add it to OneOfEachKind.
            Assert.That(OneOfEachKind().Select(e => e.Kind).Distinct(), Is.EquivalentTo(ReplayEventKinds.AllKinds));
        }

        [Test]
        public void Lines_AreHeaderThenOneKindTaggedEventEach_SeqTAndKindFirst()
        {
            string[] lines = DecompressLines(Write(MakeHeader(), OneOfEachKind()));

            Assert.That(lines, Has.Length.EqualTo(1 + OneOfEachKind().Count), "one line per event, plus the header");
            Assert.That(lines[0], Does.StartWith("{\"FormatVersion\":1,"));
            Assert.That(lines[1], Does.StartWith("{\"Seq\":0,\"T\":0,\"Kind\":\"StoreAdd\","),
                "Kind is an explicit greppable string right after Seq/T, not a Newtonsoft $type");
            Assert.That(lines.Skip(1), Has.None.Contains("\"$type\":\"FDG."), "no CLR type names in the container itself");
            Assert.That(lines[11], Does.Contain("\"Marker\":\"GameStart\""), "enums are written by name");
            Assert.That(lines[11], Does.Not.Contain("\"Round\""), "unset marker fields are omitted");
            Assert.That(lines[9], Does.Not.Contain("\"Debug\""), "a non-debug log line omits the flag");
        }

        [Test]
        public void EmbeddedJsonAndDateLikeStrings_SurviveVerbatim()
        {
            string tricky = "{\"a\":\"line1\\nline2\",\"b\":1.10}";
            var events = new List<ReplayEvent>
            {
                new LogEvent("2026-10-30T12:00:00Z") { Seq = 0, T = 0 },
                new StoreUpdateEvent(Ref(1, 0, 1), tricky) { Seq = 1, T = 1 },
            };

            ReplayDocument read = Read(Write(MakeHeader(), events));

            Assert.That(((LogEvent)read.Events[0]).Line, Is.EqualTo("2026-10-30T12:00:00Z"),
                "a date-shaped string must not be re-parsed and re-formatted");
            Assert.That(((StoreUpdateEvent)read.Events[1]).Json, Is.EqualTo(tricky));
        }

        [Test]
        public void Reader_RefusesTheNextFormatVersion()
        {
            ReplayHeader future = MakeHeader();
            future.FormatVersion = ReplayFormat.CurrentVersion + 1;
            byte[] bytes = Write(future, OneOfEachKind());

            ReplayFormatException? refused = Assert.Throws<ReplayFormatException>(() => Read(bytes));
            Assert.That(refused!.Message, Is.EqualTo(
                $"Replay format {ReplayFormat.CurrentVersion + 1}; this build reads {ReplayFormat.CurrentVersion}."));
        }

        [Test]
        public void Reader_RefusesDataThatIsNotGzip()
        {
            byte[] plain = Encoding.UTF8.GetBytes("{\"FormatVersion\":1}\n");

            Assert.Throws<ReplayFormatException>(() => Read(plain));
        }

        [Test]
        public void Reader_RefusesAnEmptyReplay()
        {
            Assert.Throws<ReplayFormatException>(() => Read(Gzip("")));
        }

        [Test]
        public void Reader_RefusesAnUnknownKind_NamingTheLine()
        {
            string text = "{\"FormatVersion\":1}\n{\"Seq\":0,\"T\":0,\"Kind\":\"Teleport\"}\n";

            ReplayFormatException? refused = Assert.Throws<ReplayFormatException>(() => Read(Gzip(text)));
            Assert.That(refused!.Message, Does.Contain("line 2").And.Contain("Teleport"));
        }

        [Test]
        public void Reader_RefusesAMissingKind()
        {
            string text = "{\"FormatVersion\":1}\n{\"Seq\":0,\"T\":0,\"Line\":\"x\"}\n";

            Assert.Throws<ReplayFormatException>(() => Read(Gzip(text)));
        }

        [Test]
        public void ReplayFile_ExtensionMirrorsTheSaveFile()
        {
            Assert.That(ReplayFile.EXTENSION_NO_PERIOD, Is.EqualTo("fdgreplay"));
            Assert.That(ReplayFile.EXTENSION_WITH_PERIOD, Is.EqualTo(".fdgreplay"));
        }
    }
}
