using Obsidian.CommandFramework.ArgumentParsers;
using Obsidian.CommandFramework.Attributes;
using Obsidian.CommandFramework.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private List<BaseArgumentParser> _argumentParsers;

        public CommandHandler(string prefix)
        {
            this._commandParser = new CommandParser(prefix);
            this._commandClasses = new List<Type>();
            this._argumentParsers = new List<BaseArgumentParser>();

            var parsers = typeof(BaseArgumentParser).Assembly.GetTypes().Where(x => typeof(BaseArgumentParser).IsAssignableFrom(x) && !x.IsAbstract);
            // use reflection to find all predefined argument parsers

            foreach (var parser in parsers)
            {
                _argumentParsers.Add((BaseArgumentParser)Activator.CreateInstance(parser));
            }
        }

        public void AddArgumentParser(BaseArgumentParser parser)
        {
            this._argumentParsers.Add(parser);
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

        public CommandInfo[] GetAllCommands()
        {
            List<CommandInfo> infos = new List<CommandInfo>();

            foreach(var c in _commandClasses)
            {
                infos.AddRange(GetSubCommands(c));
            }

            return infos.ToArray();
        }

        private CommandInfo[] GetSubCommands(Type t, string parent = "")
        {
            List<CommandInfo> infos = new List<CommandInfo>();

            var classes = t.GetNestedTypes().Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandGroupAttribute)));
            foreach(var c in classes)
            {
                infos.AddRange(GetSubCommands(c, string.Join(' ', parent, (string)c.CustomAttributes.First(x => x.AttributeType == typeof(CommandGroupAttribute)).ConstructorArguments.First().Value).Trim(' ')));
            }

            foreach(var m in t.GetMethods().Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandAttribute))))
            {
                string desc = "";
                if(m.CustomAttributes.Any(x => x.AttributeType == typeof(CommandInfoAttribute)))
                {
                    desc = (string)m.CustomAttributes.First(x => x.AttributeType == typeof(CommandInfoAttribute)).ConstructorArguments.First().Value;
                }
                infos.Add(new CommandInfo(string.Join(' ', parent, (string)m.CustomAttributes.First(y => y.AttributeType == typeof(CommandAttribute)).ConstructorArguments.First().Value).Trim(' '), desc, GetParams(m)));
            }

            return infos.ToArray();
        }

        private CommandParam[] GetParams(MethodInfo method)
            => method.GetParameters().Skip(1).Select(x => new CommandParam(x.Name, x.ParameterType)).ToArray();

        public async Task ProcessCommand(BaseCommandContext ctx)
        {
            ctx.Commands = this;

            if (!this._contextType.IsAssignableFrom(ctx.GetType()))
            {
                throw new Exception("Your context does not match the registered context type.");
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
            var qualified = searchForQualifiedMethods(this._commandClasses.ToArray(), command);
            // now find the methodinfo with the right amount of args and execute that

            var method = qualified.method.First(x => x.GetParameters().Count() - 1 == qualified.args.Count());

            var obj = Activator.CreateInstance(method.DeclaringType);

            var methodparams = method.GetParameters().Skip(1).ToArray();

            var parsedargs = new object[methodparams.Length + 1];
            parsedargs[0] = (object)ctx;

            for (int i = 0; i < methodparams.Length; i++)
            {
                var paraminfo = methodparams[i];
                var arg = qualified.args[i];

                if (_argumentParsers.Any(x => x.GetType().BaseType.GetGenericArguments()[0] == paraminfo.ParameterType))
                {
                    var parsertype = _argumentParsers.First(x => x.GetType().BaseType.GetGenericArguments()[0] == paraminfo.ParameterType).GetType();
                    var parser = Activator.CreateInstance(parsertype);

                    var parseargs = new object[3] { (object)arg, (object)ctx, null };

                    // cast with reflection?
                    if ((bool)parsertype.GetMethod("TryParseArgument").Invoke(parser, parseargs))
                    {
                        parsedargs[i + 1] = parseargs[2];
                    }
                    else
                    {
                        throw new Exception("Invalid arguments!");
                    }
                }
                else
                {
                    throw new Exception("Invalid arguments!");
                }
            }

            // do execution checks
            var checks = method.CustomAttributes.Where(x => typeof(BaseExecutionCheckAttribute).IsAssignableFrom(x.AttributeType));

            foreach(var c in checks)
            {
                var check = (BaseExecutionCheckAttribute)Activator.CreateInstance(c.AttributeType);
                if(!await check.RunChecksAsync(ctx))
                {
                    throw new Exception("One or more execution checks failed!");
                }
            }

            var task = (Task)method.Invoke(obj, parsedargs);

            await task;
        }

        private (MethodInfo[] method, string[] args) searchForQualifiedMethods(Type[] types, string[] cmd)
        {
            // get args
            string[] args = cmd.Skip(1).ToArray();

            foreach (var cmdclass in types)
            {
                // gets methods with command attribute
                var qualifiedmethods = cmdclass.GetMethods()
                    .Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandAttribute) && (string)y.ConstructorArguments.First().Value == cmd[0]))
                    .ToArray();

                if (qualifiedmethods.Count() > 0)
                {
                    // return found methods
                    return (qualifiedmethods, args);
                }

                var qualifiedclasses = cmdclass.GetNestedTypes()
                    .Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandGroupAttribute) && (string)y.ConstructorArguments.First().Value == cmd[0]))
                    .ToArray();

                if (qualifiedclasses.Count() > 0)
                {
                    // repeat search on subclasses
                    return searchForQualifiedMethods(qualifiedclasses, args);
                }
            }

            throw new Exception("No qualified commands found");
        }
    }
}
