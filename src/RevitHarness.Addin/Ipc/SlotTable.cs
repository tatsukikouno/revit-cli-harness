#nullable enable

using System;
using System.Collections.Generic;

namespace RevitHarness.Addin.Ipc
{
    public sealed class SlotEntry
    {
        public string Panel = string.Empty;
        public string Name = string.Empty;
        public string CommandType = string.Empty;
    }

    /// <summary>
    /// The fixed pool of permanent ribbon command stubs (<c>TargetSlot0</c>..<c>TargetSlot{Capacity-1}</c>).
    /// Every pushbutton of the hot-reloaded target, including those inside split buttons and pulldowns, owns one slot.
    /// </summary>
    public sealed class SlotTable
    {
        public const int Capacity = 32;

        private readonly SlotEntry?[] _slots = new SlotEntry?[Capacity];

        public SlotEntry? Get(int index) => index >= 0 && index < Capacity ? _slots[index] : null;

        public int Allocate(string panel, string name, string commandType)
        {
            var index = Array.FindIndex(_slots, s => s == null);
            if (index < 0)
                throw new InvalidOperationException($"At most {Capacity} hot reloaded ribbon buttons are supported.");
            _slots[index] = new SlotEntry { Panel = panel, Name = name, CommandType = commandType };
            return index;
        }

        public IEnumerable<KeyValuePair<int, SlotEntry>> Occupied()
        {
            for (var i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                if (slot != null) yield return new KeyValuePair<int, SlotEntry>(i, slot);
            }
        }
    }
}
