using Obsidian.CommandFramework.Attributes;
using Obsidian.CommandFramework.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.CommandFramework
{
    public class CommandHandler
    {
        private Type _contextType;
        private List<Type> _commandClasses;
        private CommandParser _commandParser;

        public CommandHandler(string prefix)
        {
            this._commandParser = new CommandParser(prefix);
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

        public void RegisterCommandClass<T>() where T : BaseCommandClass
        {
            _commandClasses.Add(typeof(T));
        }

        public async Task ProcessCommand(BaseCommandContext ctx)
        {
            if (!this._contextType.IsAssignableFrom(ctx.GetType()))
            {
                throw new Exception("Your context does not match the registered context type.");
            }

            // split the command message into command and args.
            if(_commandParser.IsCommandQualified(ctx._message, out string qualified))
            {
                // if string is "command-qualified" we'll try to execute it.
                var command = _commandParser.SplitQualifiedString(qualified); // first, parse the command

                // [0] is the command name, all other values are arguments.
            }
            await Task.Yield();
        }

        private void executeCommand(string[] command, BaseCommandContext ctx)
        {
            var commandwithargs = searchForQualifiedMethods(this._commandClasses.ToArray(), command);
            // now find the methodinfo with the right amount of args and execute that
        }

        private (MethodInfo[] method, string[] args) searchForQualifiedMethods(Type[] types, string[] cmd)
        {
            // get args
            string[] args = cmd.Skip(1).ToArray();

            foreach(var cmdclass in types)
            {
                // gets methods with command attribute
                var qualifiedmethods = cmdclass.GetMethods()
                    .Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandAttribute) && (string)y.ConstructorArguments.First().Value == cmd[0]))
                    .ToArray();

                if(qualifiedmethods.Count() > 0)
                {
                    // return found methods
                    return (qualifiedmethods, args);
                }
                
                var qualifiedclasses = cmdclass.GetNestedTypes()
                    .Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandGroupAttribute) && (string)y.ConstructorArguments.First().Value == cmd[0]))
                    .ToArray();

                if(qualifiedclasses.Count() > 0)
                {
                    // repeat search on subclasses
                    return searchForQualifiedMethods(qualifiedclasses, args);
                }
            }

            throw new Exception("No qualified commands found");
        }
    }
}
