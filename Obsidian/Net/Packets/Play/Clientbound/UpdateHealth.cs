using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class UpdateHealth : IClientboundPacket
    {
        [Field(0)]
        public float Health { get; }

        [Field(1), VarLength]
        public int Food { get; }

        [Field(2)]
        public float FoodSaturation { get; }

        public int Id => 0x52;

        public UpdateHealth(float health, int food, float foodSaturation)
        {
            Health = health;
            Food = food;
            FoodSaturation = foodSaturation;
        }
    }
}
