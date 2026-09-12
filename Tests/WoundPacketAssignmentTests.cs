using FDG.Data;
using FDG.Players;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FDG.Tests
{
    // #401: the wound queue is a list of packets, and AssignWoundsResults commits them - so the ordering
    // rules AND Deadly's no-carry-over are the engine's to enforce, never a resolver's. These pin the
    // packet arithmetic directly on the results object; the stage-level behaviour (Deadly emitting clumps,
    // Regeneration per clump) is covered in WoundRuleIntegrationTests.
    [TestFixture]
    public class WoundPacketAssignmentTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // The plain volley: one unconfined packet drains exactly as the old scalar did - the touched
        // model is filled to death, the next takes the remainder, nothing is lost.
        [Test]
        public void UnconfinedPacket_DrainsLikeThePlainPool()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, woundsPerModel: 3);
            var results = new AssignWoundsResults(unit, new[] { WoundPacket.Unconfined(5f) });

            Assert.That(results.TryAddWounds(results.PendingWounds[0].Model), Is.True);
            Assert.That(results.PendingWounds[0].Wounds, Is.EqualTo(3f));
            Assert.That(results.IsFinishedAssigning, Is.False, "2 of the 5 are still queued");
            Assert.That(results.TryAddWounds(results.PendingWounds[1].Model), Is.True);
            Assert.That(results.PendingWounds[1].Wounds, Is.EqualTo(2f));
            Assert.That(results.IsFinishedAssigning, Is.True);
            Assert.That(results.WoundsLost, Is.EqualTo(0f));
            Assert.That(results.TotalWoundsToAssign, Is.EqualTo(5f));
        }

        // A clump meeting a nearly-dead model: the mandatory pre-assignment (#023) puts it there, one
        // wound fits, two are lost - and that is the end of the assignment, nothing for the player.
        [Test]
        public void ConfinedPacket_OnAnAlreadyWoundedModel_LandsWhatFitsAndLosesTheRest()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 2, woundsPerModel: 3);
            PreWound(unit, modelIndex: 0, wounds: 2);
            var results = new AssignWoundsResults(unit, new[] { WoundPacket.Clump(3f) });

            Assert.That(results.PendingWounds[0].Wounds, Is.EqualTo(1f), "the clump is confined to the wounded model");
            Assert.That(results.PendingWounds[1].Wounds, Is.EqualTo(0f), "nothing carries to the fresh one");
            Assert.That(results.WoundsLost, Is.EqualTo(2f));
            Assert.That(results.TotalAssignedWounds, Is.EqualTo(1f));
            Assert.That(results.IsFinishedAssigning, Is.True);
            Assert.That(results.HasRemainingChoice, Is.False);
        }

        // A clump smaller than the model leaves it mid-fill on purpose, and #024 then forces the NEXT
        // clump onto the same model: Deadly(2) x2 into Tough(3) kills one model (3 land, 1 lost) rather
        // than chipping two.
        [Test]
        public void ConfinedPacket_SmallerThanTheModel_ForcesTheNextClumpOntoIt()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 2, woundsPerModel: 3);
            var results = new AssignWoundsResults(unit, new[] { WoundPacket.Clump(2f), WoundPacket.Clump(2f) });

            Assert.That(results.TryAddWounds(results.PendingWounds[0].Model), Is.True);
            Assert.That(results.PendingWounds[0].Wounds, Is.EqualTo(2f));
            Assert.That(results.CanAssignWoundTo(results.PendingWounds[1]), Is.False,
                "a fresh model cannot be started while another is mid-fill");
            Assert.That(results.TryAddWounds(results.PendingWounds[1].Model), Is.False);
            Assert.That(results.TryAddWounds(results.PendingWounds[0].Model), Is.True);
            Assert.That(results.PendingWounds[0].Wounds, Is.EqualTo(3f), "the second clump finishes it...");
            Assert.That(results.WoundsLost, Is.EqualTo(1f), "...and its excess is lost, not carried");
            Assert.That(results.IsFinishedAssigning, Is.True);
        }

        // A fractional clump under the probabilistic roller: weight x what fits. 0.34 of a Deadly(3)
        // clump on a 1-wound model is 0.34 of a wound - the old scalar confinement said 1.0.
        [Test]
        public void WeightedClump_LandsItsWeightOfWhatFits()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 2);
            var results = new AssignWoundsResults(unit, new[] { WoundPacket.Clump(3f, weight: 0.34f) });

            Assert.That(results.TryAddWounds(results.PendingWounds[0].Model), Is.True);
            Assert.That(results.PendingWounds[0].Wounds, Is.EqualTo(0.34f).Within(0.0001f));
            Assert.That(results.WoundsLost, Is.EqualTo(0.68f).Within(0.0001f));
            Assert.That(results.TotalWoundsToAssign, Is.EqualTo(1.02f).Within(0.0001f), "the headline is the weighted carry");
        }

        // More clumps than the unit can absorb: AutoFill places what it can and reports the rest as
        // overkill instead of throwing (the old AutoFill faulted the game when it could not place
        // everything, so callers pre-capped the pool by hand).
        [Test]
        public void AutoFill_ReportsOverkillInsteadOfThrowing()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 2, woundsPerModel: 3);
            WoundPacket[] fourClumps = { WoundPacket.Clump(3f), WoundPacket.Clump(3f), WoundPacket.Clump(3f), WoundPacket.Clump(3f) };
            var results = new AssignWoundsResults(unit, fourClumps);

            Assert.That(results.AutoFillWouldKillEveryModel(), Is.True);
            Assert.That(results.TotalAssignedWounds, Is.EqualTo(0f), "the prediction runs on a copy");

            results.AutoFill();

            Assert.That(results.TotalAssignedWounds, Is.EqualTo(6f));
            Assert.That(results.WoundsLost, Is.EqualTo(6f), "two whole clumps had nothing left to kill");
            Assert.That(results.IsFinishedAssigning, Is.True);
        }

        [Test]
        public void AutoFillWouldKillEveryModel_IsFalseWhenAModelSurvives()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 2, woundsPerModel: 3);
            var results = new AssignWoundsResults(unit, new[] { WoundPacket.Clump(3f) });

            Assert.That(results.AutoFillWouldKillEveryModel(), Is.False);
        }

        // Takedown's single-model results: the one recipient absorbs what it can, the rest is lost -
        // no caller-side cap needed any more.
        [Test]
        public void SingleModelResults_LoseWhatTheModelCannotTake()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 2, woundsPerModel: 3);
            var results = new AssignWoundsResults(unit.GetValue().ModelBindings[1], totalWoundsToAssign: 5f);

            results.AutoFill();

            Assert.That(results.PendingWounds, Has.Count.EqualTo(1));
            Assert.That(results.TotalAssignedWounds, Is.EqualTo(3f));
            Assert.That(results.WoundsLost, Is.EqualTo(2f));
        }

        // A packet Regeneration emptied is dropped, so "next packet" is always something a click lands.
        [Test]
        public void EmptiedPackets_AreDroppedFromTheQueue()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 2, woundsPerModel: 3);
            var results = new AssignWoundsResults(unit,
                new[] { WoundPacket.Clump(3f).WithWounds(0f), WoundPacket.Clump(3f).WithWounds(2f) });

            Assert.That(results.Packets, Has.Count.EqualTo(1));
            Assert.That(results.NextPacket!.Wounds, Is.EqualTo(2f));
        }

        // The predictive twin agrees with the interactive object on landed and lost, across the shapes
        // that matter: pre-wounded model, clumps mixed with a plain pool (Shred after Deadly), weights,
        // and a joined hero ordered last.
        [TestCaseSource(nameof(SimulateCases))]
        public void Simulate_MatchesAutoFill(string label, int[] toughs, int[] preWounds, bool hero, WoundPacket[] packets)
        {
            DataBinding<UnitData> unit = hero ? MakeHeroUnit(toughs) : MakeUnit(toughs);
            for (int i = 0; i < preWounds.Length; i++)
                if (preWounds[i] > 0) PreWound(unit, i, preWounds[i]);

            var results = new AssignWoundsResults(unit, packets);
            results.AutoFill();
            float simulated = WoundAllocation.Simulate(packets, WoundAllocation.Order(unit.GetValue()), out float lost);

            Assert.That(simulated, Is.EqualTo(results.TotalAssignedWounds).Within(0.0001f), $"{label}: landed");
            Assert.That(lost, Is.EqualTo(results.WoundsLost).Within(0.0001f), $"{label}: lost");
        }

        private static IEnumerable<TestCaseData> SimulateCases()
        {
            yield return new TestCaseData("two clumps, one pre-wounded", new[] { 3, 3, 3 }, new[] { 0, 0, 2 }, false,
                new[] { WoundPacket.Clump(3f), WoundPacket.Clump(3f) });
            yield return new TestCaseData("Deadly(2) clumps into Tough(3)", new[] { 3, 3 }, new int[2], false,
                new[] { WoundPacket.Clump(2f), WoundPacket.Clump(2f), WoundPacket.Clump(2f) });
            yield return new TestCaseData("clumps then a Shred pool", new[] { 3, 3 }, new int[2], false,
                new[] { WoundPacket.Clump(3f), WoundPacket.Unconfined(2f) });
            yield return new TestCaseData("weighted tail clump", new[] { 1, 1, 1 }, new int[3], false,
                new[] { WoundPacket.Clump(3f), WoundPacket.Clump(3f, 0.34f) });
            yield return new TestCaseData("overkill", new[] { 1, 1 }, new int[2], false,
                new[] { WoundPacket.Clump(3f), WoundPacket.Clump(3f), WoundPacket.Clump(3f) });
            yield return new TestCaseData("hero last", new[] { 1, 6 }, new int[2], true,
                new[] { WoundPacket.Clump(3f), WoundPacket.Clump(3f) });
        }

        // The dialogs show a placed clump as "1 -> Model 3, 2 lost", so each commit is recorded: which
        // packet, which model, what stayed, what was lost. The clump's pre-Regeneration size rides on the
        // packet for the same reason ("3 rolled, 2 ignored").
        [Test]
        public void Commits_RecordWhereEachPacketWentAndWhatItDid()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 2, woundsPerModel: 3);
            PreWound(unit, modelIndex: 1, wounds: 2);
            WoundPacket regenerated = WoundPacket.Clump(3f).WithWounds(1f);
            var results = new AssignWoundsResults(unit, new[] { WoundPacket.Clump(3f), regenerated });

            Assert.That(regenerated.OriginalWounds, Is.EqualTo(3f));
            Assert.That(regenerated.Ignored, Is.EqualTo(2f));
            Assert.That(results.Commits, Has.Count.EqualTo(1), "the pre-assignment placed clump 1");
            Assert.That(results.Commits[0].PacketIndex, Is.EqualTo(0));
            Assert.That(results.Commits[0].Model, Is.SameAs(results.PendingWounds[1].Model));
            Assert.That(results.Commits[0].Landed, Is.EqualTo(1f));
            Assert.That(results.Commits[0].Lost, Is.EqualTo(2f));

            results.TryAddWounds(results.PendingWounds[0].Model);
            Assert.That(results.Commits, Has.Count.EqualTo(2));
            Assert.That(results.Commits[1].PacketIndex, Is.EqualTo(1));
            Assert.That(results.Commits[1].Landed, Is.EqualTo(1f));
            Assert.That(results.Commits[1].Lost, Is.EqualTo(0f));
        }

        // The reply crosses the wire mid-assignment (and a save can snapshot one): the queue, its cursor
        // and the lost tally must all come back so the assignment resumes where it stopped.
        [Test]
        public void RoundTrip_PreservesTheQueueAndCursor()
        {
            DataBinding<UnitData> unit = MakeUnit(modelCount: 3, woundsPerModel: 3);
            var results = new AssignWoundsResults(unit, new[] { WoundPacket.Clump(3f), WoundPacket.Clump(2f), WoundPacket.Unconfined(1f) });
            results.TryAddWounds(results.PendingWounds[0].Model);

            JsonSerializerSettings settings = _store.GetJsonSettings();
            string json = JsonConvert.SerializeObject(results, settings);
            AssignWoundsResults revived = JsonConvert.DeserializeObject<AssignWoundsResults>(json, settings)!;

            Assert.That(revived.Packets, Has.Count.EqualTo(3));
            Assert.That(revived.PacketsCommitted, Is.EqualTo(1));
            Assert.That(revived.TotalAssignedWounds, Is.EqualTo(3f));
            Assert.That(revived.PendingWounds[0].Wounds, Is.EqualTo(3f));
            Assert.That(revived.NextPacket!.Confined, Is.True);
            Assert.That(revived.NextPacket.Wounds, Is.EqualTo(2f));
            Assert.That(revived.Commits, Has.Count.EqualTo(1), "the commit log rides along");
            Assert.That(revived.Commits[0].Landed, Is.EqualTo(3f));
            Assert.That(revived.Packets[0].OriginalWounds, Is.EqualTo(3f), "so does the pre-Regeneration size");

            revived.AutoFill();
            Assert.That(revived.TotalAssignedWounds, Is.EqualTo(6f), "the 2-clump and the pool finish the second model");
            Assert.That(revived.WoundsLost, Is.EqualTo(0f));
        }

        // --- helpers ---------------------------------------------------------------------------------

        private DataBinding<UnitData> MakeUnit(int modelCount, int woundsPerModel = 1) =>
            MakeUnit(Enumerable.Repeat(woundsPerModel, modelCount).ToArray());

        private DataBinding<UnitData> MakeUnit(int[] toughs)
        {
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit", quality: 4, defense: 4,
                modelBindings: toughs.Select(MakeModel).ToList());
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        // Grunts from every entry but the last, which joins as the Hero (#006).
        private DataBinding<UnitData> MakeHeroUnit(int[] toughs)
        {
            DataBinding<UnitData> unit = MakeUnit(toughs[..^1]);
            DataBinding<ModelData> hero = MakeModel(toughs[^1]);
            unit.GetValue().AttachHero(
                new HeroAttachment(hero.GetValue().ID, quality: 3, defense: 3, heroWounds: toughs[^1]),
                new List<DataBinding<ModelData>> { hero });
            return unit;
        }

        private DataBinding<ModelData> MakeModel(int tough)
        {
            var model = new ModelData(baseRadiusInches: 0.75f, weapons: new List<Weapon>(),
                initialPosition: new Position(0, 0), gameDataStore: _store);
            if (tough != 1) model.SetMaxWounds(tough);
            return _store.GetDataBinding<ModelData>(_store.Create(model));
        }

        private static void PreWound(DataBinding<UnitData> unit, int modelIndex, int wounds) =>
            unit.GetValue().ModelBindings[modelIndex].GetValue().DealWounds(wounds);
    }
}
