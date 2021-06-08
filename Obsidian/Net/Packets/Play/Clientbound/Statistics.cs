using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Collections.Generic;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class Statistics : IClientboundPacket
    {
        [Field(0)]
        public List<Statistic> Stats { get; } = new();

        public Statistics()
        {
        }

        public void Add(Statistic stat)
        {
            Stats.Add(stat);
        }

        public void Clear()
        {
            Stats.Clear();
        }

        public int Id => 0x06;
    }

    public struct Statistic
    {
        public CategoryIds CategoryId;

        /// <summary>
        /// Can either be a block, item or entity id. For CategoryIds.Custom, use the <see cref="StatisticIds"/> enum casted to an integer.
        /// </summary>
        public int StatisticId;

        public int Value;

        public Statistic(CategoryIds category, Block block, int value)
        {
            this.CategoryId = category;
            this.StatisticId = block.Id;
            this.Value = value;
        }

        public Statistic(CategoryIds category, StatisticIds statistic, int value)
        {
            this.CategoryId = category;
            this.StatisticId = (int)statistic;
            this.Value = value;
        }
        public Statistic(CategoryIds category, Items.Item item, int value)
        {
            this.CategoryId = category;
            this.StatisticId = item.Id;
            this.Value = value;
        }
        public Statistic(CategoryIds category, Entity entity, int value)
        {
            this.CategoryId = category;
            this.StatisticId = entity.EntityId;
            this.Value = value;
        }
    }

    public enum CategoryIds : int
    {
        Mined = 0,
        Crafted = 1,
        Used = 2,
        Broken = 3,
        PickedUp = 4,
        Dropped = 5,
        Killed = 6,
        KilledBy = 7,
        Custom = 8
    }

    public enum StatisticIds : int
    {
        LeaveGame,
        PlayOneMinute,
        TimeSinceDeath,
        SneakTime,
        WalkOneCm,
        CrouchOneCm,
        SprintOneCm,
        SwimOneCm,
        FallOneCm,
        ClimbOneCm,
        FlyOneCm,
        DiveOneCm,
        MinecartOneCm,
        BoatOneCm,
        PigOneCm,
        HorseOneCm,
        AviateOneCm, // elytras???
        Jump,
        Drop,
        DamageDealt,

    }
}
