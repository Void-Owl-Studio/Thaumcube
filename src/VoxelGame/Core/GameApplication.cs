using Silk.NET.Maths;
using Silk.NET.Windowing;
using VoxelGame.Input;
using VoxelGame.Player;
using VoxelGame.Rendering;
using VoxelGame.Rendering.Vulkan;
using VoxelGame.World;
using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;
using VoxelGame.World.Items;
using Silk.NET.Input;

namespace VoxelGame.Core;

public sealed class GameApplication : IDisposable
{
    private readonly GameSettings _settings = new();
    private readonly IWindow _window;
    private readonly InputManager _input = new();
    private readonly Hotbar _hotbar;
    private readonly VulkanRenderer _renderer;
    private readonly FrameTimer _timer = new();
    private readonly HudState _hud = new();
    private readonly double? _autoCloseAfterSeconds;
    private VoxelWorld? _world;
    private FirstPersonPlayer? _player;
    private DroppedBlockManager? _drops;
    private GameMode _mode = GameMode.MainMenu;
    private int _menuSelectedIndex;
    private int _selectedRenderDistance;
    private VoxelRaycastHit? _currentBreakTarget;
    private float _breakProgressSeconds;
    private double _runningSeconds;
    private bool _disposedRuntime;
    private const float BlockBreakSeconds = 0.65f;

    public GameApplication(double? autoCloseAfterSeconds = null)
    {
        _autoCloseAfterSeconds = autoCloseAfterSeconds;
        var options = WindowOptions.DefaultVulkan;
        options.Title = _settings.WindowTitle;
        options.Size = new Vector2D<int>(_settings.WindowWidth, _settings.WindowHeight);
        options.VSync = true;

        _window = Window.Create(options);
        _selectedRenderDistance = _settings.RenderDistanceChunks;
        _hotbar = Hotbar.CreateEmpty();
        _renderer = new VulkanRenderer(_settings);

        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Resize += size => _renderer.Resize(size.X, size.Y);
        _window.Closing += OnClosing;
    }

    public void Run() => _window.Run();

    private void OnLoad()
    {
        _input.Attach(_window);
        _input.SetCursorCaptured(false);
        _renderer.Initialize(_window);
        UpdateHud();
    }

    private void OnUpdate(double deltaSeconds)
    {
        var dt = (float)deltaSeconds;
        _runningSeconds += deltaSeconds;
        _timer.Update(deltaSeconds);
        _input.UpdateFrame();

        if (_autoCloseAfterSeconds is { } closeAfter && _runningSeconds >= closeAfter)
        {
            _window.Close();
            return;
        }

        if (_mode == GameMode.MainMenu)
        {
            UpdateMainMenu();
            UpdateHud();
            return;
        }

        if (_input.ExitRequested)
        {
            _mode = GameMode.MainMenu;
            _input.SetCursorCaptured(false);
            ResetBreaking();
            UpdateHud();
            return;
        }

        if (_world is null || _player is null || _drops is null)
        {
            return;
        }

        if (_hotbar.UpdateSelection(_input))
        {
            ResetBreaking();
        }

        _player.Update(dt, _input);
        _world.LoadAround(_player.Position);

        HandleBlockInteraction(dt);
        _drops.Update(dt, _player.Position, _hotbar);

        _world.RebuildDirtyMeshes();
        UpdateHud();
    }

    private void UpdateMainMenu()
    {
        if (_input.IsKeyPressedThisFrame(Key.Up) || _input.IsKeyPressedThisFrame(Key.W))
        {
            _menuSelectedIndex = (_menuSelectedIndex + 2) % 3;
        }

        if (_input.IsKeyPressedThisFrame(Key.Down) || _input.IsKeyPressedThisFrame(Key.S))
        {
            _menuSelectedIndex = (_menuSelectedIndex + 1) % 3;
        }

        if (_menuSelectedIndex == 1)
        {
            if (_input.IsKeyPressedThisFrame(Key.Left) || _input.IsKeyPressedThisFrame(Key.A))
            {
                _selectedRenderDistance = Math.Max(_settings.MinRenderDistanceChunks, _selectedRenderDistance - 1);
            }

            if (_input.IsKeyPressedThisFrame(Key.Right) || _input.IsKeyPressedThisFrame(Key.D))
            {
                _selectedRenderDistance = Math.Min(_settings.MaxRenderDistanceChunks, _selectedRenderDistance + 1);
            }
        }

        if (_input.IsKeyPressedThisFrame(Key.Enter) || _input.IsKeyPressedThisFrame(Key.Space))
        {
            if (_menuSelectedIndex == 0)
            {
                StartGame(_selectedRenderDistance);
            }
            else if (_menuSelectedIndex == 2)
            {
                _window.Close();
            }
        }

        if (_input.ExitRequested)
        {
            _window.Close();
        }
    }

