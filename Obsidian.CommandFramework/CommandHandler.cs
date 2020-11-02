using Obsidian.CommandFramework.ArgumentParsers;
using Obsidian.CommandFramework.Attributes;
using Obsidian.CommandFramework.Entities;
using Obsidian.CommandFramework.Exceptions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.CommandFramework
{
    public class CommandHandler
    {
        internal Type _contextType;
        internal List<Command> _commands;
        internal CommandParser _commandParser;
        internal List<BaseArgumentParser> _argumentParsers;

        public CommandHandler(string prefix)
        {
            this._commandParser = new CommandParser(prefix);
            this._commands = new List<Command>();
            this._argumentParsers = new List<BaseArgumentParser>();

            var parsers = typeof(BaseArgumentParser).Assembly.GetTypes().Where(x => typeof(BaseArgumentParser).IsAssignableFrom(x) && !x.IsAbstract);
            // use reflection to find all predefined argument parsers

            foreach (var parser in parsers)
            {
                _argumentParsers.Add((BaseArgumentParser)Activator.CreateInstance(parser));
            }
        }

        public string FindMinecraftType(Type t)
        {
            if (this._argumentParsers.Any(x => x.GetType().BaseType.GetGenericArguments()[0] == t))
            {
                // Gets parser
                var parsertype = this._argumentParsers.First(x => x.GetType().BaseType.GetGenericArguments()[0] == t).GetType();
                var parser = Activator.CreateInstance(parsertype);

                return (string)parsertype.GetMethod("GetParserIdentifier").Invoke(parser, null);
            }
            Console.WriteLine("big oopsie");
            throw new Exception("No such parser registered!");
        }
        public Command[] GetAllCommands()
        {
            return _commands.ToArray();
        }

        public void AddArgumentParser(BaseArgumentParser parser)
        {
            this._argumentParsers.Add(parser);
        }

        public void RegisterContextType<T>() where T : BaseCommandContext
        {
            this._contextType = typeof(T);
        }

        public void RegisterCommandClass<T>() where T : BaseCommandClass
        {
            var t = typeof(T);

            registerSubgroups(t);
            registerSubcommands(t);
        }

        private void registerSubgroups(Type t, Command parent = null)
        {
            // find all command groups under this command
            var subtypes = t.GetNestedTypes().Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandGroupAttribute)));

            foreach(var st in subtypes)
            {
                // Get command name from first constructor argument for command attribute.
                var name = (string)st.CustomAttributes.First(x => x.AttributeType == typeof(CommandGroupAttribute)).ConstructorArguments[0].Value;
                // Get aliases
                var baliases = (ReadOnlyCollection<System.Reflection.CustomAttributeTypedArgument>)st.CustomAttributes.First(x => x.AttributeType == typeof(CommandGroupAttribute)).ConstructorArguments[1].Value;
                var aliases = baliases.Select(x => (string)x.Value);

                var checks = st.CustomAttributes.Where(x => typeof(BaseExecutionCheckAttribute).IsAssignableFrom(x.AttributeType))
                    .Select(x => (BaseExecutionCheckAttribute)Activator.CreateInstance(x.AttributeType)).ToArray();

                var desc = "";
                var usage = "";

                if (st.CustomAttributes.Any(x => x.AttributeType == typeof(CommandInfoAttribute)))
                {
                    var args = st.CustomAttributes.First(x => x.AttributeType == typeof(CommandInfoAttribute)).ConstructorArguments;
                    desc = (string)args[0].Value;
                    if (args.Count >= 2) usage = (string)args[1].Value;
                }

                var cmd = new Command(name, aliases.ToArray(), desc, usage, parent, checks, this);

                registerSubgroups(st, cmd);
                registerSubcommands(st, cmd);

                this._commands.Add(cmd);
            }
        }

        private void registerSubcommands(Type t, Command parent = null)
        {
            // loop through methods and find valid commands
            var methods = t.GetMethods();

            if(parent != null)
            {
                // Adding all methods with GroupCommand attribute
                parent.Overloads.AddRange(methods.Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(GroupCommandAttribute))));
            }

            // Selecting all methods that have the CommandAttribute.
            foreach(var m in methods.Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandAttribute))))
            {
                // Get command name from first constructor argument for command attribute.
                var name = (string)m.CustomAttributes.First(x => x.AttributeType == typeof(CommandAttribute)).ConstructorArguments[0].Value;
                // Get aliases
                var baliases = (ReadOnlyCollection<System.Reflection.CustomAttributeTypedArgument>)m.CustomAttributes.First(x => x.AttributeType == typeof(CommandAttribute)).ConstructorArguments[1].Value;
                var aliases = baliases.Select(x => (string)x.Value);
                var checks = m.CustomAttributes.Where(x => typeof(BaseExecutionCheckAttribute).IsAssignableFrom(x.AttributeType))
                    .Select(x => (BaseExecutionCheckAttribute)Activator.CreateInstance(x.AttributeType)).ToArray();

                var desc = "";
                var usage = "";

                if (m.CustomAttributes.Any(x => x.AttributeType == typeof(CommandInfoAttribute)))
                {
                    var args = m.CustomAttributes.First(x => x.AttributeType == typeof(CommandInfoAttribute)).ConstructorArguments;
                    desc = (string)args[0].Value;
                    if(args.Count >= 2) usage = (string)args[1].Value;
                }

                var cmd = new Command(name, aliases.ToArray(), desc, usage, parent, checks, this);
                cmd.Overloads.Add(m);

                // Add overloads.
                cmd.Overloads.AddRange(methods.Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandOverloadAttribute)) && x.Name == m.Name));

                this._commands.Add(cmd);
            }
        }

        public async Task ProcessCommand(BaseCommandContext ctx)
        {
            ctx.Commands = this;

            if (!this._contextType.IsAssignableFrom(ctx.GetType()))
            {
                throw new InvalidCommandContextTypeException("Your context does not match the registered context type.");
            }

            // split the command message into command and args.
            if (_commandParser.IsCommandQualified(ctx._message, out string qualified))
            {
                // if string is "command-qualified" we'll try to execute it.
                var command = _commandParser.SplitQualifiedString(qualified); // first, parse the command

                // [0] is the command name, all other values are arguments.
                await executeCommand(command, ctx);
            }
            await Task.Yield();
        }

        private async Task executeCommand(string[] command, BaseCommandContext ctx)
        {
            Command cmd = null;
            var args = command;

            // Search for correct Command class in this._commands.
            while(_commands.Any(x => x.CheckCommand(args, cmd)))
            {
                cmd = _commands.First(x => x.CheckCommand(args, cmd));
                args = args.Skip(1).ToArray();
            }

            if (cmd != null)
                await cmd.ExecuteAsync(ctx, args);
            else
                throw new CommandNotFoundException("No such command was found!");
        }
    }
}
