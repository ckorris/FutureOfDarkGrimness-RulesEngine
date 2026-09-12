using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests;

// #153/#402 launch gate: the host-side army check, in two halves. BlockingProblems (no army, over lobby
// points, wrong game system) greys LAUNCH out; OverridableProblems (full catalog Errors for Forge-built
// armies) raises the "launch anyway?" confirm. Forge armies only reach the second half when their
// embedded book + selections survive the wire, which the round-trip tests pin — the encode/decode used
// to flatten BuiltArmyFile to its base type.
[TestFixture]
public class LaunchGateTests
{
    private static BuiltArmyFile ForgeArmy(int pointsLimit = 100000, params BuilderUnit[] units)
    {
        var list = new BuilderList { Name = "L", PointsLimit = pointsLimit };
        list.Units.AddRange(units);
        return ListCompiler.Compile(DemoBook.Build(), list);
    }

    [Test]
    public void CleanArmies_NoProblems()
    {
        BuiltArmyFile army = ForgeArmy(units: new BuilderUnit { RosterUnitId = "warriors" });

        Assert.That(Blocking(("Alice", army), lobbyPointsLimit: 500), Is.Empty);
        Assert.That(LaunchGate.OverridableProblems(new[] { ("Alice", (ArmyListFile?)army) }), Is.Empty);
    }

    [Test]
    public void OverLobbyPoints_IsAProblem_EvenIfTheSavedListLimitAllowsIt()
    {
        // Saved with a generous limit; the lobby's tighter setting is authoritative at launch.
        BuiltArmyFile army = ForgeArmy(pointsLimit: 100000, units: new BuilderUnit { RosterUnitId = "warriors" });

        var problems = Blocking(("Alice", army), lobbyPointsLimit: 50); // 65 > 50

        Assert.That(problems, Has.One.Contains("over the 50 pt lobby limit"));
    }

    [Test]
    public void ForgeArmyWithCatalogError_IsAProblem_AttributedToThePlayer()
    {
        // Two copies of a Unique unit — the catalog validation Error the builder would have flagged.
        var book = DemoBook.Build();
        book.Units[0].Rules.Add(new SpecialRuleEntry_Core("Unique"));
        var list = new BuilderList { Name = "L", PointsLimit = 100000 };
        list.Units.Add(new BuilderUnit { RosterUnitId = "warriors" });
        list.Units.Add(new BuilderUnit { RosterUnitId = "warriors" });
        BuiltArmyFile army = ListCompiler.Compile(book, list);

        var problems = LaunchGate.OverridableProblems(new[] { ("Bob", (ArmyListFile?)army) });

        Assert.That(problems, Has.One.Contains("Bob").And.One.Contains("Unique"));
        // A catalog Error is overridable, never blocking — the #153 house-rules escape hatch (#402).
        Assert.That(Blocking(("Bob", army), lobbyPointsLimit: 100000), Is.Empty);
    }

    [Test]
    public void HandAuthoredArmy_OnlyGetsThePointsCheck()
    {
        var plain = new ArmyListFile { Name = "Hand", Units = { new UnitFileEntry { Name = "U", PointCost = 80 } } };

        Assert.That(LaunchGate.OverridableProblems(new[] { ("Cid", (ArmyListFile?)plain) }), Is.Empty);
        Assert.That(Blocking(("Cid", plain), lobbyPointsLimit: 100), Is.Empty);
        Assert.That(Blocking(("Cid", plain), lobbyPointsLimit: 50), Has.Count.EqualTo(1));
    }

    // ── #402: no army, and the game-system filter ────────────────────────────────────────────

    [Test]
    public void NoArmyAssigned_Blocks()
    {
        var problems = LaunchGate.BlockingProblems(
            new[] { ("Dot", (ArmyListFile?)null) }, 2000, EAllowedGameSystems.All);

        Assert.That(problems, Has.One.Contains("Dot").And.One.Contains("no army assigned"));
    }

