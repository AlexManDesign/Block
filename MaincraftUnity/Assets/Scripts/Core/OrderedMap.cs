using System.Collections;
using System.Collections.Generic;

namespace Maincraft
{
    /// <summary>
    /// A map that iterates in insertion order (like a JavaScript Map): the original relies on that order
    /// in a few places (redstone timers, scheduled ticks). Removing during iteration over a snapshot is fine.
    /// </summary>
    public sealed class OrderedMap<TK, TV> : IEnumerable<KeyValuePair<TK, TV>>
    {
        readonly Dictionary<TK, LinkedListNode<KeyValuePair<TK, TV>>> index = new Dictionary<TK, LinkedListNode<KeyValuePair<TK, TV>>>();
        readonly LinkedList<KeyValuePair<TK, TV>> list = new LinkedList<KeyValuePair<TK, TV>>();

        public int Count => index.Count;
        public bool ContainsKey(TK k) => index.ContainsKey(k);

        public bool TryGetValue(TK k, out TV v)
        {
            if (index.TryGetValue(k, out var n)) { v = n.Value.Value; return true; }
            v = default; return false;
        }

        public TV Get(TK k, TV dflt = default) => index.TryGetValue(k, out var n) ? n.Value.Value : dflt;

        /// <summary>Sets a value; a new key goes to the end, an existing one keeps its place.</summary>
        public void Set(TK k, TV v)
        {
            if (index.TryGetValue(k, out var n)) n.Value = new KeyValuePair<TK, TV>(k, v);
            else index[k] = list.AddLast(new KeyValuePair<TK, TV>(k, v));
        }

        public bool Remove(TK k)
        {
            if (!index.TryGetValue(k, out var n)) return false;
            list.Remove(n); index.Remove(k);
            return true;
        }

        public void Clear() { index.Clear(); list.Clear(); }

        public IEnumerable<TK> Keys { get { foreach (var kv in list) yield return kv.Key; } }

        /// <summary>A snapshot of the entries (safe to modify the map while walking it).</summary>
        public List<KeyValuePair<TK, TV>> Snapshot() => new List<KeyValuePair<TK, TV>>(list);

        public IEnumerator<KeyValuePair<TK, TV>> GetEnumerator() => list.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
