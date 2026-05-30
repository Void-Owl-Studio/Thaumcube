using VoxelGame.Input;
using VoxelGame.World.Blocks;

namespace VoxelGame.Rendering;

public sealed class Hotbar
{
    public const int SlotCount = 9;
    public const int InventorySlotCount = 27;
    public const int MaxStackSize = 64;

    private readonly HotbarSlot[] _hotbarSlots;
    private readonly HotbarSlot[] _inventorySlots;

    public int SelectedIndex { get; private set; }
    public IReadOnlyList<HotbarSlot> Slots => _hotbarSlots;
    public IReadOnlyList<HotbarSlot> InventorySlots => _inventorySlots;
    public HotbarSlot CursorSlot { get; private set; }
    public BlockType SelectedBlock => _hotbarSlots[SelectedIndex].Block;

    private Hotbar(HotbarSlot[] hotbarSlots, HotbarSlot[] inventorySlots)
    {
        _hotbarSlots = hotbarSlots;
        _inventorySlots = inventorySlots;
    }

    public static Hotbar CreateEmpty()
    {
        return new Hotbar(
            Enumerable.Repeat(HotbarSlot.Empty, SlotCount).ToArray(),
            Enumerable.Repeat(HotbarSlot.Empty, InventorySlotCount).ToArray());
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

        if (TryStackInto(_hotbarSlots, block) || TryStackInto(_inventorySlots, block))
        {
            return true;
        }

        if (TryPlaceIntoEmpty(_hotbarSlots, block) || TryPlaceIntoEmpty(_inventorySlots, block))
        {
            return true;
        }

        return false;
    }

    public void Clear()
    {
        Array.Fill(_hotbarSlots, HotbarSlot.Empty);
        Array.Fill(_inventorySlots, HotbarSlot.Empty);
        CursorSlot = HotbarSlot.Empty;
        SelectedIndex = 0;
    }

    public bool TryConsumeSelected()
    {
        var slot = _hotbarSlots[SelectedIndex];
        if (slot.IsEmpty)
        {
            return false;
        }

        var nextCount = slot.Count - 1;
        _hotbarSlots[SelectedIndex] = nextCount <= 0 ? HotbarSlot.Empty : slot with { Count = nextCount };
        return true;
    }

    public bool InteractWithHotbarSlot(int index)
    {
        if (index < 0 || index >= _hotbarSlots.Length)
        {
            return false;
        }

        SelectSlot(index);
        return InteractWithSlot(_hotbarSlots, index);
    }

    public bool InteractWithInventorySlot(int index)
    {
        if (index < 0 || index >= _inventorySlots.Length)
        {
            return false;
        }

        return InteractWithSlot(_inventorySlots, index);
    }

    private bool InteractWithSlot(HotbarSlot[] slots, int index)
    {
        var slot = slots[index];
        if (CursorSlot.IsEmpty)
        {
            if (slot.IsEmpty)
            {
                return false;
            }

            CursorSlot = slot;
            slots[index] = HotbarSlot.Empty;
            return true;
        }

        if (slot.IsEmpty)
        {
            slots[index] = CursorSlot;
            CursorSlot = HotbarSlot.Empty;
            return true;
        }

        if (slot.Block == CursorSlot.Block && slot.Count < MaxStackSize)
        {
            var transfer = Math.Min(CursorSlot.Count, MaxStackSize - slot.Count);
            slots[index] = slot with { Count = slot.Count + transfer };
            CursorSlot = CursorSlot with { Count = CursorSlot.Count - transfer };
            if (CursorSlot.Count <= 0)
            {
                CursorSlot = HotbarSlot.Empty;
            }

            return true;
        }

        (slots[index], CursorSlot) = (CursorSlot, slot);
        return true;
    }

    private static bool TryStackInto(HotbarSlot[] slots, BlockType block)
    {
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].Block == block && slots[i].Count < MaxStackSize)
            {
                slots[i] = slots[i] with { Count = slots[i].Count + 1 };
                return true;
            }
        }

        return false;
    }

    private static bool TryPlaceIntoEmpty(HotbarSlot[] slots, BlockType block)
    {
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].IsEmpty)
            {
                slots[i] = new HotbarSlot(block, 1);
                return true;
            }
        }

        return false;
    }
}

public readonly record struct HotbarSlot(BlockType Block, int Count)
{
    public static HotbarSlot Empty => new(BlockType.Air, 0);
    public bool IsEmpty => Block == BlockType.Air || Count <= 0;
}
