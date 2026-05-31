using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using VoxelGame.Input;
using VoxelGame.Player;
using VoxelGame.Rendering;
using VoxelGame.Rendering.Sprites;
using VoxelGame.Rendering.Vulkan;
using VoxelGame.World;
using VoxelGame.World.Blocks;
using VoxelGame.World.Chunks;
using VoxelGame.World.Items;
using VoxelGame.World.Storage;

namespace VoxelGame.Core;

public sealed class GameApplication : IDisposable
{
    private const float LeafStickDropChanceMin = 0.02f;
    private const float LeafStickDropChanceMax = 0.05f;

    private readonly object _asyncOperationLock = new();
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
    private BlockBreakParticleManager? _blockParticles;
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
    private string _busyTitle = string.Empty;
    private string _busyStatusText = string.Empty;
    private string? _pendingPlayerSkinPath;
    private PlayerBodyType _playerBodyType = PlayerSkinStore.LoadBodyType();
    private MenuScreen _settingsBackScreen = MenuScreen.Main;
    private VoxelRaycastHit? _currentBreakTarget;
    private float _breakProgressSeconds;
    private double _runningSeconds;
    private bool _disposedRuntime;
    private bool _closeRequestedAfterSave;
    private bool _showInventory;
    private float _busyProgress;
    private Task<LoadedGameSession>? _pendingLoadTask;
    private Task? _pendingSaveTask;
    private CameraViewMode _cameraMode = CameraViewMode.FirstPerson;
    private const float BlockBreakSeconds = 0.65f;
    private const float MaxGameplayDeltaSeconds = 1f / 15f;
    private const int MainMenuSingleplayerIndex = 0;
    private const int MainMenuSettingsIndex = 1;
    private const int MainMenuBodyTypeIndex = 2;
    private const int MainMenuLoadSkinIndex = 3;
    private const int MainMenuApplySkinIndex = 4;
    private const int MainMenuExitIndex = 5;
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
        options.VSync = _settings.EnableVSync;

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
        var gameplayDt = Math.Min(dt, MaxGameplayDeltaSeconds);
        _runningSeconds += deltaSeconds;
        _timer.Update(deltaSeconds);
        _input.UpdateFrame();

        if (_autoCloseAfterSeconds is { } closeAfter && _runningSeconds >= closeAfter)
        {
            RequestApplicationClose();
            return;
        }

        AdvanceAsyncOperations();

        if (_mode is GameMode.LoadingWorld or GameMode.SavingWorld)
        {
            UpdateHud();
            return;
        }

        if (_mode is GameMode.MainMenu or GameMode.Paused)
        {
            UpdateMenu();
            UpdateHud();
            return;
        }

        if (_input.InventoryPressedThisFrame)
        {
            ToggleInventory();
        }

        if (_input.ThirdPersonPressedThisFrame && !_showInventory)
        {
            _cameraMode = NextCameraMode(_cameraMode);
        }

        if (_showInventory && _input.ExitRequested)
        {
            ToggleInventory(forceState: false);
        }

        if (_input.ExitRequested)
        {
            PauseGame();
            UpdateHud();
            return;
        }

        if (_world is null || _player is null || _drops is null || _blockParticles is null)
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

        _player.Update(gameplayDt, _input, allowLook: !_showInventory);
        _world.ReportPlayerPosition(_player.Position);

        if (_showInventory)
        {
            HandleInventoryInteraction();
        }
        else
        {
            HandleBlockInteraction(gameplayDt);
        }

