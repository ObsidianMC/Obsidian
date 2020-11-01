using Obsidian.API.Events;
using Obsidian.Blocks;
using Obsidian.Entities;
using Obsidian.Util.DataTypes;

namespace Obsidian.Events.EventArgs
{
    public class BlockInteractEventArgs : PlayerEventArgs, ICancellable
    {
        public bool Cancel { get; set; }

        /// <summary>
        /// The block that was interacted with
        /// </summary>
        public Block Block { get; }

        public Position Location { get; }

        public BlockInteractEventArgs(Player who, Block block, Position location) : base(who) 
        { 
            this.Block = block;
            this.Location = location;
        }


      
    }
}
