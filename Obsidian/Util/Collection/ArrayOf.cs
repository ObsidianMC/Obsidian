namespace Obsidian.Util.Collection
{
    public static class ArrayOf<T> where T : new()
    {
        public static T[] Create(int size, T initialValue)
        {
            var array = new T[size];
            for (var i = 0; i < array.Length; i++)
                array[i] = initialValue;
            return array;
        }

        public static T[] Create(int size)
        {
            var array = new T[size];
            for (var i = 0; i < array.Length; i++)
                array[i] = new T();
            return array;
        }
    }
}
