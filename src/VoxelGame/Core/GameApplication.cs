using Silk.NET.Input;
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
    private readonly char[] _invalidWorldNameChars = Path.GetInvalidFileNameChars();
    private readonly List<WorldMetadata> _availableWorlds = [];
    private WorldSaveStore? _activeWorldStore;
    private VoxelWorld? _world;
    private FirstPersonPlayer? _player;
    private DroppedBlockManager? _drops;
    private GameMode _mode = GameMode.MainMenu;
    private MenuScreen _menuScreen = MenuScreen.Main;
    private int _mainMenuSelectedIndex;
    private int _pauseMenuSelectedIndex;
    private int _selectedRenderDistance;
    private int _selectedWorldIndex = -1;
    private int _worldListScrollOffset;
    private int _singleplayerActionIndex;
    private int _settingsActionIndex;
    private int _createWorldActionIndex;
    private string _newWorldName = string.Empty;
    private string _menuStatusText = string.Empty;
    private MenuScreen _settingsBackScreen = MenuScreen.Main;
    private VoxelRaycastHit? _currentBreakTarget;
    private float _breakProgressSeconds;
    private double _runningSeconds;
    private bool _disposedRuntime;
    private const float BlockBreakSeconds = 0.65f;
    private const int MainMenuSingleplayerIndex = 0;
    private const int MainMenuSettingsIndex = 1;
    private const int MainMenuExitIndex = 2;
    private const int PauseMenuSettingsIndex = 0;
    private const int PauseMenuExitToMainMenuIndex = 1;
    private const int SingleplayerCreateIndex = 0;
    private const int SingleplayerLoadIndex = 1;
    private const int SingleplayerDeleteIndex = 2;
    private const int SingleplayerBackIndex = 3;
    private const int SettingsBackIndex = 0;
    private const int CreateWorldConfirmIndex = 0;
    private const int CreateWorldBackIndex = 1;
    private const int MaxWorldNameLength = 32;

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
        RefreshWorldCatalog();

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

        if (_mode is GameMode.MainMenu or GameMode.Paused)
        {
            UpdateMenu();
            UpdateHud();
            return;
        }

        if (_input.ExitRequested)
        {
            PauseGame();
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

    private void UpdateMenu()
    {
        switch (_menuScreen)
        {
            case MenuScreen.Main:
                UpdateMainMenu();
                break;
            case MenuScreen.Pause:
                UpdatePauseMenu();
                break;
            case MenuScreen.Singleplayer:
                UpdateSingleplayerMenu();
                break;
            case MenuScreen.Settings:
                UpdateSettingsMenu();
                break;
            case MenuScreen.CreateWorld:
                UpdateCreateWorldMenu();
                break;
        }
    }

    private void UpdateMainMenu()
    {
        var hoveredMenuItem = HudLayout.HitTestMainMenu(_input.MousePosition, _window.Size.X, _window.Size.Y);
        if (hoveredMenuItem.HasValue)
        {
            _mainMenuSelectedIndex = hoveredMenuItem.Value;
        }

        if (_input.IsKeyPressedThisFrame(Key.Up) || _input.IsKeyPressedThisFrame(Key.W))
        {
            _mainMenuSelectedIndex = (_mainMenuSelectedIndex + 2) % 3;
        }

        if (_input.IsKeyPressedThisFrame(Key.Down) || _input.IsKeyPressedThisFrame(Key.S))
        {
            _mainMenuSelectedIndex = (_mainMenuSelectedIndex + 1) % 3;
        }

        if (_input.IsKeyPressedThisFrame(Key.Enter) || _input.IsKeyPressedThisFrame(Key.Space))
        {
            ActivateMainMenu(_mainMenuSelectedIndex);
        }

        if (_input.LeftPressedThisFrame && hoveredMenuItem.HasValue)
        {
            ActivateMainMenu(hoveredMenuItem.Value);
        }

        if (_input.ExitRequested)
        {
            _window.Close();
        }
    }

    private void UpdateSingleplayerMenu()
    {
        var hoveredWorldIndex = HudLayout.HitTestWorldSelectionWorld(_input.MousePosition, _window.Size.X, _window.Size.Y, GetVisibleWorldNames());
        if (hoveredWorldIndex.HasValue)
        {
            _selectedWorldIndex = _worldListScrollOffset + hoveredWorldIndex.Value;
            EnsureWorldSelectionVisible();
            ClearMenuStatus();
        }

        var hoveredAction = HudLayout.HitTestWorldSelectionAction(_input.MousePosition, _window.Size.X, _window.Size.Y, GetVisibleWorldNames());
        if (hoveredAction.HasValue)
        {
            _singleplayerActionIndex = hoveredAction.Value;
        }

        if (_input.ScrollDeltaY != 0f && _availableWorlds.Count > 0)
        {
            var direction = _input.ScrollDeltaY > 0f ? -1 : 1;
            _selectedWorldIndex = Math.Clamp(_selectedWorldIndex + direction, 0, _availableWorlds.Count - 1);
            EnsureWorldSelectionVisible();
        }

        if ((_input.IsKeyPressedThisFrame(Key.Up) || _input.IsKeyPressedThisFrame(Key.W)) && _availableWorlds.Count > 0)
        {
            _selectedWorldIndex = Math.Clamp(_selectedWorldIndex - 1, 0, _availableWorlds.Count - 1);
            EnsureWorldSelectionVisible();
            ClearMenuStatus();
        }

        if ((_input.IsKeyPressedThisFrame(Key.Down) || _input.IsKeyPressedThisFrame(Key.S)) && _availableWorlds.Count > 0)
        {
            _selectedWorldIndex = Math.Clamp(_selectedWorldIndex + 1, 0, _availableWorlds.Count - 1);
            EnsureWorldSelectionVisible();
            ClearMenuStatus();
        }

        if (_input.IsKeyPressedThisFrame(Key.Left) || _input.IsKeyPressedThisFrame(Key.A))
        {
            _singleplayerActionIndex = (_singleplayerActionIndex + 3) % 4;
        }

        if (_input.IsKeyPressedThisFrame(Key.Right) || _input.IsKeyPressedThisFrame(Key.D))
        {
            _singleplayerActionIndex = (_singleplayerActionIndex + 1) % 4;
        }

        if (_input.IsKeyPressedThisFrame(Key.Enter) || _input.IsKeyPressedThisFrame(Key.Space))
        {
            ActivateSingleplayerAction(_singleplayerActionIndex);
        }

        if (_input.LeftPressedThisFrame && hoveredAction.HasValue)
        {
            ActivateSingleplayerAction(hoveredAction.Value);
        }

        if (_input.ExitRequested)
        {
            OpenMainMenu();
        }
    }

    private void UpdatePauseMenu()
    {
        var hoveredMenuItem = HudLayout.HitTestPauseMenu(_input.MousePosition, _window.Size.X, _window.Size.Y);
        if (hoveredMenuItem.HasValue)
        {
            _pauseMenuSelectedIndex = hoveredMenuItem.Value;
        }

        if (_input.IsKeyPressedThisFrame(Key.Up) || _input.IsKeyPressedThisFrame(Key.W))
        {
            _pauseMenuSelectedIndex = (_pauseMenuSelectedIndex + 1) % 2;
        }

        if (_input.IsKeyPressedThisFrame(Key.Down) || _input.IsKeyPressedThisFrame(Key.S))
        {
            _pauseMenuSelectedIndex = (_pauseMenuSelectedIndex + 1) % 2;
        }

        if (_input.IsKeyPressedThisFrame(Key.Enter) || _input.IsKeyPressedThisFrame(Key.Space))
        {
            ActivatePauseMenu(_pauseMenuSelectedIndex);
        }

        if (_input.LeftPressedThisFrame && hoveredMenuItem.HasValue)
        {
            ActivatePauseMenu(hoveredMenuItem.Value);
        }

        if (_input.ExitRequested)
        {
            ResumeGame();
        }
    }

    private void UpdateSettingsMenu()
    {
        var hoveredSlider = HudLayout.HitTestSettingsSlider(_input.MousePosition, _window.Size.X, _window.Size.Y);
        var hoveredAction = HudLayout.HitTestSettingsAction(_input.MousePosition, _window.Size.X, _window.Size.Y);
        if (hoveredAction.HasValue)
        {
            _settingsActionIndex = hoveredAction.Value;
        }

        if (_input.LeftPressedThisFrame && hoveredSlider)
        {
            var layout = HudLayout.BuildSettingsMenu(_window.Size.X, _window.Size.Y);
            _selectedRenderDistance = SliderPositionToRenderDistance(layout.SliderBounds, _input.MousePosition.X);
            ClearMenuStatus();
        }

        if (_input.IsKeyPressedThisFrame(Key.Left) || _input.IsKeyPressedThisFrame(Key.A))
        {
            _selectedRenderDistance = Math.Max(_settings.MinRenderDistanceChunks, _selectedRenderDistance - 1);
            ClearMenuStatus();
        }

        if (_input.IsKeyPressedThisFrame(Key.Right) || _input.IsKeyPressedThisFrame(Key.D))
        {
            _selectedRenderDistance = Math.Min(_settings.MaxRenderDistanceChunks, _selectedRenderDistance + 1);
            ClearMenuStatus();
        }

        if (_input.IsKeyPressedThisFrame(Key.Enter) || _input.IsKeyPressedThisFrame(Key.Space))
        {
            CloseSettingsMenu();
        }

        if (_input.LeftPressedThisFrame && hoveredAction.HasValue)
        {
            CloseSettingsMenu();
        }

        if (_input.ExitRequested)
        {
            CloseSettingsMenu();
        }
    }

    private int SliderPositionToRenderDistance(UiRect sliderBounds, float mouseX)
    {
        var clampedX = Math.Clamp(mouseX, sliderBounds.X, sliderBounds.Right);
        var range = Math.Max(1, _settings.MaxRenderDistanceChunks - _settings.MinRenderDistanceChunks);
        var normalized = (clampedX - sliderBounds.X) / Math.Max(1, sliderBounds.Width);
        var value = _settings.MinRenderDistanceChunks + (int)MathF.Round(normalized * range);
        return Math.Clamp(value, _settings.MinRenderDistanceChunks, _settings.MaxRenderDistanceChunks);
    }

    private void UpdateCreateWorldMenu()
    {
        foreach (var character in _input.TypedText)
        {
            if (_newWorldName.Length >= MaxWorldNameLength)
            {
                break;
            }

            if (!char.IsControl(character) && Array.IndexOf(_invalidWorldNameChars, character) < 0)
            {
                _newWorldName += character;
                ClearMenuStatus();
            }
        }

        if (_input.BackspacePressedThisFrame && _newWorldName.Length > 0)
        {
            _newWorldName = _newWorldName[..^1];
            ClearMenuStatus();
        }

        var hoveredAction = HudLayout.HitTestCreateWorldAction(_input.MousePosition, _window.Size.X, _window.Size.Y);
        if (hoveredAction.HasValue)
        {
            _createWorldActionIndex = hoveredAction.Value;
        }

        if (_input.IsKeyPressedThisFrame(Key.Left) || _input.IsKeyPressedThisFrame(Key.A) || _input.IsKeyPressedThisFrame(Key.Up) || _input.IsKeyPressedThisFrame(Key.W))
        {
            _createWorldActionIndex = (_createWorldActionIndex + 1) % 2;
        }

        if (_input.IsKeyPressedThisFrame(Key.Right) || _input.IsKeyPressedThisFrame(Key.D) || _input.IsKeyPressedThisFrame(Key.Down) || _input.IsKeyPressedThisFrame(Key.S))
        {
            _createWorldActionIndex = (_createWorldActionIndex + 1) % 2;
        }

        if (_input.IsKeyPressedThisFrame(Key.Enter) || _input.IsKeyPressedThisFrame(Key.Space))
        {
            ActivateCreateWorldAction(_createWorldActionIndex);
        }

        if (_input.LeftPressedThisFrame && hoveredAction.HasValue)
        {
            ActivateCreateWorldAction(hoveredAction.Value);
        }

        if (_input.ExitRequested)
        {
            OpenSingleplayerMenu();
        }
    }

    private void ActivateMainMenu(int menuIndex)
    {
        _mainMenuSelectedIndex = menuIndex;
        if (menuIndex == MainMenuSingleplayerIndex)
        {
            OpenSingleplayerMenu();
            return;
        }

        if (menuIndex == MainMenuSettingsIndex)
        {
            OpenSettingsMenu();
            return;
        }

        _window.Close();
    }

    private void ActivatePauseMenu(int menuIndex)
    {
        _pauseMenuSelectedIndex = menuIndex;
        if (menuIndex == PauseMenuSettingsIndex)
        {
            OpenSettingsMenu(MenuScreen.Pause);
            return;
        }

        ReturnToMainMenu();
    }

    private void ActivateSingleplayerAction(int actionIndex)
    {
        _singleplayerActionIndex = actionIndex;

        switch (actionIndex)
        {
            case SingleplayerCreateIndex:
                OpenCreateWorldMenu();
                return;
            case SingleplayerLoadIndex:
                LoadSelectedWorld();
                return;
            case SingleplayerDeleteIndex:
                DeleteSelectedWorld();
                return;
            case SingleplayerBackIndex:
                OpenMainMenu();
                return;
        }
    }

    private void ActivateCreateWorldAction(int actionIndex)
    {
        _createWorldActionIndex = actionIndex;
        if (actionIndex == CreateWorldConfirmIndex)
        {
            CreateWorldFromMenu();
            return;
        }

        OpenSingleplayerMenu();
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
        _hud.ShowMenu = _mode != GameMode.Playing;
        _hud.ShowPauseOverlay = _mode == GameMode.Paused;
        _hud.MenuScreen = _menuScreen;
        _hud.MainMenuSelectedIndex = _mainMenuSelectedIndex;
        _hud.PauseMenuSelectedIndex = _pauseMenuSelectedIndex;
        _hud.MenuSelectedActionIndex = _menuScreen switch
        {
            MenuScreen.CreateWorld => _createWorldActionIndex,
            MenuScreen.Settings => _settingsActionIndex,
            MenuScreen.Pause => _pauseMenuSelectedIndex,
            _ => _singleplayerActionIndex
        };
        _hud.MenuTitle = _menuScreen switch
        {
            MenuScreen.Main => "VOXELGAME",
            MenuScreen.Pause => "PAUSED",
            MenuScreen.Singleplayer => "SINGLEPLAYER",
            MenuScreen.Settings => "SETTINGS",
            MenuScreen.CreateWorld => "CREATE WORLD",
            _ => "VOXELGAME"
        };
        _hud.MenuSubtitle = _menuScreen switch
        {
            MenuScreen.Main => "ANCIENT ARCANE SANDBOX",
            MenuScreen.Pause => "THE WORLD WAITS IN ARCANE STASIS",
            MenuScreen.Singleplayer => _availableWorlds.Count == 0 ? "NO WORLDS FOUND" : "SELECT A WORLD",
            MenuScreen.Settings => _mode == GameMode.Paused ? "TUNE VIEW WHILE PAUSED" : "TUNE WORLD VIEW",
            MenuScreen.CreateWorld => "ENTER WORLD NAME",
            _ => string.Empty
        };
        _hud.MenuStatusText = _menuStatusText;
        _hud.CreateWorldName = _newWorldName;
        _hud.MenuWorldNames = GetVisibleWorldNames();
        _hud.MenuRenderDistance = _selectedRenderDistance;
        _hud.MenuSelectedWorldIndex = _selectedWorldIndex < _worldListScrollOffset
            ? -1
            : _selectedWorldIndex - _worldListScrollOffset;
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

    private void OpenMainMenu()
    {
        _menuScreen = MenuScreen.Main;
        _mainMenuSelectedIndex = Math.Clamp(_mainMenuSelectedIndex, MainMenuSingleplayerIndex, MainMenuExitIndex);
        ClearMenuStatus();
    }

    private void OpenSingleplayerMenu()
    {
        RefreshWorldCatalog();
        _menuScreen = MenuScreen.Singleplayer;
        _singleplayerActionIndex = SingleplayerCreateIndex;
        ClearMenuStatus();
    }

    private void OpenSettingsMenu()
    {
        OpenSettingsMenu(MenuScreen.Main);
    }

    private void OpenSettingsMenu(MenuScreen backScreen)
    {
        _menuScreen = MenuScreen.Settings;
        _settingsBackScreen = backScreen;
        _settingsActionIndex = SettingsBackIndex;
        ClearMenuStatus();
    }

    private void OpenCreateWorldMenu()
    {
        _menuScreen = MenuScreen.CreateWorld;
        _newWorldName = string.Empty;
        _createWorldActionIndex = CreateWorldConfirmIndex;
        ClearMenuStatus();
    }

    private void LoadSelectedWorld()
    {
        var world = GetSelectedWorld();
        if (world is null)
        {
            _menuStatusText = "SELECT A WORLD TO LOAD";
            return;
        }

        try
        {
            StartGame(WorldSaveStore.Open(_settings.WorldsRootPath, world.Name));
        }
        catch (Exception)
        {
            _menuStatusText = "FAILED TO LOAD WORLD";
            RefreshWorldCatalog();
        }
    }

    private void DeleteSelectedWorld()
    {
        var world = GetSelectedWorld();
        if (world is null)
        {
            _menuStatusText = "SELECT A WORLD TO DELETE";
            return;
        }

        try
        {
            WorldCatalog.DeleteWorld(_settings.WorldsRootPath, world.Name);
            RefreshWorldCatalog();
            _menuStatusText = $"DELETED {world.Name.ToUpperInvariant()}";
        }
        catch (Exception)
        {
            _menuStatusText = "FAILED TO DELETE WORLD";
        }
    }

    private void CreateWorldFromMenu()
    {
        var worldName = _newWorldName.Trim();
        if (string.IsNullOrWhiteSpace(worldName))
        {
            _menuStatusText = "ENTER A WORLD NAME";
            return;
        }

        if (worldName.EndsWith(' ') || worldName.EndsWith('.'))
        {
            _menuStatusText = "NAME CANNOT END WITH SPACE OR DOT";
            return;
        }

        if (worldName.IndexOfAny(_invalidWorldNameChars) >= 0)
        {
            _menuStatusText = "NAME CONTAINS INVALID CHARACTERS";
            return;
        }

        if (_availableWorlds.Any(world => string.Equals(world.Name, worldName, StringComparison.OrdinalIgnoreCase)))
        {
            _menuStatusText = "WORLD NAME ALREADY EXISTS";
            return;
        }

        try
        {
            StartGame(WorldSaveStore.CreateNew(_settings.WorldsRootPath, worldName, _settings.WorldSeed));
        }
        catch (Exception)
        {
            _menuStatusText = "FAILED TO CREATE WORLD";
            RefreshWorldCatalog();
        }
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
        OpenMainMenu();
        RefreshWorldCatalog();
    }

    private void PauseGame()
    {
        if (_mode != GameMode.Playing)
        {
            return;
        }

        _mode = GameMode.Paused;
        _input.SetCursorCaptured(false);
        ResetBreaking();
        OpenPauseMenu();
    }

    private void ResumeGame()
    {
        if (_mode != GameMode.Paused)
        {
            return;
        }

        _mode = GameMode.Playing;
        _input.SetCursorCaptured(true);
        ResetBreaking();
        ClearMenuStatus();
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

    private void RefreshWorldCatalog()
    {
        _availableWorlds.Clear();
        _availableWorlds.AddRange(WorldCatalog.ListWorlds(_settings.WorldsRootPath));

        if (_availableWorlds.Count == 0)
        {
            _selectedWorldIndex = -1;
            _worldListScrollOffset = 0;
        }
        else
        {
            _selectedWorldIndex = Math.Clamp(_selectedWorldIndex, 0, _availableWorlds.Count - 1);
            if (_selectedWorldIndex < 0)
            {
                _selectedWorldIndex = 0;
            }

            EnsureWorldSelectionVisible();
        }
    }

    private WorldMetadata? GetSelectedWorld()
    {
        if (_selectedWorldIndex < 0 || _selectedWorldIndex >= _availableWorlds.Count)
        {
            return null;
        }

        return _availableWorlds[_selectedWorldIndex];
    }

    private IReadOnlyList<string> GetVisibleWorldNames()
    {
        var maxVisible = GetVisibleWorldRowCapacity();
        if (_availableWorlds.Count == 0 || maxVisible <= 0)
        {
            return Array.Empty<string>();
        }

        var visibleCount = Math.Min(maxVisible, _availableWorlds.Count - _worldListScrollOffset);
        var worlds = new string[visibleCount];
        for (var i = 0; i < visibleCount; i++)
        {
            worlds[i] = _availableWorlds[_worldListScrollOffset + i].Name;
        }

        return worlds;
    }

    private int GetVisibleWorldRowCapacity()
    {
        return Math.Max(4, Math.Min(8, _window.Size.Y / 76));
    }

    private void EnsureWorldSelectionVisible()
    {
        if (_selectedWorldIndex < 0)
        {
            _worldListScrollOffset = 0;
            return;
        }

        var visibleCount = GetVisibleWorldRowCapacity();
        if (_selectedWorldIndex < _worldListScrollOffset)
        {
            _worldListScrollOffset = _selectedWorldIndex;
        }
        else if (_selectedWorldIndex >= _worldListScrollOffset + visibleCount)
        {
            _worldListScrollOffset = _selectedWorldIndex - visibleCount + 1;
        }
    }

    private void ClearMenuStatus()
    {
        _menuStatusText = string.Empty;
    }

    private void OpenPauseMenu()
    {
        _menuScreen = MenuScreen.Pause;
        _pauseMenuSelectedIndex = Math.Clamp(_pauseMenuSelectedIndex, PauseMenuSettingsIndex, PauseMenuExitToMainMenuIndex);
        ClearMenuStatus();
    }

    private void CloseSettingsMenu()
    {
        _menuScreen = _settingsBackScreen;
        ClearMenuStatus();
    }

    private enum GameMode
    {
        MainMenu,
        Playing,
        Paused
    }
}
