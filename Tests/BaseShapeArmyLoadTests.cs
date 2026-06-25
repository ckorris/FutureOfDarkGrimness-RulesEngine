using System.Linq;
using System.Text.Json;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #149 slice B: the per-unit base authored in a .fdgarmy flows into the unit's live models, and a
    // pre-#149 file (no base) still loads as the default 28mm circle.
    [TestFixture]
    public class BaseShapeArmyLoadTests
    {
        private const float Tol = 0.0001f;

        // --- Army-file serialization -----------------------------------------------------------------

        [Test]
        public void ArmyFile_RectangleBase_RoundTrips()
        {
            ArmyListFile army = new ArmyListFile
            {
                Name = "T", Faction = "F", PointsLimit = 100,
                Units = new()
                {
                    new UnitFileEntry
                    {
                        Name = "Cavalry", ModelCount = 3, Quality = 4, Defense = 4, PointCost = 60,
                        Base = new BaseFileEntry
                        {
                            Shape = EBaseShapeKind.Rectangle, WidthInches = 0.9842520f, HeightInches = 1.9685040f,
                        },
                    },
                },
            };

            string first = JsonSerializer.Serialize(army, RuleJson.Options);
            ArmyListFile back = JsonSerializer.Deserialize<ArmyListFile>(first, RuleJson.Options)!;
            string second = JsonSerializer.Serialize(back, RuleJson.Options);

            Assert.That(second, Is.EqualTo(first), "base did not round-trip structurally.");
            BaseFileEntry b = back.Units[0].Base;
            Assert.That(b.Shape, Is.EqualTo(EBaseShapeKind.Rectangle));
            Assert.That(b.WidthInches, Is.EqualTo(0.9842520f).Within(Tol));
            Assert.That(b.HeightInches, Is.EqualTo(1.9685040f).Within(Tol));
        }

        [Test]
        public void ArmyFile_NoBaseProperty_DefaultsToCircle()
        {
            // A pre-#149 file has no "base" — the default initializer must survive deserialization.
            const string json = "{\"name\":\"Old\",\"faction\":\"F\",\"pointsLimit\":100," +
                "\"units\":[{\"name\":\"Grunts\",\"modelCount\":2,\"quality\":4,\"defense\":4,\"pointCost\":10}]}";

            ArmyListFile army = JsonSerializer.Deserialize<ArmyListFile>(json, RuleJson.Options)!;

            BaseFileEntry b = army.Units[0].Base;
            Assert.That(b, Is.Not.Null, "a missing base must default, not be null.");
            Assert.That(b.Shape, Is.EqualTo(EBaseShapeKind.Circle));
            Assert.That(b.DiameterInches, Is.EqualTo(BaseShapeDefaults.CircleDiameterInches).Within(Tol));
        }

        // --- BaseFileEntry → IBaseShape --------------------------------------------------------------

        [Test]
        public void ToBaseShape_Circle_UsesHalfTheDiameter()
        {
            IBaseShape shape = new BaseFileEntry { Shape = EBaseShapeKind.Circle, DiameterInches = 2f }.ToBaseShape();
            Assert.That(shape, Is.TypeOf<CircleBase>());
            Assert.That(((CircleBase)shape).RadiusInches, Is.EqualTo(1f).Within(Tol));
        }

        // --- Army-load: file entry → live models -----------------------------------------------------

        [Test]
        public void UnitData_RectangleBase_AllModelsGetTheRectangle()
        {
            UnitData unit = BuildUnit(new BaseFileEntry
            {
                Shape = EBaseShapeKind.Rectangle, WidthInches = 1f, HeightInches = 2f,
            }, modelCount: 3);

            Assert.That(unit.Models, Has.Count.EqualTo(3));
            foreach (IModel model in unit.Models)
            {
                Assert.That(model.BaseShape, Is.TypeOf<RectangleBase>());
                RectangleBase rect = (RectangleBase)model.BaseShape;
                Assert.That(rect.WidthInches, Is.EqualTo(1f).Within(Tol));
                Assert.That(rect.HeightInches, Is.EqualTo(2f).Within(Tol));
            }
        }

        [Test]
        public void UnitData_DefaultBase_ModelsGet28mmCircle()
        {
            // A UnitFileEntry built without setting Base uses the default circle.
            UnitData unit = BuildUnit(new BaseFileEntry(), modelCount: 2);

            foreach (IModel model in unit.Models)
            {
                Assert.That(model.BaseShape, Is.TypeOf<CircleBase>());
                Assert.That(((CircleBase)model.BaseShape).RadiusInches,
                    Is.EqualTo(BaseShapeDefaults.CircleRadiusInches).Within(Tol));
            }
        }

        private static UnitData BuildUnit(BaseFileEntry baseEntry, int modelCount)
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            UnitFileEntry entry = new UnitFileEntry
            {
                Name = "U", ModelCount = modelCount, Quality = 4, Defense = 4, Base = baseEntry,
            };
            return new UnitData(new PlayerID(System.Guid.NewGuid()), entry, store);
        }
    }
}
