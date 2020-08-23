using Obsidian.Serializer.Enums;
using System;

namespace Obsidian.Serializer.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class ReadMethod : Attribute, IStreamMethod
    {
        public DataType Type { get; set; }

        public ReadMethod(DataType type)
        {
            Type = type;
        }
    }
}
