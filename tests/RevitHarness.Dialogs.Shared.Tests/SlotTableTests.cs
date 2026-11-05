using RevitHarness.Addin.Ipc;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace RevitHarness.Dialogs.Shared.Tests
{
    public class SlotTableTests
    {
        [Fact]
        public void Capacity_covers_a_product_with_many_buttons()
        {
            Assert.True(SlotTable.Capacity >= 16);
            var table = new SlotTable();
            for (var i = 0; i < SlotTable.Capacity; i++)
                Assert.Equal(i, table.Allocate("Panel", "B" + i, "T" + i));
            Assert.Equal(SlotTable.Capacity, table.Occupied().Count());
        }

        [Fact]
        public void Allocating_beyond_capacity_names_the_limit()
        {
            var table = new SlotTable();
            for (var i = 0; i < SlotTable.Capacity; i++) table.Allocate("P", "B" + i, "T");
            var ex = Assert.Throws<InvalidOperationException>(() => table.Allocate("P", "Extra", "T"));
            Assert.Contains(SlotTable.Capacity.ToString(), ex.Message);
        }

        [Fact]
        public void Get_outside_the_pool_is_null()
        {
            var table = new SlotTable();
            Assert.Null(table.Get(-1));
            Assert.Null(table.Get(SlotTable.Capacity));
            Assert.Null(table.Get(0));
        }

        [Fact]
        public void Child_buttons_of_split_and_pulldown_take_slots_with_the_container_as_owner()
        {
            var table = new SlotTable();
            Assert.Equal(0, table.Allocate("Panel", "Plain", "A"));
            Assert.Equal(1, table.Allocate("SplitName", "Child1", "B"));
            Assert.Equal(2, table.Allocate("PulldownName", "Child2", "C"));
            Assert.Equal(new[] { "Panel", "SplitName", "PulldownName" }, table.Occupied().Select(o => o.Value.Panel));
        }

        [Fact]
        public void Every_slot_has_a_permanent_command_stub()
        {
            var source = File.ReadAllText(Path.Combine(SourceDir(), "..", "..", "src", "RevitHarness.Addin", "Ipc", "TargetHost.cs"));
            var stubs = Regex.Matches(source, @"class TargetSlot(\d+) : TargetSlotCommand \{ public TargetSlot\1\(\) : base\(\1\) \{ \} \}")
                .Select(m => int.Parse(m.Groups[1].Value)).OrderBy(n => n).ToArray();
            Assert.Equal(Enumerable.Range(0, SlotTable.Capacity), stubs);
        }

        private static string SourceDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
    }
}
