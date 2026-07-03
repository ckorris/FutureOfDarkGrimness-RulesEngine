using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests;

// #153 launch gate (decision 9): the host-side "launch anyway?" check. Over-lobby-points on any army;
// full catalog Errors for Forge-built armies whose embedded book + selections survived the wire (which
// the round-trip test pins — the encode/decode used to flatten BuiltArmyFile to its base type).
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

        var problems = LaunchGate.ValidateArmies(
            new[] { ("Alice", (ArmyListFile?)army) }, lobbyPointsLimit: 500);

        Assert.That(problems, Is.Empty);
    }

    [Test]
    public void OverLobbyPoints_IsAProblem_EvenIfTheSavedListLimitAllowsIt()
    {
        // Saved with a generous limit; the lobby's tighter setting is authoritative at launch.
        BuiltArmyFile army = ForgeArmy(pointsLimit: 100000, units: new BuilderUnit { RosterUnitId = "warriors" });

        var problems = LaunchGate.ValidateArmies(
            new[] { ("Alice", (ArmyListFile?)army) }, lobbyPointsLimit: 50); // 65 > 50

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

        var problems = LaunchGate.ValidateArmies(
            new[] { ("Bob", (ArmyListFile?)army) }, lobbyPointsLimit: 100000);

        Assert.That(problems, Has.One.Contains("Bob").And.One.Contains("Unique"));
    }

    [Test]
    public void HandAuthoredArmy_OnlyGetsThePointsCheck()
    {
        var plain = new ArmyListFile { Name = "Hand", Units = { new UnitFileEntry { Name = "U", PointCost = 80 } } };

        Assert.That(LaunchGate.ValidateArmies(new[] { ("Cid", (ArmyListFile?)plain) }, 100), Is.Empty);
        Assert.That(LaunchGate.ValidateArmies(new[] { ("Cid", (ArmyListFile?)plain) }, 50), Has.Count.EqualTo(1));
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
}
