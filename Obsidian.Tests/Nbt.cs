using Obsidian;
using Obsidian.Items;
using Obsidian.Net;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests
{
    public class Nbt
    {
        private readonly ITestOutputHelper output;

        public Nbt(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public async Task ReadSlot()
        {
            await using var stream = new MinecraftStream();

            var dataSlot = new Slot
            {
                Count = 0,
                Id = 25,
                Present = true,
                ItemNbt = new ItemNbt
                {
                    Slot = 1,
                    Damage = 1
                }
            };
            await stream.WriteSlotAsync(dataSlot);

            stream.Position = 0;

            var slot = await stream.ReadSlotAsync();

            Assert.True(slot.Present);
            Assert.Equal(0, slot.Count);
            Assert.Equal(25, slot.Id);

            Assert.Equal(1, slot.ItemNbt.Slot);
            Assert.Equal(1, slot.ItemNbt.Damage);
        }

        [Fact]
        public async Task ReadPlayerData()
        {

        }
    }
}
