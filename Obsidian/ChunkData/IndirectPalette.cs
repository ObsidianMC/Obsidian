using Obsidian.Net;

namespace Obsidian.ChunkData;

public sealed class IndirectPalette<T> : IPalette<T>
{
    public int[] Values { get; }

    public int Size { get; set; }

    public bool IsFull => this.Values.Length == this.Size;

    public IndirectPalette(byte bitCount) => this.Values = new int[1 << bitCount];

    public T? GetValueFromIndex(int index)
    {
        if (index > this.Size - 1 || index < 0)
            throw new IndexOutOfRangeException($"Index({index}) > Size({this.Size - 1}) || Index({index}) < 0");

        if (typeof(T).IsEnum)
        {
            var name = Enum.GetName(typeof(T), this.Values[index]);

            return string.IsNullOrWhiteSpace(name) ? default : (T)Enum.Parse(typeof(T), name);
        }

        return (T)Activator.CreateInstance(typeof(T), this.Values[index]);
    }

    public int GetIdFromValue(T value)
    {
        //TODO try catch
        var valueId = value is Block block ? block.StateId : Convert.ToInt32(value);

        for(int id = 0; id < this.Size; id++)
        {
            if (this.Values[id] == valueId)
                return id;
        }

        if (this.IsFull)
        {
            Console.WriteLine("Full");
            return -1;
        }

        var newId = this.Size;

        this.Values[newId] = valueId;

        this.Size++;

        return newId;
    }

    public async Task ReadFromAsync(MinecraftStream stream)
    {
        var length = await stream.ReadVarIntAsync();

        for (int i = 0; i < length; i++)
        {
            int id = await stream.ReadVarIntAsync();

            this.Values[i] = id;
            this.Size++;
        }
    }

    public async Task WriteToAsync(MinecraftStream stream)
    {
        await stream.WriteVarIntAsync(this.Size);

        for (int i = 0; i < this.Size; i++)
            await stream.WriteVarIntAsync(this.Values[i]);
    }

    public void WriteTo(MinecraftStream stream)
    {
        stream.WriteVarInt(this.Size);

        for (int i = 0; i < this.Size; i++)
            stream.WriteVarInt(this.Values[i]);
    }
}


