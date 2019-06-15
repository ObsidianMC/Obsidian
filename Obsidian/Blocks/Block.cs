namespace Obsidian.Blocks
{
    public class Block
    {
       
    }

    public class BlockState
    {
        public int Id { get; }
        public int Data { get; }

        public BlockState(int id, int data)
        {
            this.Id = id;
            this.Data = data;
        }


    }

    public enum BlockFace
    {
        Down,

        Up,

        North,

        South,

        West,

        East
    }
}
