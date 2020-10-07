using Obsidian.CommandFramework.Attributes;
using Obsidian.CommandFramework.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.CommandFramework
{
    public class CommandHandler
    {
        private Type _contextType;
        private List<Type> _commandTypes;

        public CommandHandler()
        {
            
        }

        public void RegisterContextType<T>()
        {
            if (typeof(BaseCommandContext).IsAssignableFrom(typeof(T)))
            {
                this._contextType = typeof(T);
                return;
            }

            throw new Exception("BaseCommandContext is not assignable from your Type!");
        }

        public void RegisterCommandClass<T>()
        {
            if(typeof(BaseCommandClass).IsAssignableFrom(typeof(T)))
            {
                this._commandTypes.Add(typeof(T));
                return;
            }

            throw new Exception("BaseCommandClass is not assignable from your Type!");
        }

        public async Task ProcessCommand(BaseCommandContext ctx)
        {
            if(!this._contextType.IsAssignableFrom(ctx.GetType()))
            {
                throw new Exception("Your context does not match the registered context type.");
            }

            // split the command message into command and args.
            var args = _parseCommand(ctx._message);

            // Find class and command to execute, this is taken from a private project.
            // TODO fix and update this.
            foreach (Type typ in this._commandTypes)
            {
                foreach (var method in typ.GetMethods())
                {
                    if (method.CustomAttributes.Any(x => x.AttributeType == typeof(CommandAttribute)))
                    {
                        var attribute = method.CustomAttributes.First(x => x.AttributeType == typeof(CommandAttribute));
                        var commandname = (string)attribute.ConstructorArguments.First().Value;
                        Console.WriteLine($"found a method with command name {commandname}");
                    }
                }
            }

            await Task.Yield();
        }

        #region parsing text
        private List<string> _parseCommand(string msg)
        {
            var list = new List<string>();

            StringBuilder buffer = new StringBuilder();
            bool quote = false;
            bool escape = false;

            for(int i = 0; i < msg.Length; i++)
            {
                if(!quote) // in quote
                {
                    if(msg[i] == ' ')
                    {
                        // on space, next word
                        list.Add(buffer.ToString());
                        buffer.Clear(); // clear buffer
                        continue;
                    }
                    else if(msg[i] == '"')
                    {
                        // enter quote
                        quote = true;
                        continue;
                    }
                }
                else
                {
                    if(msg[i] == '"' && !escape)
                    {
                        // exit quotes
                        quote = false;
                        continue;
                    }
                    else if(msg[i] == '\\')
                    {
                        // escape next quote
                        escape = true;
                        continue;
                    }

                    if(escape) // handle special chars like \n, \r, etc..
                    {
                        // TODO: add more cases
                        if(msg[i] == 'n')
                        {
                            buffer.Append('\n');
                            continue;
                        }
                        else if(msg[i] == 'r')
                        {
                            buffer.Append('\r');
                            continue;
                        }
                    }
                }

                // add to buffer
                buffer.Append(msg[i]);
            }

            // ensuring last parameter is added.
            if(buffer.Length < 1)
            {
                list.Add(buffer.ToString());
            }

            return list;
        }
        #endregion
    }
}
