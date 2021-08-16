using System;

namespace Obsidian.WorldData.Generators.Modules
{
    public class ModuleCompilationException : Exception
    {
        public ModuleCompilationException(string message) : base(message)
        {
        }

        public ModuleCompilationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
