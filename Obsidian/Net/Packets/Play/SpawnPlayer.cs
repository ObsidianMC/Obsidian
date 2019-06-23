using Obsidian.Entities;
using Obsidian.Util;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public class SpawnPlayer : Packet
    {
        public int Id { get; set; }

        public Guid Uuid { get; set; }

        public string Uuid3 { get; set; }

        public Transform Tranform { get; set; }

        public Player Player { get; set; }

        public SpawnPlayer() : base(0x05, new byte[0]) { }

        public override Task PopulateAsync()
        {
            throw new NotImplementedException();
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteVarIntAsync(this.Id);
                if (this.Uuid3 != null)
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(this.Uuid3));
                    Console.WriteLine("UUID is  null");
                }
                else
                {
                    Console.WriteLine("UUID is not null");
                    await stream.WriteUuidAsync(this.Uuid);
                }

                await stream.WriteDoubleAsync(this.Tranform.X);
                await stream.WriteDoubleAsync(this.Tranform.Y);
                await stream.WriteDoubleAsync(this.Tranform.Z);

                await stream.WriteAngleAsync(this.Tranform.Yaw);
                await stream.WriteAngleAsync(this.Tranform.Pitch);

                await Player.WriteAsync(stream);

                return stream.ToArray();
            }
        }
    }
}
