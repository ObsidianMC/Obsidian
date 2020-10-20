using Obsidian.CommandFramework.Attributes;
using Obsidian.CommandFramework.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.CommandFramework
{
    public class CommandHandler
    {
        private Type _contextType;
        private List<BaseCommandClass> _commandClasses;

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

        public void RegisterCommandClass<T>() where T : BaseCommandClass
        {
            _commandClasses.Add((BaseCommandClass)Activator.CreateInstance(typeof(T)));
        }

        public async Task ProcessCommand(BaseCommandContext ctx)
        {
            if (!this._contextType.IsAssignableFrom(ctx.GetType()))
            {
                throw new Exception("Your context does not match the registered context type.");
            }

            // split the command message into command and args.
            var args = ParseCommand(ctx._message);

            // finding the right command method
            var commands = _commandClasses.SelectMany(x => x.GetType().GetMethods())
                .Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandAttribute)));

            // shitty complex linq statement that basically gets our command. maybe could use optimization. idk.
            var method = commands.First(x => (string)x.CustomAttributes.First(y => y.AttributeType == typeof(CommandAttribute)).ConstructorArguments.First().Value == args[0]);

            // TODO parse args. commands have no args rn.

            // use this methodinfo to run?
            method.Invoke(_commandClasses.First(x => x.GetType() == method.DeclaringType), null);

            await Task.Yield();
        }

        #region Parsing text
        internal List<string> ParseCommand(string text)
        {
            var list = new List<string>();
            return list;
        }
        #endregion
    }
}
