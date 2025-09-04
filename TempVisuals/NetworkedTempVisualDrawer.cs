using System.Numerics;
using FDG.TempVisuals.Messages;
using FDG.Network.Connection;
using System.Drawing;
using FDG.MessageBus;

namespace FDG.TempVisuals
{
    public class NetworkedTempVisualDrawer : ITempVisualDrawer
    {
        private readonly IMessageBusHost _messageBusHost;
        private readonly ConnectionID _connectionID;


        public NetworkedTempVisualDrawer(IMessageBusHost messageBusHost, ConnectionID connectionID)
        {
            _messageBusHost = messageBusHost;
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
            _messageBusHost.SendCommandToAllAsync(addMessage);
        }

        public void UpdateVisualTransform(Guid tempVisualID, Position position, Quaternion rotation, Vector3 scale)
        {
            var message = new UpdateTempVisualTransformMessage(tempVisualID, position, rotation, scale);
            _messageBusHost.SendCommandToAllAsync(message);
        }

        public void UpdateVisualColor(Guid tempVisualID, Color color)
        {
            var message = new UpdateTempVisualColorMessage(tempVisualID, color);
            _messageBusHost.SendCommandToAllAsync(message);
        }

        public void RemoveVisual(Guid tempVisualID)
        {
            var message = new RemoveTempVisualMessage(tempVisualID);
            _messageBusHost.SendCommandToAllAsync(message);
        }

        public void ClearAllVisuals()
        {
            var message = new ClearAllTempVisualsMessage();
            _messageBusHost.SendCommandToAllAsync(message);
        }
    }
}
