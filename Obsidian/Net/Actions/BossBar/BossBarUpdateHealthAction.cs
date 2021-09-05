using System.Threading.Tasks;

namespace Obsidian.Net.Actions.BossBar
{
    public class BossBarUpdateHealthAction : BossBarAction
    {
        public float Health { get; set; }

        public BossBarUpdateHealthAction() : base(2) { }

        public override void WriteTo(MinecraftStream stream)
        {
            base.WriteTo(stream);

            stream.WriteFloat(Health);
        }

        public override async Task WriteToAsync(MinecraftStream stream)
        {
            await base.WriteToAsync(stream); 
            await stream.WriteFloatAsync(Health);
        }
    }
}
