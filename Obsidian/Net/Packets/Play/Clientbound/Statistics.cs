﻿using Obsidian.Entities;
using Obsidian.Items;
using Obsidian.Serialization.Attributes;
using System.Collections.Generic;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class Statistics : IClientboundPacket
    {
        [Field(0)]
        public List<Statistic> Stats { get; } = new();

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

    public readonly struct Statistic
    {
        public CategoryIds CategoryId { get; }

        /// <summary>
        /// Can either be a block, item or entity id. For CategoryIds.Custom, use the <see cref="StatisticIds"/> enum casted to an integer.
        /// </summary>
        public int StatisticId { get; }

        public int Value { get; }

        public Statistic(CategoryIds category, Block block, int value)
        {
            CategoryId = category;
            StatisticId = block.Id;
            Value = value;
        }

        public Statistic(CategoryIds category, StatisticIds statistic, int value)
        {
            CategoryId = category;
            StatisticId = (int)statistic;
            Value = value;
        }
        public Statistic(CategoryIds category, Item item, int value)
        {
            CategoryId = category;
            StatisticId = item.Id;
            Value = value;
        }
        public Statistic(CategoryIds category, Entity entity, int value)
        {
            CategoryId = category;
            StatisticId = entity.EntityId;
            Value = value;
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
