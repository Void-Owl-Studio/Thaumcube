using System.Collections.Concurrent;
using VoxelGame.Shared.Messages;

namespace VoxelGame.Shared.Transport;

public sealed class LocalLoopbackTransport : ITransport
{
    private readonly ConcurrentQueue<GameMessage> _serverInbox = new();
    private readonly ConcurrentQueue<GameMessage> _clientInbox = new();

    public void SendToServer(GameMessage message)
    {
        _serverInbox.Enqueue(message);
    }

    public void SendToClient(GameMessage message)
    {
        _clientInbox.Enqueue(message);
    }

    public bool TryReceiveForServer(out GameMessage message)
    {
        return _serverInbox.TryDequeue(out message!);
    }

    public bool TryReceiveForClient(out GameMessage message)
    {
        return _clientInbox.TryDequeue(out message!);
    }
}
