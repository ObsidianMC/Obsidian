using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class UpdateHealth: ISerializablePacket
    {
         [Field(0)] public float Health { get; set; }
         
         [VarLength]
         [Field(1)] public int Food { get; set; }
         
         [Field(2)] public float FoodSaturation { get; set; }
         
         public int Id => 0x49;
 
         public UpdateHealth(float health, int food, float foodSaturation)
         {
             Health = health;
             Food = food;
             FoodSaturation = foodSaturation;
         }
         
         public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;
 
         public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
        
        
    }
}