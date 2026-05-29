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
using VoxelGame.World.Storage;
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
    private WorldSaveStore? _activeWorldStore;
    private VoxelWorld? _world;
    private FirstPersonPlayer? _player;
    private DroppedBlockManager? _drops;
    private GameMode _mode = GameMode.MainMenu;
    private int _menuSelectedIndex;
    private int _selectedRenderDistance;
    private string? _loadableWorldName;
    private string _newWorldName = string.Empty;
    private VoxelRaycastHit? _currentBreakTarget;
    private float _breakProgressSeconds;
    private double _runningSeconds;
    private bool _disposedRuntime;
    private const float BlockBreakSeconds = 0.65f;
    private const int LoadWorldMenuIndex = 0;
    private const int NewWorldMenuIndex = 1;
    private const int RenderDistanceMenuIndex = 2;
    private const int ExitMenuIndex = 3;

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
        RefreshWorldMenuState();

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
            ReturnToMainMenu();
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

        var scrollSteps = -(int)MathF.Sign(_input.ScrollDeltaY);
        if (_hotbar.CycleSelection(scrollSteps))
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
        var hoveredMenuItem = HudLayout.HitTestMainMenu(
            _input.MousePosition,
            _window.Size.X,
            _window.Size.Y,
            _hud.MainMenuLoadWorldLabel,
            _hud.MainMenuNewWorldLabel,
            _selectedRenderDistance);
        if (hoveredMenuItem.HasValue)
        {
            _menuSelectedIndex = hoveredMenuItem.Value;
        }

        if (_input.IsKeyPressedThisFrame(Key.Up) || _input.IsKeyPressedThisFrame(Key.W))
        {
            _menuSelectedIndex = (_menuSelectedIndex + 3) % 4;
        }

        if (_input.IsKeyPressedThisFrame(Key.Down) || _input.IsKeyPressedThisFrame(Key.S))
        {
            _menuSelectedIndex = (_menuSelectedIndex + 1) % 4;
        }

        if (_menuSelectedIndex == RenderDistanceMenuIndex)
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
            if (_menuSelectedIndex == LoadWorldMenuIndex)
            {
                LoadLatestWorldOrCreate();
            }
            else if (_menuSelectedIndex == NewWorldMenuIndex)
            {
                StartNewWorld();
            }
            else if (_menuSelectedIndex == ExitMenuIndex)
            {
                _window.Close();
            }
        }

        if (_input.LeftPressedThisFrame && hoveredMenuItem.HasValue)
        {
            HandleMainMenuClick(hoveredMenuItem.Value, _input.MousePosition.X);
        }

        if (_input.ExitRequested)
        {
            _window.Close();
        }
    }

    private void StartGame(WorldSaveStore worldStore)
    {
        _hotbar.Clear();
        _activeWorldStore = worldStore;
        _world = new VoxelWorld(worldStore.Metadata.Seed, _selectedRenderDistance, worldStore);
        _player = new FirstPersonPlayer(_world, _settings.MouseSensitivity);
        _drops = new DroppedBlockManager(_world);

        var playerSave = worldStore.LoadPlayer();
        if (playerSave is not null)
        {
            _player.SpawnAt(playerSave.Position, playerSave.YawDegrees, playerSave.PitchDegrees);
        }
        else
        {
            _player.SpawnAt(_world.FindSpawnPosition(0, 0));
        }

        _world.LoadAround(_player.Position);
        _world.RebuildDirtyMeshes();
        worldStore.Touch();

        _mode = GameMode.Playing;
        _input.SetCursorCaptured(true);
        ResetBreaking();
    }

    private void HandleMainMenuClick(int menuIndex, float mouseX)
    {
        _menuSelectedIndex = menuIndex;

        if (menuIndex == LoadWorldMenuIndex)
        {
            LoadLatestWorldOrCreate();
            return;
        }

        if (menuIndex == NewWorldMenuIndex)
        {
            StartNewWorld();
            return;
        }

        if (menuIndex == RenderDistanceMenuIndex)
        {
            var layout = HudLayout.BuildMainMenu(
                _window.Size.X,
                _window.Size.Y,
                _hud.MainMenuLoadWorldLabel,
                _hud.MainMenuNewWorldLabel,
                _selectedRenderDistance);
            var bounds = layout.Items[menuIndex].Bounds;
            var midX = bounds.X + bounds.Width / 2f;
            if (mouseX < midX)
            {
                _selectedRenderDistance = Math.Max(_settings.MinRenderDistanceChunks, _selectedRenderDistance - 1);
            }
            else
            {
                _selectedRenderDistance = Math.Min(_settings.MaxRenderDistanceChunks, _selectedRenderDistance + 1);
            }

            return;
        }

        if (menuIndex == ExitMenuIndex)
        {
            _window.Close();
        }
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
        var renderDistance = _world?.RenderDistanceChunks ?? _selectedRenderDistance;
        var scene = new RenderScene(meshes, camera, _hotbar, _hud, renderDistance);
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

        if (_player is not null)
        {
            meshes.AddRange(SkyMeshBuilder.Build(_player.Camera, _settings));
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
        SaveActiveWorld();
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

    private void LoadLatestWorldOrCreate()
    {
        if (string.IsNullOrWhiteSpace(_loadableWorldName))
        {
            StartNewWorld();
            return;
        }

        StartGame(WorldSaveStore.Open(_settings.WorldsRootPath, _loadableWorldName));
    }

    private void StartNewWorld()
    {
        var worldName = EnsureUniqueWorldName();
        StartGame(WorldSaveStore.CreateNew(_settings.WorldsRootPath, worldName, _settings.WorldSeed));
    }

    private void ReturnToMainMenu()
    {
        SaveActiveWorld();
        _activeWorldStore = null;
        _world = null;
        _player = null;
        _drops = null;
        _hotbar.Clear();
        _mode = GameMode.MainMenu;
        _input.SetCursorCaptured(false);
        ResetBreaking();
        RefreshWorldMenuState();
    }

    private void SaveActiveWorld()
    {
        if (_activeWorldStore is null || _world is null)
        {
            return;
        }

        _world.SaveWorldState();
        if (_player is not null)
        {
            _activeWorldStore.SavePlayer(_player.CreateSaveData());
        }
    }

    private void RefreshWorldMenuState()
    {
        var worlds = WorldCatalog.ListWorlds(_settings.WorldsRootPath);
        _loadableWorldName = worlds.FirstOrDefault()?.Name;
        _newWorldName = EnsureUniqueWorldName(worlds.Select(world => world.Name));
        _menuSelectedIndex = Math.Clamp(_menuSelectedIndex, 0, ExitMenuIndex);

        _hud.MainMenuLoadWorldLabel = _loadableWorldName is null
            ? "LOAD WORLD NONE"
            : $"LOAD WORLD {_loadableWorldName.ToUpperInvariant()}";
        _hud.MainMenuNewWorldLabel = $"NEW WORLD {_newWorldName.ToUpperInvariant()}";
    }

    private string EnsureUniqueWorldName()
    {
        return EnsureUniqueWorldName(WorldCatalog.ListWorlds(_settings.WorldsRootPath).Select(world => world.Name));
    }

    private static string EnsureUniqueWorldName(IEnumerable<string> existingWorldNames)
    {
        var existing = new HashSet<string>(existingWorldNames, StringComparer.OrdinalIgnoreCase);
        var baseName = WorldCatalog.CreateNewWorldName();
        var candidate = baseName;
        var suffix = 1;

        while (existing.Contains(candidate))
        {
            candidate = $"{baseName}{suffix}";
            suffix++;
        }

        return candidate;
    }

    private enum GameMode
    {
        MainMenu,
        Playing
    }
}
