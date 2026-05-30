using System.Numerics;

namespace VoxelGame.Shared.Messages;

public sealed record PlayerPositionMessage(Vector3 Position, bool Immediate = false) : GameMessage;
