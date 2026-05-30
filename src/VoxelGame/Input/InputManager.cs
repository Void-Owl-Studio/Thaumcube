using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace VoxelGame.Input;

public sealed class InputManager : IDisposable
{
    private static readonly Key[] TrackedKeys =
    [
        Key.Escape,
        Key.W,
        Key.A,
        Key.S,
        Key.D,
        Key.Space,
        Key.ShiftLeft,
        Key.Up,
        Key.Down,
        Key.Left,
        Key.Right,
        Key.Enter,
        Key.Backspace,
        Key.E,
        Key.T,
        Key.Number1,
        Key.Number2,
        Key.Number3,
        Key.Number4,
        Key.Number5,
        Key.Number6,
        Key.Number7,
        Key.Number8,
        Key.Number9
    ];

    private IInputContext? _context;
    private IKeyboard? _keyboard;
    private IMouse? _mouse;
    private readonly List<char> _typedCharacters = [];
    private bool _lastLeft;
    private bool _lastRight;
    private Vector2 _lastMousePosition;
    private bool _hasMousePosition;
    private float _scrollDeltaY;
    private HashSet<Key> _pressedKeys = new();
    private HashSet<Key> _previousPressedKeys = new();

    public bool ExitRequested { get; private set; }
    public bool BreakPressedThisFrame { get; private set; }
    public bool BreakHeld { get; private set; }
    public bool LeftPressedThisFrame { get; private set; }
    public bool RightPressedThisFrame { get; private set; }
    public bool PlacePressedThisFrame { get; private set; }
    public bool BackspacePressedThisFrame { get; private set; }
    public bool InventoryPressedThisFrame { get; private set; }
    public bool ThirdPersonPressedThisFrame { get; private set; }
    public string TypedText { get; private set; } = string.Empty;
    public Vector2 MousePosition { get; private set; }
    public Vector2 MouseDelta { get; private set; }
    public float ScrollDeltaY { get; private set; }

    public void Attach(IWindow window)
    {
        _context = window.CreateInput();
        _keyboard = _context.Keyboards.FirstOrDefault();
        _mouse = _context.Mice.FirstOrDefault();

        if (_keyboard is not null)
        {
            _keyboard.KeyChar += OnKeyChar;
        }

        if (_mouse is not null)
        {
            _mouse.Cursor.CursorMode = CursorMode.Raw;
            _mouse.Scroll += OnMouseScroll;
        }
    }

    public void UpdateFrame()
    {
        BreakPressedThisFrame = false;
        PlacePressedThisFrame = false;
        BreakHeld = false;
        LeftPressedThisFrame = false;
        RightPressedThisFrame = false;
        BackspacePressedThisFrame = false;
        InventoryPressedThisFrame = false;
        ThirdPersonPressedThisFrame = false;
        MouseDelta = default;
        TypedText = string.Empty;
        ScrollDeltaY = _scrollDeltaY;
        _scrollDeltaY = 0f;

        (_previousPressedKeys, _pressedKeys) = (_pressedKeys, _previousPressedKeys);
        _pressedKeys.Clear();

        if (_keyboard is not null)
        {
            foreach (var key in TrackedKeys)
            {
                if ((int)key >= 0 && _keyboard.IsKeyPressed(key))
                {
                    _pressedKeys.Add(key);
                }
            }
        }

        ExitRequested = IsKeyPressedThisFrame(Key.Escape);
        BackspacePressedThisFrame = IsKeyPressedThisFrame(Key.Backspace);
        InventoryPressedThisFrame = IsKeyPressedThisFrame(Key.E);
        ThirdPersonPressedThisFrame = IsKeyPressedThisFrame(Key.T);
        if (_typedCharacters.Count > 0)
        {
            TypedText = new string(_typedCharacters.ToArray());
            _typedCharacters.Clear();
        }

        if (_mouse is null)
        {
            return;
        }

        var position = _mouse.Position;
        MousePosition = position;
        if (_hasMousePosition)
        {
            MouseDelta = position - _lastMousePosition;
        }

        _lastMousePosition = position;
        _hasMousePosition = true;

        var left = _mouse.IsButtonPressed(MouseButton.Left);
        var right = _mouse.IsButtonPressed(MouseButton.Right);

        BreakHeld = left;
        BreakPressedThisFrame = left && !_lastLeft;
        LeftPressedThisFrame = BreakPressedThisFrame;
        PlacePressedThisFrame = right && !_lastRight;
        RightPressedThisFrame = PlacePressedThisFrame;

        _lastLeft = left;
        _lastRight = right;
    }

    public bool IsKeyPressed(Key key) => _pressedKeys.Contains(key);

    public bool IsKeyPressedThisFrame(Key key) => _pressedKeys.Contains(key) && !_previousPressedKeys.Contains(key);

    public void SetCursorCaptured(bool captured)
    {
        if (_mouse is not null)
        {
            _mouse.Cursor.CursorMode = captured ? CursorMode.Raw : CursorMode.Normal;
        }
    }

    public bool IsNumberPressed(int slot)
    {
        return slot switch
        {
            1 => IsKeyPressed(Key.Number1),
            2 => IsKeyPressed(Key.Number2),
            3 => IsKeyPressed(Key.Number3),
            4 => IsKeyPressed(Key.Number4),
            5 => IsKeyPressed(Key.Number5),
            6 => IsKeyPressed(Key.Number6),
            7 => IsKeyPressed(Key.Number7),
            8 => IsKeyPressed(Key.Number8),
            9 => IsKeyPressed(Key.Number9),
            _ => false
        };
    }

    public bool IsNumberPressedThisFrame(int slot)
    {
        return slot switch
        {
            1 => IsKeyPressedThisFrame(Key.Number1),
            2 => IsKeyPressedThisFrame(Key.Number2),
            3 => IsKeyPressedThisFrame(Key.Number3),
            4 => IsKeyPressedThisFrame(Key.Number4),
            5 => IsKeyPressedThisFrame(Key.Number5),
            6 => IsKeyPressedThisFrame(Key.Number6),
            7 => IsKeyPressedThisFrame(Key.Number7),
            8 => IsKeyPressedThisFrame(Key.Number8),
            9 => IsKeyPressedThisFrame(Key.Number9),
            _ => false
        };
    }

    public void Dispose()
    {
        if (_keyboard is not null)
        {
            _keyboard.KeyChar -= OnKeyChar;
        }

        if (_mouse is not null)
        {
            _mouse.Scroll -= OnMouseScroll;
        }

        _context?.Dispose();
    }

    private void OnMouseScroll(IMouse mouse, ScrollWheel scrollWheel)
    {
        _scrollDeltaY += scrollWheel.Y;
    }

    private void OnKeyChar(IKeyboard keyboard, char character)
    {
        if (!char.IsControl(character))
        {
            _typedCharacters.Add(character);
        }
    }
}
