using Microsoft.Extensions.DependencyInjection;
using Obsidian.API;
using Obsidian.API.Plugins;
using Obsidian.Commands.Framework.Entities;
using Obsidian.Commands.Framework.Exceptions;
using Obsidian.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Obsidian.Commands.Framework
{
    public class CommandHandler
    {
        internal List<Command> _commands;
        internal CommandParser _commandParser;
        internal List<BaseArgumentParser> _argumentParsers;
        internal string _prefix;
        internal PluginManager _plugins;

        public CommandHandler(string prefix)
        {
            this._commandParser = new CommandParser(prefix);
            this._commands = new List<Command>();
            this._argumentParsers = new List<BaseArgumentParser>();
            this._prefix = prefix;

            var parsers = typeof(StringArgumentParser).Assembly.GetTypes().Where(x => typeof(BaseArgumentParser).IsAssignableFrom(x) && !x.IsAbstract);
            // use reflection to find all predefined argument parsers

            foreach (var parser in parsers)
            {
                _argumentParsers.Add((BaseArgumentParser)Activator.CreateInstance(parser));
            }
        }

        public void LinkPluginManager(PluginManager plugins)
        {
            this._plugins = plugins;
        }

        public string FindMinecraftType(Type type)
        {
            if (_argumentParsers.Any(x => x.GetType().BaseType.GetGenericArguments()[0] == type))
            {
                // Gets parser
                var parserType = _argumentParsers.First(x => x.GetType().BaseType.GetGenericArguments()[0] == type).GetType();
                var parser = Activator.CreateInstance(parserType);

                return (string)parserType.GetMethod("GetParserIdentifier").Invoke(parser, null);
            }

            throw new Exception("No such parser registered!");
        }
        public Command[] GetAllCommands()
        {
            return _commands.ToArray();
        }

        public void AddArgumentParser(BaseArgumentParser parser)
        {
            _argumentParsers.Add(parser);
        }

        public void RegisterSingleCommand(Action method, PluginContainer plugin, Type t)
        {
            var m = method.Method;

            // Get command name from first constructor argument for command attribute.
            var cmd = m.GetCustomAttribute<CommandAttribute>();
            var name = cmd.CommandName;
            // Get aliases
            var aliases = cmd.Aliases;
            var checks = m.GetCustomAttributes<BaseExecutionCheckAttribute>();

            var info = m.GetCustomAttribute<CommandInfoAttribute>();
            var issuers = m.GetCustomAttribute<IssuerScopeAttribute>()?.Issuers ?? CommandIssuers.Client;

            var command = new Command(name, aliases, info?.Description ?? "", info?.Usage ?? "", null, checks.ToArray(), this, plugin, null, t, issuers);
            command.Overloads.Add(m);

            this._commands.Add(command);
        }

        public void UnregisterPluginCommands(PluginContainer plugin)
        {
            this._commands.RemoveAll(x => x.Plugin == plugin);
        }

        public void RegisterCommandClass<T>(PluginContainer plugin, T instance) => RegisterCommandClass(plugin, typeof(T), instance);

        public void RegisterCommandClass(PluginContainer plugin, Type t, object instance = null)
        {
            RegisterSubgroups(t, plugin);
            RegisterSubcommands(t, plugin, instance);
        }

        public async Task<object> CreateCommandRootInstance(Type t, PluginContainer plugin)
        {
            await Task.Yield();
            // get constructor with most params.
            var instance = Activator.CreateInstance(t);

            var injectables = t.GetProperties().Where(x => x.GetCustomAttribute<InjectAttribute>() != null);
            foreach(var injectable in injectables)
            {
                if(injectable.PropertyType == typeof(PluginBase) || injectable.PropertyType == plugin.Plugin.GetType())
                {
                    injectable.SetValue(instance, plugin.Plugin);
                }
                else
                {
                    this._plugins.serviceProvider.InjectServices(instance, plugin, _plugins.logger);
                }
            }

            return instance;
        }

        private void RegisterSubgroups(Type t, PluginContainer plugin, Command parent = null)
        {
            // find all command groups under this command
            var subtypes = t.GetNestedTypes().Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandGroupAttribute)));

            foreach (var st in subtypes)
            {
                var group = st.GetCustomAttribute<CommandGroupAttribute>();
                // Get command name from first constructor argument for command attribute.
                var name = group.GroupName;
                // Get aliases
                var aliases = group.Aliases;

                var checks = st.GetCustomAttributes<BaseExecutionCheckAttribute>();

                var info = st.GetCustomAttribute<CommandInfoAttribute>();
                var issuers = st.GetCustomAttribute<IssuerScopeAttribute>()?.Issuers ?? CommandIssuers.Client;

                var cmd = new Command(name, aliases.ToArray(), info?.Description ?? "", info?.Usage ?? "", parent, checks.ToArray(), this, plugin, null, t, issuers);

                RegisterSubgroups(st, plugin, cmd);
                RegisterSubcommands(st, plugin, cmd);

                this._commands.Add(cmd);
            }
        }

        private void RegisterSubcommands(Type t, PluginContainer plugin, object instance, Command parent = null)
        {
            // loop through methods and find valid commands
            var methods = t.GetMethods();

            if (parent != null)
            {
                // Adding all methods with GroupCommand attribute
                parent.Overloads.AddRange(methods.Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(GroupCommandAttribute))));
            }

            // Selecting all methods that have the CommandAttribute.
            foreach (var m in methods.Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandAttribute))))
            {
                // Get command name from first constructor argument for command attribute.
                var cmd = m.GetCustomAttribute<CommandAttribute>();
                var name = cmd.CommandName;
                // Get aliases
                var aliases = cmd.Aliases;
                var checks = m.GetCustomAttributes<BaseExecutionCheckAttribute>();

                var info = m.GetCustomAttribute<CommandInfoAttribute>();
                var issuers = m.GetCustomAttribute<IssuerScopeAttribute>()?.Issuers ?? CommandIssuers.Client;

                var command = new Command(name, aliases, info?.Description ?? "", info?.Usage ?? "", parent, checks.ToArray(), this, plugin, null, t, issuers);
                command.Overloads.Add(m);

                // Add overloads.
                command.Overloads.AddRange(methods.Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandOverloadAttribute)) && x.Name == m.Name));

                this._commands.Add(command);
            }
        }

        public async Task ProcessCommand(CommandContext ctx)
        {
            // split the command message into command and args.
            if (_commandParser.IsCommandQualified(ctx.Message, out ReadOnlyMemory<char> qualified))
            {
                // if string is "command-qualified" we'll try to execute it.
                var command = _commandParser.SplitQualifiedString(qualified.Span); // first, parse the command

                await ExecuteCommand(command, ctx);
            }
            await Task.Yield();
        }

        private async Task ExecuteCommand(string[] command, CommandContext ctx)
        {
            Command cmd = null;
            var args = command;

            // Search for correct Command class in this._commands.
            while (_commands.Any(x => x.CheckCommand(args, cmd)))
            {
                cmd = _commands.First(x => x.CheckCommand(args, cmd));
                args = args.Skip(1).ToArray();
            }

            if (cmd != null)
            {
                ctx.Plugin = cmd.Plugin?.Plugin;

                await cmd.ExecuteAsync(ctx, args);
            }
            else
                throw new CommandNotFoundException("No such command was found!");
        }
    }
}
