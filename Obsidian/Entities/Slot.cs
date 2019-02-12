namespace Obsidian.Entities
{
    ///<summary>
    ///https://wiki.vg/Slot_Data
    ///</summary>
    public class Slot
    {
        public Slot(bool present, int itemId, byte itemCount, object nbt)
        {
            this.Present = present;
            if (this.Present)
            {
                this.ItemId = itemId;
                this.ItemCount = itemCount;
                //this.NBT = nbt;
            }
        }


        public bool Present { get; set; }

        public int ItemId { get; set; }

        public byte ItemCount { get; set; }

        //public NBT NBT;
    }
}