using System.Collections;
using BitOperations = System.Numerics.BitOperations;

namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// A set of block positions that iterates in exactly the same order as vanilla's <c>java.util.HashSet&lt;BlockPos&gt;</c>.
/// </summary>
/// <remarks>
/// Tree placement iterates hash sets of positions (decorator lists are stable-sorted copies of them, and the leaf
/// distance pass pops "the first" element), so random-call pairing and leaf <c>distance</c> values depend on Java's
/// iteration order. This is a port of the parts of <c>java.util.HashMap</c> that decide that order: the
/// <c>Vec3i.hashCode</c> spread into a power-of-two table, order-preserving resize splits and red-black tree bins
/// (including <c>moveRootToFront</c>). Only for distinct positions with an identical 32-bit hash inside a tree bin
/// does Java fall back to <c>System.identityHashCode</c>, which isn't reproducible; this port orders them by
/// Y, Z, X instead.
/// </remarks>
public sealed class VanillaBlockPosSet : IEnumerable<Vector>
{
    private const int DefaultInitialCapacity = 16;
    private const float LoadFactor = 0.75f;
    private const int TreeifyThreshold = 8;
    private const int UntreeifyThreshold = 6;
    private const int MinTreeifyCapacity = 64;

    private Node?[]? table;
    private int threshold;
    private int modCount;

    public int Count { get; private set; }

    /// <summary>Java <c>HashSet.add</c>. Returns <c>false</c> if the position was already present.</summary>
    public bool Add(Vector position)
    {
        var hash = Hash(position);
        if (this.table is null || this.table.Length == 0)
            this.Resize();

        var tab = this.table!;
        var n = tab.Length;
        var i = (n - 1) & hash;
        var p = tab[i];
        if (p is null)
        {
            tab[i] = Pool.NewNode(hash, position, null);
        }
        else
        {
            Node? existing;
            if (p.Hash == hash && p.Key == position)
            {
                existing = p;
            }
            else if (p is TreeNode treeNode)
            {
                existing = treeNode.PutTreeVal(this, tab, hash, position);
            }
            else
            {
                for (var binCount = 0; ; binCount++)
                {
                    existing = p.Next;
                    if (existing is null)
                    {
                        p.Next = Pool.NewNode(hash, position, null);
                        if (binCount >= TreeifyThreshold - 1)
                            this.TreeifyBin(tab, hash);
                        break;
                    }

                    if (existing.Hash == hash && existing.Key == position)
                        break;

                    p = existing;
                }
            }

            if (existing is not null)
                return false;
        }

        this.modCount++;
        if (++this.Count > this.threshold)
            this.Resize();

        return true;
    }

    public void AddAll(IEnumerable<Vector> positions)
    {
        foreach (var position in positions)
            this.Add(position);
    }

    public bool Contains(Vector position) => this.GetNode(position) is not null;

    /// <summary>Java <c>HashSet.remove</c>.</summary>
    public bool Remove(Vector position) => this.RemoveNode(Hash(position), position, movable: true) is not null;

    /// <summary>
    /// Removes and returns the first element in iteration order, like <c>iterator().next()</c> followed by
    /// <c>iterator.remove()</c>.
    /// </summary>
    public Vector RemoveFirst()
    {
        var first = this.FirstNode() ?? throw new InvalidOperationException("The set is empty.");
        this.RemoveNode(first.Hash, first.Key, movable: false);

        // Removing the first element ends any enumeration, so nothing refers to the node anymore.
        var key = first.Key;
        Pool.ReturnNode(first);
        return key;
    }