    [Test]
    public void AllowedAll_TakesEverySystem_IncludingOneThisBuildDoesNotKnow()
    {
        foreach (string? system in new[] { null, GameSystems.GrimdarkFuture, GameSystems.AgeOfFantasy, "some-custom-thing" })
        {
            Assert.That(Blocking(("Alice", PlainArmy(system)), 2000, EAllowedGameSystems.All), Is.Empty,
                $"All should accept '{system ?? "(absent)"}'");
        }
    }

    [Test]
    public void WrongSystem_Blocks_AndNamesBothSides()
    {
        var problems = Blocking(("Alice", PlainArmy(GameSystems.AgeOfFantasy)), 2000,
            EAllowedGameSystems.GrimdarkFuture);

        Assert.That(problems, Has.One.Contains("Age of Fantasy army").And.One.Contains("Grimdark Future armies only"));
    }

    [Test]
    public void ArmyWithNoSystemField_CountsAsGrimdarkFuture()
    {
        // Every pre-#378 .fdgarmy has no gameSystem field at all; absent means GDF, so a GDF-only
        // lobby must still take all of them.
        Assert.That(Blocking(("Alice", PlainArmy(null)), 2000, EAllowedGameSystems.GrimdarkFuture), Is.Empty);
        Assert.That(Blocking(("Alice", PlainArmy(null)), 2000, EAllowedGameSystems.AgeOfFantasy),
            Has.One.Contains("Age of Fantasy armies only"));
    }

    [Test]
    public void UnderbuiltArmy_IsLegal_AndNeverBlocks()
    {
        // Deliberate (#402): 80 pts in a 2000-pt lobby is a yellow advisory in the roster, not a block.
        Assert.That(Blocking(("Cid", PlainArmy(null)), 2000, EAllowedGameSystems.All), Is.Empty);
    }

    [Test]
    public void EveryProblemIsReported_NotJustTheFirst()
    {
        ArmyListFile fat = PlainArmy(GameSystems.AgeOfFantasy, pointCost: 3000);

        var problems = Blocking(("Alice", fat), 2000, EAllowedGameSystems.GrimdarkFuture);

        Assert.That(problems, Has.Count.EqualTo(2), "points and system are separate lines");
    }

    [Test]
    public void ArmyWireRoundTrip_PreservesEmbeddedBookAndSelections()
    {
        BuiltArmyFile army = ForgeArmy(units: new BuilderUnit { RosterUnitId = "warriors" });

        var message = FDG.Network.Messages.ArmyListUpdateMessage.FromArmy(
            new PlayerID(System.Guid.NewGuid()), army);
        ArmyListFile decoded = message.DecodeArmy();

        Assert.That(decoded, Is.InstanceOf<BuiltArmyFile>());
        var built = (BuiltArmyFile)decoded;
        Assert.That(built.Book, Is.Not.Null, "the embedded book survives the wire");
        Assert.That(built.Selections, Is.Not.Null, "the embedded selections survive the wire");
        Assert.That(built.Selections!.Units.Single().RosterUnitId, Is.EqualTo("warriors"));
    }

    [Test]
    public void PlainArmyWireRoundTrip_LeavesEmbeddedDataNull()
    {
        var plain = new ArmyListFile { Name = "Hand", Units = { new UnitFileEntry { Name = "U", PointCost = 80 } } };

        var message = FDG.Network.Messages.ArmyListUpdateMessage.FromArmy(
            new PlayerID(System.Guid.NewGuid()), plain);
        ArmyListFile decoded = message.DecodeArmy();

        var built = (BuiltArmyFile)decoded;
        Assert.That(built.Book, Is.Null);
        Assert.That(built.Selections, Is.Null);
        Assert.That(decoded.Units.Single().PointCost, Is.EqualTo(80));
    }

    private static ArmyListFile PlainArmy(string? gameSystem, int pointCost = 80) => new()
    {
        Name = "Hand",
        GameSystem = gameSystem,
        Units = { new UnitFileEntry { Name = "U", PointCost = pointCost } },
    };

    private static IReadOnlyList<string> Blocking((string Name, ArmyListFile? Army) player,
        int lobbyPointsLimit, EAllowedGameSystems allowed = EAllowedGameSystems.All) =>
        LaunchGate.BlockingProblems(new[] { (player.Name, player.Army) }, lobbyPointsLimit, allowed);
}