    private void StartGame(int renderDistance)
    {
        _hotbar.Clear();
        _world = new VoxelWorld(_settings.WorldSeed, renderDistance);
        _player = new FirstPersonPlayer(_world, _settings.MouseSensitivity);
        _drops = new DroppedBlockManager(_world);

        _world.LoadAround(_player.Position);
        _player.SpawnAt(_world.FindSpawnPosition(0, 0));
        _world.LoadAround(_player.Position);
        _world.RebuildDirtyMeshes();

        _mode = GameMode.Playing;
        _input.SetCursorCaptured(true);
        ResetBreaking();
    }

    private void HandleBlockInteraction(float dt)
    {
        if (_world is null || _player is null || _drops is null)
        {
            return;
        }

        var ray = _player.CreateLookRay();
        var hit = VoxelRaycaster.Raycast(_world, ray, 6.0f);

        _hud.TargetedBlock = hit?.BlockType ?? BlockType.Air;
        _hud.BreakProgress = 0f;

        if (hit is null)
        {
            ResetBreaking();
            return;
        }

        if (_input.BreakHeld && hit.Value.BlockType != BlockType.Air)
        {
            if (_currentBreakTarget?.BlockPosition != hit.Value.BlockPosition)
            {
                _currentBreakTarget = hit.Value;
                _breakProgressSeconds = 0f;
            }

            _breakProgressSeconds += dt;
            _hud.BreakProgress = Math.Clamp(_breakProgressSeconds / BlockBreakSeconds, 0f, 1f);

            if (_breakProgressSeconds >= BlockBreakSeconds)
            {
                var blockPosition = hit.Value.BlockPosition;
                var brokenBlock = hit.Value.BlockType;
                _world.SetBlock(blockPosition.X, blockPosition.Y, blockPosition.Z, BlockType.Air);
                _drops.Spawn(brokenBlock, blockPosition, _player.Camera.Forward * 1.6f + new System.Numerics.Vector3(0, 2.2f, 0));
                ResetBreaking();
            }
        }
        else
        {
            ResetBreaking();
        }

        if (_input.PlacePressedThisFrame)
        {
            var place = hit.Value.BlockPosition + hit.Value.FaceNormal;
            var selected = _hotbar.SelectedBlock;
            if (selected != BlockType.Air && !_player.Body.IntersectsBlock(place.X, place.Y, place.Z))
            {
                _world.SetBlock(place.X, place.Y, place.Z, selected);
                _hotbar.TryConsumeSelected();
            }
        }
    }

    private void OnRender(double deltaSeconds)
    {
        var meshes = BuildSceneMeshes();
        var camera = _player?.Camera ?? new CameraState(new System.Numerics.Vector3(0, 64, -4), 0, 0);
        var scene = new RenderScene(meshes, camera, _hotbar, _hud);
        _renderer.Render(scene);
    }

    private IEnumerable<ChunkRenderMesh> BuildSceneMeshes()
    {
        if (_world is null)
        {
            return [];
        }

        var meshes = _world.GetVisibleMeshes().ToList();

        if (_drops is not null)
        {
            meshes.AddRange(_drops.BuildRenderMeshes());
        }

        if (_currentBreakTarget is { } breakTarget && _hud.BreakProgress > 0f)
        {
            var stage = Math.Clamp((int)MathF.Floor(_hud.BreakProgress * 10f), 0, 9);
            meshes.Add(ChunkMeshBuilder.BuildBreakOverlayMesh(breakTarget.BlockPosition, breakTarget.FaceNormal, stage));
        }

        return meshes;
    }

    private void UpdateHud()
    {
        _hud.Fps = _timer.Fps;
        _hud.PlayerPosition = _player?.Position ?? default;
        _hud.SelectedSlot = _hotbar.SelectedIndex;
        _hud.SelectedBlock = _hotbar.SelectedBlock;
        _hud.LoadedChunks = _world?.LoadedChunkCount ?? 0;
        _hud.VisibleChunkMeshes = _world?.VisibleMeshCount ?? 0;
        _hud.ShowMainMenu = _mode == GameMode.MainMenu;
        _hud.MainMenuSelectedIndex = _menuSelectedIndex;
        _hud.MainMenuRenderDistance = _selectedRenderDistance;
    }

    private void ResetBreaking()
    {
        _currentBreakTarget = null;
        _breakProgressSeconds = 0f;
        _hud.BreakProgress = 0f;
    }

    private void OnClosing()
    {
        DisposeRuntime();
    }

    public void Dispose()
    {
        DisposeRuntime();
        _window.Dispose();
    }

    private void DisposeRuntime()
    {
        if (_disposedRuntime)
        {
            return;
        }

        _renderer.Dispose();
        _input.Dispose();
        _disposedRuntime = true;
    }

    private enum GameMode
    {
        MainMenu,
        Playing
    }
}
