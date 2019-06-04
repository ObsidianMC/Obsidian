using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Util
{
    public interface ISerializable
    {
        Task<byte[]> ToArrayAsync(); 
    }
}
