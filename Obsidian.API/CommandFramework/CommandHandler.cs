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
                var group = st.GetCustomAttribute<CommandGroupAttribute>();
                // Get command name from first constructor argument for command attribute.
                var name = group.GroupName;
                // Get aliases
                var aliases = group.Aliases;

                var checks = st.GetCustomAttributes<BaseExecutionCheckAttribute>();

                var desc = "";
                var usage = "";

                var info = st.GetCustomAttribute<CommandInfoAttribute>();

                var cmd = new Command(name, aliases.ToArray(), info?.Description ?? "", info?.Usage ?? "", parent, checks.ToArray(), this);

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
                var cmd = m.GetCustomAttribute<CommandAttribute>();
                var name = cmd.CommandName;
                // Get aliases
                var aliases = cmd.Aliases;
                var checks = m.GetCustomAttributes<BaseExecutionCheckAttribute>();

                var info = m.GetCustomAttribute<CommandInfoAttribute>();

                var command = new Command(name, aliases, info?.Description ?? "", info?.Usage ?? "", parent, checks.ToArray(), this);
                command.Overloads.Add(m);

                // Add overloads.
                command.Overloads.AddRange(methods.Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandOverloadAttribute)) && x.Name == m.Name));

                this._commands.Add(command);
            }
        }

        public async Task ProcessCommand(ObsidianContext ctx)
        {
            ctx.Commands = this;

            // split the command message into command and args.
            if (_commandParser.IsCommandQualified(ctx.Message, out string qualified))
            {
                // if string is "command-qualified" we'll try to execute it.
                var command = _commandParser.SplitQualifiedString(qualified); // first, parse the command

                // [0] is the command name, all other values are arguments.
                await executeCommand(command, ctx);
            }
            await Task.Yield();
        }

        private async Task executeCommand(string[] command, ObsidianContext ctx)
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
