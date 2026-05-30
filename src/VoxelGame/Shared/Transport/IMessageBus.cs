using VoxelGame.Shared.Messages;

namespace VoxelGame.Shared.Transport;

public interface IMessageBus
{
    void SendToServer(GameMessage message);
    void SendToClient(GameMessage message);
    bool TryReceiveForServer(out GameMessage message);
    bool TryReceiveForClient(out GameMessage message);
}
