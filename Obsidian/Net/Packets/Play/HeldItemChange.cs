using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play {
	public class HeldItemChange : Packet {
		public short Slot { get; private set; }

		public HeldItemChange(short slot) : base(0x21, new byte[0]) {
			this.Slot = slot;
		}
		
		public HeldItemChange(byte[] data) : base(0x21, data) { }
		public override async Task<byte[]> ToArrayAsync() {
			using (var stream = new MinecraftStream())
			{
				await stream.WriteShortAsync(Slot);
				return stream.ToArray();
			}
		}
		public override async Task PopulateAsync() {
			using (var stream = new MinecraftStream(this.PacketData))
			{
				this.Slot = await stream.ReadShortAsync();
			}
		}
	}
}