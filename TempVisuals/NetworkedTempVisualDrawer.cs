using System.Numerics;
using FDG.TempVisuals.Messages;
using FDG.Network.Connection;
using System.Drawing;

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
            _dispatcher.SendCommandToAllAsync(addMessage);
        }

        public void UpdateVisualTransform(Guid tempVisualID, Position position, Quaternion rotation, Vector3 scale)
        {
            var message = new UpdateTempVisualTransformMessage(tempVisualID, position, rotation, scale);
            _dispatcher.SendCommandToAllAsync(message);
        }

        public void UpdateVisualColor(Guid tempVisualID, Color color)
        {
            var message = new UpdateTempVisualColorMessage(tempVisualID, color);
            _dispatcher.SendCommandToAllAsync(message);
        }

        public void RemoveVisual(Guid tempVisualID)
        {
            var message = new RemoveTempVisualMessage(tempVisualID);
            _dispatcher.SendCommandToAllAsync(message);
        }

        public void ClearAllVisuals()
        {
            var message = new ClearAllTempVisualsMessage();
            _dispatcher.SendCommandToAllAsync(message);
        }
    }
}
