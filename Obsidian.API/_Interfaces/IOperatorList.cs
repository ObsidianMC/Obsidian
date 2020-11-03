namespace Obsidian.API
{
    public interface IOperatorList
    {
        public void AddOperator(IPlayer player);
        public bool CreateRequest(IPlayer player);
        public bool ProcessRequest(IPlayer player, string code);
        public void AddOperator(string username);
        public void RemoveOperator(IPlayer player);
        public void RemoveOperator(string username);
        public bool IsOperator(IPlayer p);
    }
}