        _drops.Update(gameplayDt, _player.Position, _hotbar);
        _blockParticles.Update(gameplayDt);

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
            _mainMenuSelectedIndex = (_mainMenuSelectedIndex + MainMenuExitIndex) % (MainMenuExitIndex + 1);
        }

        if (_input.IsKeyPressedThisFrame(Key.Down) || _input.IsKeyPressedThisFrame(Key.S))
        {
            _mainMenuSelectedIndex = (_mainMenuSelectedIndex + 1) % (MainMenuExitIndex + 1);
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
            RequestApplicationClose();
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

        if (menuIndex == MainMenuBodyTypeIndex)
        {
            TogglePlayerBodyType();
            return;
        }

        if (menuIndex == MainMenuLoadSkinIndex)
        {
            PickPlayerSkinFromMenu();
            return;
        }

        if (menuIndex == MainMenuApplySkinIndex)
        {
            ApplyPendingPlayerSkin();
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

    private void HandleBlockInteraction(float dt)
    {
        if (_world is null || _player is null || _drops is null || _blockParticles is null)
        {
            return;
        }

        _player.SetBreakingBlock(false);
        var aimCamera = _player.Camera;
        var ray = _player.CreateLookRay(aimCamera);
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
            _player.SetBreakingBlock(true);
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
                _blockParticles.Spawn(brokenBlock, blockPosition);
                if (TryResolveDroppedItem(brokenBlock, out var droppedItem))
                {
                    _drops.Spawn(droppedItem, blockPosition, aimCamera.Forward * 1.6f + new System.Numerics.Vector3(0, 2.2f, 0));
                }

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
            if (selected != BlockType.Air &&
                _world.Blocks.IsPlaceable(selected) &&
                !_player.Body.IntersectsBlock(place.X, place.Y, place.Z))
            {
                _world.SetBlock(place.X, place.Y, place.Z, selected);
                _hotbar.TryConsumeSelected();
            }
        }
    }

    private static bool TryResolveDroppedItem(BlockType brokenBlock, out BlockType droppedItem)
    {
        if (brokenBlock == BlockType.OakLeaves)
        {
            var chance = Random.Shared.NextSingle() * (LeafStickDropChanceMax - LeafStickDropChanceMin) + LeafStickDropChanceMin;
            if (Random.Shared.NextSingle() <= chance)
            {
                droppedItem = BlockType.Stick;
                return true;
            }

            droppedItem = BlockType.Air;
            return false;
        }

        if (brokenBlock != BlockType.Air)
        {
            droppedItem = brokenBlock;
            return true;
        }

        droppedItem = BlockType.Air;
        return false;
    }

    private void OnRender(double deltaSeconds)
    {
        var camera = _player?.GetCamera(_cameraMode) ?? new CameraState(new System.Numerics.Vector3(0, 64, -4), 0, 0);
        var meshes = BuildSceneMeshes(camera);
        var sprites = BuildSceneSprites();
        var renderDistance = _world?.RenderDistanceChunks ?? _selectedRenderDistance;
        var scene = new RenderScene(meshes, sprites, camera, _hotbar, _hud, renderDistance, (float)_runningSeconds);
        _renderer.Render(scene);
    }

    private IEnumerable<ChunkRenderMesh> BuildSceneMeshes(CameraState camera)
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

        if (_player is not null && _cameraMode != CameraViewMode.FirstPerson)
        {
            meshes.AddRange(_player.BuildRenderMeshes(hideHeadForFirstPerson: false));
        }

        if (_currentBreakTarget is { } breakTarget && _hud.BreakProgress > 0f)
        {
            var stage = Math.Clamp((int)MathF.Floor(_hud.BreakProgress * 10f), 0, 9);
            meshes.Add(ChunkMeshBuilder.BuildBreakOverlayMesh(breakTarget.BlockPosition, breakTarget.FaceNormal, stage));
        }

        if (_player is not null)
        {
            meshes.AddRange(SkyMeshBuilder.Build(camera, _settings, (float)_runningSeconds));
        }

        return meshes;
    }

    private IEnumerable<WorldSprite> BuildSceneSprites()
    {
        if (_blockParticles is not null)
        {
            foreach (var sprite in _blockParticles.BuildSprites())
            {
                yield return sprite;
            }
        }

        if (_drops is not null)
        {
            foreach (var sprite in _drops.BuildSprites())
            {
                yield return sprite;
            }
        }
    }

    private void UpdateHud()
    {
        _hud.Fps = _timer.Fps;
        _hud.PlayerPosition = _player?.Position ?? default;
        _hud.CurrentBiome = ResolveCurrentBiomeName();
        _hud.SelectedSlot = _hotbar.SelectedIndex;
        _hud.SelectedBlock = _hotbar.SelectedBlock;
        _hud.MousePosition = _input.MousePosition;
        _hud.LoadedChunks = _world?.LoadedChunkCount ?? 0;
        _hud.VisibleChunkMeshes = _world?.VisibleMeshCount ?? 0;
        _hud.ShowMenu = _mode != GameMode.Playing;
        _hud.ShowInventory = _showInventory && _mode == GameMode.Playing;
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
        _hud.MenuSkinText = BuildMenuSkinText();
        _hud.MenuSkinReadyToApply = !string.IsNullOrWhiteSpace(_pendingPlayerSkinPath);
        _hud.MenuPlayerBodyType = _playerBodyType;
        _hud.CreateWorldName = _newWorldName;
        _hud.MenuWorldNames = GetVisibleWorldNames();
        _hud.InventorySlots = _hotbar.InventorySlots;
        _hud.CursorSlot = _hotbar.CursorSlot;
        _hud.MenuRenderDistance = _selectedRenderDistance;
        _hud.MenuSelectedWorldIndex = _selectedWorldIndex < _worldListScrollOffset
            ? -1
            : _selectedWorldIndex - _worldListScrollOffset;
        _hud.ShowBusyOverlay = _mode is GameMode.LoadingWorld or GameMode.SavingWorld;
        _hud.BusyTitle = _busyTitle;
        _hud.BusyStatusText = _busyStatusText;
        _hud.BusyProgress = _busyProgress;
    }

    private void HandleInventoryInteraction()
    {
        if (!_input.LeftPressedThisFrame)
        {
            return;
        }

        var hit = HudLayout.HitTestInventory(_input.MousePosition, _window.Size.X, _window.Size.Y);
        switch (hit.Area)
        {
            case InventoryArea.Main:
                _hotbar.InteractWithInventorySlot(hit.Index);
                break;
            case InventoryArea.Hotbar:
                _hotbar.InteractWithHotbarSlot(hit.Index);
                break;
        }
    }

    private string ResolveCurrentBiomeName()
    {
        if (_world is null || _player is null)
        {
            return string.Empty;
        }

        var worldX = (int)MathF.Floor(_player.Position.X);
        var worldZ = (int)MathF.Floor(_player.Position.Z);
        return FormatBiomeName(_world.GetBiome(worldX, worldZ));
    }

    private static string FormatBiomeName(World.Generation.BiomeType biome)
    {
        return biome switch
        {
            World.Generation.BiomeType.Ocean => "Ocean",
            World.Generation.BiomeType.Beach => "Beach",
            World.Generation.BiomeType.Plains => "Plains",
            World.Generation.BiomeType.Forest => "Forest",
            World.Generation.BiomeType.Desert => "Desert",
            World.Generation.BiomeType.Savanna => "Savanna",
            World.Generation.BiomeType.Swamp => "Swamp",
            World.Generation.BiomeType.Taiga => "Taiga",
            World.Generation.BiomeType.Snow => "Snow",
            World.Generation.BiomeType.Mountains => "Mountains",
            World.Generation.BiomeType.River => "River",
            World.Generation.BiomeType.Lake => "Lake",
            _ => biome.ToString()
        };
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
            BeginWorldLoad(() => WorldSaveStore.Open(_settings.WorldsRootPath, world.Name), "LOADING WORLD");
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
            BeginWorldLoad(() => WorldSaveStore.CreateNew(_settings.WorldsRootPath, worldName, _settings.WorldSeed), "CREATING WORLD");
        }
        catch (Exception)
        {
            _menuStatusText = "FAILED TO CREATE WORLD";
            RefreshWorldCatalog();
        }
    }

    private void ReturnToMainMenu()
    {
        BeginWorldSave(closeAfterSave: false);
    }

    private void PauseGame()
    {
        if (_mode != GameMode.Playing)
        {
            return;
        }

        _showInventory = false;
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
        _input.SetCursorCaptured(!_showInventory);
        ResetBreaking();
        ClearMenuStatus();
    }

    private void ToggleInventory(bool? forceState = null)
    {
        if (_mode != GameMode.Playing)
        {
            return;
        }

        _showInventory = forceState ?? !_showInventory;
        _input.SetCursorCaptured(!_showInventory);
        ResetBreaking();
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

    private void BeginWorldLoad(Func<WorldSaveStore> worldStoreFactory, string title)
    {
        if (_pendingLoadTask is not null || _pendingSaveTask is not null)
        {
            return;
        }

        _hotbar.Clear();
        _showInventory = false;
        _cameraMode = CameraViewMode.FirstPerson;
        _input.SetCursorCaptured(false);
        ResetBreaking();
        SetBusyState(title, "PREPARING WORLD...", 0.02f);
        _mode = GameMode.LoadingWorld;
        _pendingLoadTask = Task.Run(() => LoadGameSession(worldStoreFactory));
    }

    private LoadedGameSession LoadGameSession(Func<WorldSaveStore> worldStoreFactory)
    {
        SetBusyState(_busyTitle, "OPENING SAVE...", 0.08f);
        var worldStore = worldStoreFactory();
        var world = new VoxelWorld(worldStore.Metadata.Seed, _selectedRenderDistance, worldStore);
        var player = new FirstPersonPlayer(world, _settings.MouseSensitivity, _playerBodyType);
        var drops = new DroppedBlockManager(world);
        var particles = new BlockBreakParticleManager(world);

        SetBusyState(_busyTitle, "READING PLAYER DATA...", 0.16f);
        var playerSave = worldStore.LoadPlayer();
        if (playerSave is not null)
        {
            var safePosition = world.EnsureSafeSpawnPosition(playerSave.Position, (loaded, total) => UpdateBusyProgress("STREAMING CHUNKS...", loaded, total, 0.16f, 0.72f));
            world.RebuildDirtyMeshes((built, total) => UpdateBusyProgress("BUILDING MESHES...", built, total, 0.72f, 0.96f));
            player.SpawnAt(safePosition, playerSave.YawDegrees, playerSave.PitchDegrees);
        }
        else
        {
            SetBusyState(_busyTitle, "FINDING SPAWN...", 0.22f);
            var spawn = world.FindSpawnPosition(0, 0, (loaded, total) => UpdateBusyProgress("STREAMING CHUNKS...", loaded, total, 0.22f, 0.72f));
            world.RebuildDirtyMeshes((built, total) => UpdateBusyProgress("BUILDING MESHES...", built, total, 0.72f, 0.96f));
            player.SpawnAt(spawn);
        }

        worldStore.Touch();
        SetBusyState(_busyTitle, "FINALIZING...", 1f);
        return new LoadedGameSession(worldStore, world, player, drops, particles);
    }

    private void BeginWorldSave(bool closeAfterSave)
    {
        if (_pendingSaveTask is not null || _pendingLoadTask is not null)
        {
            return;
        }

        if (_activeWorldStore is null || _world is null)
        {
            if (closeAfterSave)
            {
                _window.Close();
            }
            else
            {
                FinishReturnToMainMenu();
            }

            return;
        }

        var world = _world;
        var worldStore = _activeWorldStore;
        var playerSave = _player?.CreateSaveData();
        _closeRequestedAfterSave = closeAfterSave;
        _showInventory = false;
        _input.SetCursorCaptured(false);
        ResetBreaking();
        SetBusyState("SAVING WORLD", "WRITING CHUNKS...", 0.02f);
        _mode = GameMode.SavingWorld;
        _pendingSaveTask = Task.Run(() =>
        {
            world.SaveWorldState((saved, total) => UpdateBusyProgress("WRITING CHUNKS...", saved, total, 0.08f, 0.92f));
            SetBusyState("SAVING WORLD", "WRITING PLAYER...", 0.96f);
            if (playerSave is not null)
            {
                worldStore.SavePlayer(playerSave);
            }

            SetBusyState("SAVING WORLD", "DONE", 1f);
        });
    }

    private void AdvanceAsyncOperations()
    {
        if (_pendingLoadTask is not null && _pendingLoadTask.IsCompleted)
        {
            try
            {
                ApplyLoadedSession(_pendingLoadTask.GetAwaiter().GetResult());
                _pendingLoadTask = null;
            }
            catch (Exception)
            {
                _pendingLoadTask = null;
                _mode = GameMode.MainMenu;
                _menuScreen = MenuScreen.Singleplayer;
                _menuStatusText = "FAILED TO LOAD WORLD";
                SetBusyState(string.Empty, string.Empty, 0f);
                RefreshWorldCatalog();
            }
        }

        if (_pendingSaveTask is not null && _pendingSaveTask.IsCompleted)
        {
            try
            {
                _pendingSaveTask.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                _menuStatusText = "FAILED TO SAVE WORLD";
            }
            finally
            {
                _pendingSaveTask = null;
            }

            if (_closeRequestedAfterSave)
            {
                _activeWorldStore = null;
                _world = null;
                _player = null;
                _drops = null;
                _blockParticles = null;
                _window.Close();
                return;
            }

            FinishReturnToMainMenu();
        }
    }

    private void ApplyLoadedSession(LoadedGameSession session)
    {
        _activeWorldStore = session.WorldStore;
        _world = session.World;
        _player = session.Player;
        _drops = session.Drops;
        _blockParticles = session.BlockParticles;
        _mode = GameMode.Playing;
        _input.SetCursorCaptured(true);
        _menuStatusText = string.Empty;
        SetBusyState(string.Empty, string.Empty, 0f);
        ResetBreaking();
        UpdateHud();
    }

    private void FinishReturnToMainMenu()
    {
        _activeWorldStore = null;
        _world = null;
        _player = null;
        _drops = null;
        _blockParticles = null;
        _hotbar.Clear();
        _mode = GameMode.MainMenu;
        _cameraMode = CameraViewMode.FirstPerson;
        _closeRequestedAfterSave = false;
        SetBusyState(string.Empty, string.Empty, 0f);
        OpenMainMenu();
        RefreshWorldCatalog();
    }

    private void RequestApplicationClose()
    {
        if (_mode == GameMode.SavingWorld)
        {
            _closeRequestedAfterSave = true;
            return;
        }

        if (_world is not null)
        {
            BeginWorldSave(closeAfterSave: true);
            return;
        }

        _window.Close();
    }

    private void SetBusyState(string title, string status, float progress)
    {
        lock (_asyncOperationLock)
        {
            _busyTitle = title;
            _busyStatusText = status;
            _busyProgress = Math.Clamp(progress, 0f, 1f);
        }
    }

    private void UpdateBusyProgress(string status, int completed, int total, float start, float end)
    {
        var progress = total <= 0 ? end : start + ((end - start) * completed / total);
        SetBusyState(_busyTitle, status, progress);
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

    private void PickPlayerSkinFromMenu()
    {
        var selectedPath = NativeFileDialog.PickPngFile();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            _menuStatusText = "SKIN SELECTION CANCELED";
            return;
        }

        _pendingPlayerSkinPath = selectedPath;
        _menuStatusText = "SKIN SELECTED - PRESS APPLY";
    }

    private void ApplyPendingPlayerSkin()
    {
        if (string.IsNullOrWhiteSpace(_pendingPlayerSkinPath))
        {
            _menuStatusText = "LOAD A SKIN FIRST";
            return;
        }

        if (!PlayerSkinStore.TryApplySkin(_pendingPlayerSkinPath, out var error))
        {
            _menuStatusText = error;
            return;
        }

        _pendingPlayerSkinPath = null;
        _renderer.ReloadBlockAtlas();
        _menuStatusText = "PLAYER SKIN APPLIED";
    }

    private void TogglePlayerBodyType()
    {
        _playerBodyType = _playerBodyType == PlayerBodyType.Slim ? PlayerBodyType.Normal : PlayerBodyType.Slim;
        PlayerSkinStore.SaveBodyType(_playerBodyType);
        _player?.SetBodyType(_playerBodyType);
        _menuStatusText = _playerBodyType == PlayerBodyType.Slim ? "SLIM BODY SELECTED" : "NORMAL BODY SELECTED";
    }

    private string BuildMenuSkinText()
    {
        if (!string.IsNullOrWhiteSpace(_pendingPlayerSkinPath))
        {
            return $"SELECTED {ShortenFileName(Path.GetFileName(_pendingPlayerSkinPath), 18)}";
        }

        return PlayerSkinStore.HasAppliedSkin ? "CUSTOM SKIN ACTIVE" : "DEFAULT SKIN";
    }

    private static string ShortenFileName(string fileName, int maxLength)
    {
        if (fileName.Length <= maxLength)
        {
            return fileName;
        }

        var extension = Path.GetExtension(fileName);
        var stemLength = Math.Max(4, maxLength - extension.Length - 1);
        return $"{fileName[..Math.Min(stemLength, fileName.Length)]}~{extension}";
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

    private static CameraViewMode NextCameraMode(CameraViewMode currentMode)
    {
        return currentMode switch
        {
            CameraViewMode.FirstPerson => CameraViewMode.ThirdPersonBack,
            CameraViewMode.ThirdPersonBack => CameraViewMode.ThirdPersonFront,
            _ => CameraViewMode.FirstPerson
        };
    }

    private enum GameMode
    {
        MainMenu,
        Playing,
        Paused,
        LoadingWorld,
        SavingWorld
    }

    private sealed record LoadedGameSession(
        WorldSaveStore WorldStore,
        VoxelWorld World,
        FirstPersonPlayer Player,
        DroppedBlockManager Drops,
        BlockBreakParticleManager BlockParticles);
}
