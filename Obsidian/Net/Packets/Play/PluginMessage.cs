namespace Obsidian.Net.Packets
{
    //public abstract class PluginMessage : Packet
    //{
    //    public PluginMessage() : base(0x0E, new byte[0])
    //    {
    //    }
    //
    //    public abstract string Channel { get; }
    //
    //    public abstract Task ToDataArrayAsync(Stream stream);
    //
    //    public abstract Task PopulateFromDataAsync(Stream stream);
    //
    //    protected override async Task PopulateAsync()
    //    {
    //        throw new NotImplementedException();
    //        using (var stream = new MemoryStream(this._packetData))
    //        {
    //            this.Channel = await stream.ReadStringAsync();
    //          
    //        }
    //    }
    //
    //    public override async Task<byte[]> ToArrayAsync()
    //    {
    //        using (var stream = new MemoryStream())
    //        {
    //            await stream.WriteStringAsync(this.Channel);
    //            await stream.WriteAsync(this.Data);
    //            return stream.ToArray();
    //        }
    //    }
    //}
}