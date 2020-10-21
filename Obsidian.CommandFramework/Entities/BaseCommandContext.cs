using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework.Entities
{
    public class BaseCommandContext
    {
        internal string _message;
        public CommandHandler Commands { get; internal set; }

        /// <summary>
        /// Constructs a new BaseCommandContext
        /// </summary>
        /// <param name="message">Full command text (without prefix)</param>
        public BaseCommandContext(string message)
        {
            this._message = message;
        }
    }
}