    /// <summary>
    /// Empties the set and gives its storage back to a pool of the thread, for sets that aren't used anymore. The set can
    /// be reused, starting like a new one.
    /// </summary>
    public void Release()
    {
        var tab = this.table;
        if (tab is not null)
        {
            for (var i = 0; i < tab.Length; i++)
            {
                var e = tab[i];
                tab[i] = null;

                // Tree bins are rare; their nodes are left to the garbage collector.
                while (e is not null && e is not TreeNode)
                {
                    var next = e.Next;
                    Pool.ReturnNode(e);
                    e = next;
                }
            }

            Pool.ReturnTable(tab);
        }

        this.table = null;
        this.threshold = 0;
        this.Count = 0;
        this.modCount++;
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<Vector> IEnumerable<Vector>.GetEnumerator() => this.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    /// <summary>Java <c>Vec3i.hashCode()</c> spread like <c>HashMap.hash</c>.</summary>
    private static int Hash(Vector position)
    {
        var h = unchecked((position.Y + position.Z * 31) * 31 + position.X);
        return h ^ (h >>> 16);
    }

    /// <summary>Ordering used only for equal hashes in tree bins (see remarks).</summary>
    private static int TieBreakOrder(Vector a, Vector b)
    {
        var compared = a.Y.CompareTo(b.Y);
        if (compared == 0)
            compared = a.Z.CompareTo(b.Z);
        if (compared == 0)
            compared = a.X.CompareTo(b.X);

        return compared <= 0 ? -1 : 1;
    }

    private Node? FirstNode()
    {
        if (this.table is null || this.Count == 0)
            return null;

        foreach (var node in this.table)
        {
            if (node is not null)
                return node;
        }

        return null;
    }

    private Node? GetNode(Vector key)
    {
        var tab = this.table;
        if (tab is null || tab.Length == 0)
            return null;

        var hash = Hash(key);
        var first = tab[(tab.Length - 1) & hash];
        if (first is null)
            return null;

        if (first.Hash == hash && first.Key == key)
            return first;

        var e = first.Next;
        if (e is null)
            return null;

        if (first is TreeNode treeFirst)
            return treeFirst.GetTreeNode(hash, key);

        do
        {
            if (e.Hash == hash && e.Key == key)
                return e;
        }
        while ((e = e.Next) is not null);

        return null;
    }

    private Node?[] Resize()
    {
        var oldTab = this.table;
        var oldCap = oldTab?.Length ?? 0;
        var oldThr = this.threshold;
        int newCap, newThr = 0;

        if (oldCap > 0)
        {
            newCap = oldCap << 1;
            if (oldCap >= DefaultInitialCapacity)
                newThr = oldThr << 1;
        }
        else if (oldThr > 0)
        {
            newCap = oldThr;
        }
        else
        {
            newCap = DefaultInitialCapacity;
            newThr = (int)(LoadFactor * DefaultInitialCapacity);
        }

        if (newThr == 0)
            newThr = (int)(newCap * LoadFactor);

        this.threshold = newThr;
        var newTab = Pool.RentTable(newCap);
        this.table = newTab;

        if (oldTab is null)
            return newTab;

        for (var j = 0; j < oldCap; j++)
        {
            var e = oldTab[j];
            if (e is null)
                continue;

            oldTab[j] = null;
            if (e.Next is null)
            {
                newTab[e.Hash & (newCap - 1)] = e;
            }
            else if (e is TreeNode treeNode)
            {
                treeNode.Split(newTab, j, oldCap);
            }
            else
            {
                // Preserve relative order in the low and high halves.
                Node? loHead = null, loTail = null, hiHead = null, hiTail = null;
                do
                {
                    var next = e.Next;
                    if ((e.Hash & oldCap) == 0)
                    {
                        if (loTail is null)
                            loHead = e;
                        else
                            loTail.Next = e;
                        loTail = e;
                    }
                    else
                    {
                        if (hiTail is null)
                            hiHead = e;
                        else
                            hiTail.Next = e;
                        hiTail = e;
                    }

                    e = next;
                }
                while (e is not null);

                if (loTail is not null)
                {
                    loTail.Next = null;
                    newTab[j] = loHead;
                }

                if (hiTail is not null)
                {
                    hiTail.Next = null;
                    newTab[j + oldCap] = hiHead;
                }
            }
        }

        // Every entry of the old table was cleared while moving it.
        Pool.ReturnTable(oldTab);
        return newTab;
    }

    private void TreeifyBin(Node?[] tab, int hash)
    {
        if (tab.Length < MinTreeifyCapacity)
        {
            this.Resize();
            return;
        }

        var index = (tab.Length - 1) & hash;
        var e = tab[index];
        if (e is null)
            return;

        TreeNode? head = null, tail = null;
        do
        {
            var p = new TreeNode(e.Hash, e.Key, null);
            if (tail is null)
            {
                head = p;
            }
            else
            {
                p.Prev = tail;
                tail.Next = p;
            }

            tail = p;
        }
        while ((e = e.Next) is not null);

        tab[index] = head;
        head?.Treeify(tab);
    }

    private Node? RemoveNode(int hash, Vector key, bool movable)
    {
        var tab = this.table;
        if (tab is null || tab.Length == 0)
            return null;

        var index = (tab.Length - 1) & hash;
        var p = tab[index];
        if (p is null)
            return null;

        Node? node = null;
        if (p.Hash == hash && p.Key == key)
        {
            node = p;
        }
        else if (p.Next is not null)
        {
            if (p is TreeNode treeFirst)
            {
                node = treeFirst.GetTreeNode(hash, key);
            }
            else
            {
                var e = p.Next;
                do
                {
                    if (e.Hash == hash && e.Key == key)
                    {
                        node = e;
                        break;
                    }

                    p = e;
                }
                while ((e = e.Next) is not null);
            }
        }

        if (node is null)
            return null;

        if (node is TreeNode treeNode)
            treeNode.RemoveTreeNode(tab, movable);
        else if (node == p)
            tab[index] = node.Next;
        else
            p.Next = node.Next;

        this.modCount++;
        this.Count--;
        return node;
    }

    private static Node? Untreeify(Node? head)
    {
        Node? hd = null, tl = null;
        for (var q = head; q is not null; q = q.Next)
        {
            var p = Pool.NewNode(q.Hash, q.Key, null);
            if (tl is null)
                hd = p;
            else
                tl.Next = p;
            tl = p;
        }

        return hd;
    }

    /// <summary>Iterates in Java <c>HashMap</c> order: table slots in order, each bin in its linked order.</summary>
    public struct Enumerator : IEnumerator<Vector>
    {
        private readonly VanillaBlockPosSet set;
        private readonly int expectedModCount;
        private Node? next;
        private int index;

        internal Enumerator(VanillaBlockPosSet set)
        {
            this.set = set;
            this.expectedModCount = set.modCount;
            this.next = null;
            this.index = 0;
            this.Current = default;
            this.AdvanceToNextSlot();
        }

        public Vector Current { get; private set; }

        readonly object IEnumerator.Current => this.Current;

        public bool MoveNext()
        {
            if (this.set.modCount != this.expectedModCount)
                throw new InvalidOperationException("The set was modified during enumeration.");

            var e = this.next;
            if (e is null)
                return false;

            this.Current = e.Key;
            this.next = e.Next;
            if (this.next is null)
                this.AdvanceToNextSlot();

            return true;
        }

        public void Reset() => throw new NotSupportedException();

        public readonly void Dispose()
        {
        }

        private void AdvanceToNextSlot()
        {
            var tab = this.set.table;
            if (tab is null || this.set.Count == 0)
                return;

            while (this.index < tab.Length && (this.next = tab[this.index++]) is null)
            {
            }
        }
    }

    private class Node(int hash, Vector key, Node? next)
    {
        // Settable so pooled nodes can be reused.
        public int Hash = hash;
        public Vector Key = key;
        public Node? Next = next;
    }

    /// <summary>
    /// Nodes and tables of released sets, kept per thread for the next sets: tree placement builds several sets for every
    /// tree. Pooled tables are cleared.
    /// </summary>
    private static class Pool
    {
        private const int MaxNodes = 16384;
        private const int MaxTablesPerLength = 16;
        private const int MaxTableLength = 4096;

        [ThreadStatic]
        private static Node? nodes;

        [ThreadStatic]
        private static int nodeCount;

        // Pooled tables by the log2 of their length.
        [ThreadStatic]
        private static Stack<Node?[]>?[]? tables;

        public static Node NewNode(int hash, Vector key, Node? next)
        {
            var node = nodes;
            if (node is null)
                return new Node(hash, key, next);

            nodes = node.Next;
            nodeCount--;
            node.Hash = hash;
            node.Key = key;
            node.Next = next;
            return node;
        }

        public static void ReturnNode(Node node)
        {
            if (node is TreeNode || nodeCount >= MaxNodes)
                return;

            node.Next = nodes;
            nodes = node;
            nodeCount++;
        }

        public static Node?[] RentTable(int length)
        {
            // Tables longer than the pooled ones are never kept.
            if (length > MaxTableLength)
                return new Node?[length];

            var pool = tables?[BitOperations.Log2((uint)length)];
            return pool is not null && pool.TryPop(out var table) ? table : new Node?[length];
        }

        public static void ReturnTable(Node?[] table)
        {
            if (table.Length > MaxTableLength || !BitOperations.IsPow2(table.Length))
                return;

            tables ??= new Stack<Node?[]>?[BitOperations.Log2(MaxTableLength) + 1];
            var pool = tables[BitOperations.Log2((uint)table.Length)] ??= new Stack<Node?[]>();
            if (pool.Count < MaxTablesPerLength)
                pool.Push(table);
        }
    }

    /// <summary>Port of <c>HashMap.TreeNode</c>: a red-black tree that also keeps a doubly linked bin order.</summary>
    private sealed class TreeNode(int hash, Vector key, Node? next) : Node(hash, key, next)
    {
        public TreeNode? Parent;
        public TreeNode? Left;
        public TreeNode? Right;
        public TreeNode? Prev;
        public bool Red;

        private TreeNode Root()
        {
            var r = this;
            while (r.Parent is not null)
                r = r.Parent;

            return r;
        }

        private static void MoveRootToFront(Node?[] tab, TreeNode? root)
        {
            if (root is null || tab.Length == 0)
                return;

            var index = (tab.Length - 1) & root.Hash;
            var first = (TreeNode?)tab[index];
            if (root == first)
                return;

            tab[index] = root;
            var rp = root.Prev;
            var rn = root.Next;
            if (rn is not null)
                ((TreeNode)rn).Prev = rp;
            if (rp is not null)
                rp.Next = rn;
            if (first is not null)
                first.Prev = root;
            root.Next = first;
            root.Prev = null;
        }

        private TreeNode? Find(int h, Vector k)
        {
            TreeNode? p = this;
            do
            {
                var pl = p.Left;
                var pr = p.Right;
                if (p.Hash > h)
                {
                    p = pl;
                }
                else if (p.Hash < h)
                {
                    p = pr;
                }
                else if (p.Key == k)
                {
                    return p;
                }
                else if (pl is null)
                {
                    p = pr;
                }
                else if (pr is null)
                {
                    p = pl;
                }
                else
                {
                    // BlockPos isn't "class C implements Comparable<C>", so Java searches both subtrees.
                    var q = pr.Find(h, k);
                    if (q is not null)
                        return q;
                    p = pl;
                }
            }
            while (p is not null);

            return null;
        }

        public TreeNode? GetTreeNode(int h, Vector k) => (this.Parent is not null ? this.Root() : this).Find(h, k);

        public void Treeify(Node?[] tab)
        {
            TreeNode? root = null;
            for (TreeNode? x = this, next; x is not null; x = next)
            {
                next = (TreeNode?)x.Next;
                x.Left = x.Right = null;
                if (root is null)
                {
                    x.Parent = null;
                    x.Red = false;
                    root = x;
                    continue;
                }

                var k = x.Key;
                var h = x.Hash;
                for (var p = root; ;)
                {
                    int dir;
                    if (p.Hash > h)
                        dir = -1;
                    else if (p.Hash < h)
                        dir = 1;
                    else
                        dir = TieBreakOrder(k, p.Key);

                    var xp = p;
                    p = dir <= 0 ? p.Left : p.Right;
                    if (p is null)
                    {
                        x.Parent = xp;
                        if (dir <= 0)
                            xp.Left = x;
                        else
                            xp.Right = x;
                        root = BalanceInsertion(root, x);
                        break;
                    }
                }
            }

            MoveRootToFront(tab, root);
        }

        public TreeNode? PutTreeVal(VanillaBlockPosSet set, Node?[] tab, int h, Vector k)
        {
            var searched = false;
            var root = this.Parent is not null ? this.Root() : this;
            for (var p = root; ;)
            {
                int dir;
                if (p.Hash > h)
                {
                    dir = -1;
                }
                else if (p.Hash < h)
                {
                    dir = 1;
                }
                else if (p.Key == k)
                {
                    return p;
                }
                else
                {
                    if (!searched)
                    {
                        searched = true;
                        var q = p.Left?.Find(h, k) ?? p.Right?.Find(h, k);
                        if (q is not null)
                            return q;
                    }

                    dir = TieBreakOrder(k, p.Key);
                }

                var xp = p;
                p = dir <= 0 ? p.Left : p.Right;
                if (p is null)
                {
                    var xpn = xp.Next;
                    var x = new TreeNode(h, k, xpn);
                    if (dir <= 0)
                        xp.Left = x;
                    else
                        xp.Right = x;
                    xp.Next = x;
                    x.Parent = x.Prev = xp;
                    if (xpn is not null)
                        ((TreeNode)xpn).Prev = x;
                    MoveRootToFront(tab, BalanceInsertion(root, x));
                    return null;
                }
            }
        }

        public void RemoveTreeNode(Node?[] tab, bool movable)
        {
            if (tab.Length == 0)
                return;

            var index = (tab.Length - 1) & this.Hash;
            var first = (TreeNode?)tab[index];
            var root = first;
            var succ = (TreeNode?)this.Next;
            var pred = this.Prev;
            if (pred is null)
                tab[index] = first = succ;
            else
                pred.Next = succ;
            if (succ is not null)
                succ.Prev = pred;
            if (first is null)
                return;
            if (root!.Parent is not null)
                root = root.Root();
            if (movable && (root.Right is null || root.Left is null || root.Left.Left is null))
            {
                tab[index] = Untreeify(first);
                return;
            }

            TreeNode p = this, replacement;
            var pl = this.Left;
            var pr = this.Right;
            if (pl is not null && pr is not null)
            {
                var s = pr;
                while (s.Left is not null)
                    s = s.Left;

                (s.Red, p.Red) = (p.Red, s.Red);
                var sr = s.Right;
                var pp = p.Parent;
                if (s == pr)
                {
                    p.Parent = s;
                    s.Right = p;
                }
                else
                {
                    var sp = s.Parent;
                    if ((p.Parent = sp) is not null)
                    {
                        if (s == sp!.Left)
                            sp.Left = p;
                        else
                            sp.Right = p;
                    }

                    if ((s.Right = pr) is not null)
                        pr.Parent = s;
                }

                p.Left = null;
                if ((p.Right = sr) is not null)
                    sr!.Parent = p;
                if ((s.Left = pl) is not null)
                    pl.Parent = s;
                if ((s.Parent = pp) is null)
                    root = s;
                else if (p == pp!.Left)
                    pp.Left = s;
                else
                    pp.Right = s;

                replacement = sr ?? p;
            }
            else if (pl is not null)
            {
                replacement = pl;
            }
            else if (pr is not null)
            {
                replacement = pr;
            }
            else
            {
                replacement = p;
            }

            if (replacement != p)
            {
                var pp = replacement.Parent = p.Parent;
                if (pp is null)
                    (root = replacement).Red = false;
                else if (p == pp.Left)
                    pp.Left = replacement;
                else
                    pp.Right = replacement;
                p.Left = p.Right = p.Parent = null;
            }

            var r = p.Red ? root : BalanceDeletion(root, replacement);

            if (replacement == p)
            {
                var pp = p.Parent;
                p.Parent = null;
                if (pp is not null)
                {
                    if (p == pp.Left)
                        pp.Left = null;
                    else if (p == pp.Right)
                        pp.Right = null;
                }
            }

            if (movable)
                MoveRootToFront(tab, r);
        }

        public void Split(Node?[] tab, int index, int bit)
        {
            TreeNode? loHead = null, loTail = null, hiHead = null, hiTail = null;
            int lc = 0, hc = 0;
            for (TreeNode? e = this, next; e is not null; e = next)
            {
                next = (TreeNode?)e.Next;
                e.Next = null;
                if ((e.Hash & bit) == 0)
                {
                    e.Prev = loTail;
                    if (loTail is null)
                        loHead = e;
                    else
                        loTail.Next = e;
                    loTail = e;
                    lc++;
                }
                else
                {
                    e.Prev = hiTail;
                    if (hiTail is null)
                        hiHead = e;
                    else
                        hiTail.Next = e;
                    hiTail = e;
                    hc++;
                }
            }

            if (loHead is not null)
            {
                if (lc <= UntreeifyThreshold)
                {
                    tab[index] = Untreeify(loHead);
                }
                else
                {
                    tab[index] = loHead;
                    if (hiHead is not null)
                        loHead.Treeify(tab);
                }
            }

            if (hiHead is not null)
            {
                if (hc <= UntreeifyThreshold)
                {
                    tab[index + bit] = Untreeify(hiHead);
                }
                else
                {
                    tab[index + bit] = hiHead;
                    if (loHead is not null)
                        hiHead.Treeify(tab);
                }
            }
        }

        private static TreeNode RotateLeft(TreeNode root, TreeNode? p)
        {
            TreeNode? r;
            if (p is not null && (r = p.Right) is not null)
            {
                TreeNode? rl, pp;
                if ((rl = p.Right = r.Left) is not null)
                    rl.Parent = p;
                if ((pp = r.Parent = p.Parent) is null)
                    (root = r).Red = false;
                else if (pp.Left == p)
                    pp.Left = r;
                else
                    pp.Right = r;
                r.Left = p;
                p.Parent = r;
            }

            return root;
        }

        private static TreeNode RotateRight(TreeNode root, TreeNode? p)
        {
            TreeNode? l;
            if (p is not null && (l = p.Left) is not null)
            {
                TreeNode? lr, pp;
                if ((lr = p.Left = l.Right) is not null)
                    lr.Parent = p;
                if ((pp = l.Parent = p.Parent) is null)
                    (root = l).Red = false;
                else if (pp.Right == p)
                    pp.Right = l;
                else
                    pp.Left = l;
                l.Right = p;
                p.Parent = l;
            }

            return root;
        }

        private static TreeNode BalanceInsertion(TreeNode root, TreeNode x)
        {
            x.Red = true;
            for (TreeNode? xp, xpp, xppl, xppr; ;)
            {
                if ((xp = x.Parent) is null)
                {
                    x.Red = false;
                    return x;
                }

                if (!xp.Red || (xpp = xp.Parent) is null)
                    return root;

                if (xp == (xppl = xpp.Left))
                {
                    if ((xppr = xpp.Right) is not null && xppr.Red)
                    {
                        xppr.Red = false;
                        xp.Red = false;
                        xpp.Red = true;
                        x = xpp;
                    }
                    else
                    {
                        if (x == xp.Right)
                        {
                            root = RotateLeft(root, x = xp);
                            xpp = (xp = x.Parent) is null ? null : xp.Parent;
                        }

                        if (xp is not null)
                        {
                            xp.Red = false;
                            if (xpp is not null)
                            {
                                xpp.Red = true;
                                root = RotateRight(root, xpp);
                            }
                        }
                    }
                }
                else
                {
                    if (xppl is not null && xppl.Red)
                    {
                        xppl.Red = false;
                        xp.Red = false;
                        xpp.Red = true;
                        x = xpp;
                    }
                    else
                    {
                        if (x == xp.Left)
                        {
                            root = RotateRight(root, x = xp);
                            xpp = (xp = x.Parent) is null ? null : xp.Parent;
                        }

                        if (xp is not null)
                        {
                            xp.Red = false;
                            if (xpp is not null)
                            {
                                xpp.Red = true;
                                root = RotateLeft(root, xpp);
                            }
                        }
                    }
                }
            }
        }

        private static TreeNode BalanceDeletion(TreeNode root, TreeNode? x)
        {
            for (TreeNode? xp, xpl, xpr; ;)
            {
                if (x is null || x == root)
                    return root;

                if ((xp = x.Parent) is null)
                {
                    x.Red = false;
                    return x;
                }

                if (x.Red)
                {
                    x.Red = false;
                    return root;
                }

                if ((xpl = xp.Left) == x)
                {
                    if ((xpr = xp.Right) is not null && xpr.Red)
                    {
                        xpr.Red = false;
                        xp.Red = true;
                        root = RotateLeft(root, xp);
                        xpr = (xp = x.Parent) is null ? null : xp.Right;
                    }

                    if (xpr is null)
                    {
                        x = xp;
                    }
                    else
                    {
                        TreeNode? sl = xpr.Left, sr = xpr.Right;
                        if ((sr is null || !sr.Red) && (sl is null || !sl.Red))
                        {
                            xpr.Red = true;
                            x = xp;
                        }
                        else
                        {
                            if (sr is null || !sr.Red)
                            {
                                if (sl is not null)
                                    sl.Red = false;
                                xpr.Red = true;
                                root = RotateRight(root, xpr);
                                xpr = (xp = x.Parent) is null ? null : xp.Right;
                            }

                            if (xpr is not null)
                            {
                                xpr.Red = xp is not null && xp.Red;
                                if ((sr = xpr.Right) is not null)
                                    sr.Red = false;
                            }

                            if (xp is not null)
                            {
                                xp.Red = false;
                                root = RotateLeft(root, xp);
                            }

                            x = root;
                        }
                    }
                }
                else
                {
                    if (xpl is not null && xpl.Red)
                    {
                        xpl.Red = false;
                        xp.Red = true;
                        root = RotateRight(root, xp);
                        xpl = (xp = x.Parent) is null ? null : xp.Left;
                    }

                    if (xpl is null)
                    {
                        x = xp;
                    }
                    else
                    {
                        TreeNode? sl = xpl.Left, sr = xpl.Right;
                        if ((sl is null || !sl.Red) && (sr is null || !sr.Red))
                        {
                            xpl.Red = true;
                            x = xp;
                        }
                        else
                        {
                            if (sl is null || !sl.Red)
                            {
                                if (sr is not null)
                                    sr.Red = false;
                                xpl.Red = true;
                                root = RotateLeft(root, xpl);
                                xpl = (xp = x.Parent) is null ? null : xp.Left;
                            }

                            if (xpl is not null)
                            {
                                xpl.Red = xp is not null && xp.Red;
                                if ((sl = xpl.Left) is not null)
                                    sl.Red = false;
                            }

                            if (xp is not null)
                            {
                                xp.Red = false;
                                root = RotateRight(root, xp);
                            }

                            x = root;
                        }
                    }
                }
            }
        }
    }
}
