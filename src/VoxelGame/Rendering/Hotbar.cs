using VoxelGame.Input;
using VoxelGame.World.Blocks;

namespace VoxelGame.Rendering;

public sealed class Hotbar
{
    public const int SlotCount = 9;
    public const int MaxStackSize = 64;

    private readonly HotbarSlot[] _slots;

    public int SelectedIndex { get; private set; }
    public IReadOnlyList<HotbarSlot> Slots => _slots;
    public BlockType SelectedBlock => _slots[SelectedIndex].Block;

    private Hotbar(HotbarSlot[] slots)
    {
        _slots = slots;
    }

    public static Hotbar CreateEmpty()
    {
        return new Hotbar(Enumerable.Repeat(HotbarSlot.Empty, SlotCount).ToArray());
    }

    public bool UpdateSelection(InputManager input)
    {
        var previousIndex = SelectedIndex;

        for (var i = 1; i <= SlotCount; i++)
        {
            if (input.IsNumberPressedThisFrame(i))
            {
                SelectedIndex = i - 1;
            }
        }

        return SelectedIndex != previousIndex;
    }

    public bool SelectSlot(int index)
    {
        var clamped = Math.Clamp(index, 0, SlotCount - 1);
        if (clamped == SelectedIndex)
        {
            return false;
        }

        SelectedIndex = clamped;
        return true;
    }

    public bool CycleSelection(int delta)
    {
        if (delta == 0)
        {
            return false;
        }

        var next = (SelectedIndex + delta) % SlotCount;
        if (next < 0)
        {
            next += SlotCount;
        }

        if (next == SelectedIndex)
        {
            return false;
        }

        SelectedIndex = next;
        return true;
    }

    public bool TryAdd(BlockType block)
    {
        if (block == BlockType.Air)
        {
            return true;
        }

        for (var i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Block == block && _slots[i].Count < MaxStackSize)
            {
                _slots[i] = _slots[i] with { Count = _slots[i].Count + 1 };
                return true;
            }
        }

        for (var i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].IsEmpty)
            {
                _slots[i] = new HotbarSlot(block, 1);
                return true;
            }
        }

        return false;
    }

    public void Clear()
    {
        Array.Fill(_slots, HotbarSlot.Empty);
        SelectedIndex = 0;
    }

    public bool TryConsumeSelected()
    {
        var slot = _slots[SelectedIndex];
        if (slot.IsEmpty)
        {
            return false;
        }

        var nextCount = slot.Count - 1;
        _slots[SelectedIndex] = nextCount <= 0 ? HotbarSlot.Empty : slot with { Count = nextCount };
        return true;
    }
}

public readonly record struct HotbarSlot(BlockType Block, int Count)
{
    public static HotbarSlot Empty => new(BlockType.Air, 0);
    public bool IsEmpty => Block == BlockType.Air || Count <= 0;
}
