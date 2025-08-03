using System.Numerics;

using FDG.BuiltInAssets;
using FDG.SerializableVisuals.Meshes;
using FDG.SerializableVisuals.Textures;
using FDG.SerializableVisuals.Materials;

namespace FDG.TempVisuals
{
    public class TempVisualRelayer
    {
        private readonly ITempVisualDrawer _visualDrawer;

        public TempVisualRelayer(ITempVisualDrawer visualDrawer)
        {
            _visualDrawer = visualDrawer;

            TestDrawer(); //Temp.
        }

        private void TestDrawer()
        {
            System.Diagnostics.Debug.WriteLine("Testing drawing something in TempVisualRelayer.");
            var meshProvider = new BuiltInObjMeshProvider(BuiltInAssetHelper.SILLYMANMODEL_PATH);
            var textureProvider = new BuiltInPngTextureProvider(BuiltInAssetHelper.SILLYMANTEXTURE_PATH);

            var materialProvider = new BasicMaterial();
            materialProvider.BaseColor = new Vector4(1, 0, 0, 255);
            materialProvider.BaseColorTexture = textureProvider;

            Position position = new Position(20f, 20f);

            TempVisual tempVisual = new TempVisual(meshProvider, materialProvider, position);

            _visualDrawer.AddVisual(tempVisual);
        }
    }
}
