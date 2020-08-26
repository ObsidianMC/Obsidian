using Obsidian.Serializer.Enums;
using System;

namespace Obsidian.Serializer.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class WriteMethod : Attribute, IStreamMethod
    {
        public DataType Type { get; set; }

        public WriteMethod(DataType type)
        {
            Type = type;
        }
    }
}
