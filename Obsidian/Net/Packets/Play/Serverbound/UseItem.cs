using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class UseItem : IServerboundPacket
    {
        [Field(0), ActualType(typeof(int)), VarLength]
        public Hand Hand { get; private set; }

        public int Id => 0x2F;

        public async ValueTask HandleAsync(Server server, Player player)
        {
            switch (Hand)
            {
                case Hand.MainHand:
                    await server.Events.InvokeItemUsedAsync(new ItemUsedEventArgs(player, player.Inventory.Items[player.Inventory.Size - 1 - (9 - player.CurrentSlot)], player.CurrentSlot));
                    break;
                case Hand.OffHand:
                    await server.Events.InvokeItemUsedAsync(new ItemUsedEventArgs(player, player.Inventory.Items.Last(), 9));
                    break;
            }
        }
    }
}

