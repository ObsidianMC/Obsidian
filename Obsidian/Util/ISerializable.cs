using System.Threading.Tasks;

namespace Obsidian.Util
{
    public interface ISerializable
    {
        Task<byte[]> ToArrayAsync(); 
    }
}
