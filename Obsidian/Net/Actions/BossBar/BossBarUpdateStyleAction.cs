using Obsidian.API.Boss;
using System.Threading.Tasks;

namespace Obsidian.Net.Actions.BossBar
{
    public class BossBarUpdateStyleAction : BossBarAction
    {
        public BossBarColor Color { get; set; }
        public BossBarDivisionType Division { get; set; }

        public BossBarUpdateStyleAction() : base(4) { }

        public override void WriteTo(MinecraftStream stream)
        {
            base.WriteTo(stream);

            stream.WriteVarInt(Color);
            stream.WriteVarInt(Division);
        }

        public override async Task WriteToAsync(MinecraftStream stream)
        {
            await base.WriteToAsync(stream);

            await stream.WriteVarIntAsync(Color);
            await stream.WriteVarIntAsync(Division);
        }
    }
}
