namespace VoxelGame.Core;

public sealed class FrameTimer
{
    private double _elapsed;
    private int _frames;

    public int Fps { get; private set; }

    public void Update(double deltaSeconds)
    {
        _elapsed += deltaSeconds;
        _frames++;

        if (_elapsed < 1.0)
        {
            return;
        }

        Fps = _frames;
        _frames = 0;
        _elapsed = 0;
    }
}
