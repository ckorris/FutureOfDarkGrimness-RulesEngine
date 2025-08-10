using System.Numerics;
using FDG.TempVisuals.Messages;
using FDG.Network.Connection;

namespace FDG.TempVisuals
{
    public class NetworkedTempVisualDrawer : ITempVisualDrawer
    {
        private readonly ICommandDispatcher _dispatcher;
        private readonly ConnectionID _connectionID;


        public NetworkedTempVisualDrawer(ICommandDispatcher dispatcher, ConnectionID connectionID)
        {
            _dispatcher = dispatcher;
            _connectionID = connectionID;
        }

        public void AddVisual(ITempVisual sourceVisual)
        {
            //Slightly dirty but optimized to avoid reallocation if not necessary.
            TempVisual visualToSend;
            if (sourceVisual is TempVisual castedVisual)
            {
                visualToSend = castedVisual;
            }
            else
            {
                visualToSend = new TempVisual(sourceVisual);
            }

            var addMessage = new AddTempVisualMessage(visualToSend);
            _dispatcher.SendCommandAsync(addMessage);
        }

        public void UpdateVisual(ITempVisual visual)
        {
            throw new NotImplementedException();
        }

        public void UpdateVisualTransform(Guid tempVisualID, Position position, Quaternion rotation, Vector3 scale)
        {
            throw new NotImplementedException();
        }

        public void RemoveVisual(Guid tempVisualID)
        {
            throw new NotImplementedException();
        }

        public void ClearAllVisuals()
        {
            throw new NotImplementedException();
        }



        /*
        private void TestDrawer()
        {
            System.Diagnostics.Debug.WriteLine("Testing drawing something in TempVisualRelayer.");
            var meshProvider = new BuiltInObjMeshProvider(BuiltInAssetHelper.SILLYMANMODEL_PATH);
            var textureProvider = new BuiltInPngTextureProvider(BuiltInAssetHelper.SILLYMANTEXTURE_PATH);

            var materialProvider = new BasicMaterial();
            materialProvider.BaseColor = new Vector4(1, 1, 1, 1);
            materialProvider.BaseColorTexture = textureProvider;

            Position position = new Position(20f, 20f);
            Quaternion rotation = new Quaternion(Vector3.UnitY, 0f);
            Vector3 scale = new Vector3(1, 1, 1);

            TempVisual tempVisual = new TempVisual(meshProvider, materialProvider, position, rotation, scale);

            _visualDrawer.AddVisual(tempVisual);
        }
        */
    }
}
