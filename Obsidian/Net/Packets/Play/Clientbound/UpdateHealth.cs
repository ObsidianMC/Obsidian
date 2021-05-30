using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class UpdateHealth : IClientboundPacket
    {
        [Field(0)]
        public float Health { get; set; }

        [Field(1), VarLength]
        public int Food { get; set; }

        [Field(2)]
        public float FoodSaturation { get; set; }

        public int Id => 0x49;

        public UpdateHealth(float health, int food, float foodSaturation)
        {
            Health = health;
            Food = food;
            FoodSaturation = foodSaturation;
        }
    }
}
