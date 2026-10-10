// Voxel Forge — Unity port. Insertion-ordered Set/Map with JS iteration semantics
// (entries added during iteration are visited; deleted entries are skipped).
using System;
using System.Collections;
using System.Collections.Generic;

namespace VoxelForge
{
    public sealed class OrderedMap<K, V> : IEnumerable<KeyValuePair<K, V>>
    {
        sealed class Node { public K key; public V val; public Node prev, next; public bool removed; }
        readonly Dictionary<K, Node> map;
        Node head, tail;
        // Removed nodes whose 'next' was the end of the list when they were unlinked: the next appended node is
        // linked from them too, so an enumerator parked on one of them still reaches entries added afterwards (JS Map).
        List<Node> deadTails;
        public OrderedMap() { map = new Dictionary<K, Node>(); }
        public OrderedMap(IEqualityComparer<K> cmp) { map = new Dictionary<K, Node>(cmp); }
        public int Count { get { return map.Count; } }
        public int size { get { return map.Count; } }
        public bool Has(K k) { return map.ContainsKey(k); }
        public bool ContainsKey(K k) { return map.ContainsKey(k); }
        public bool TryGetValue(K k, out V v) { Node n; if (map.TryGetValue(k, out n)) { v = n.val; return true; } v = default(V); return false; }
        public V Get(K k) { Node n; return map.TryGetValue(k, out n) ? n.val : default(V); }
        public void Set(K k, V v)
        {
            Node n;
            if (map.TryGetValue(k, out n)) { n.val = v; return; }
            n = new Node { key = k, val = v, prev = tail };
            if (tail != null) tail.next = n; else head = n;
            if (deadTails != null && deadTails.Count > 0) { foreach (var d in deadTails) d.next = n; deadTails.Clear(); }
            tail = n; map[k] = n;
        }
        public V this[K k] { get { return map[k].val; } set { Set(k, value); } }
        public bool Delete(K k)
        {
            Node n;
            if (!map.TryGetValue(k, out n)) return false;
            map.Remove(k); n.removed = true;
            if (n.prev != null) n.prev.next = n.next; else head = n.next;
            if (n.next != null) n.next.prev = n.prev; else { tail = n.prev; addDeadTail(n); }
            // keep n.next so an iterator sitting on n can continue
            return true;
        }
        public bool Remove(K k) { return Delete(k); }
        public void Clear()
        {
            for (var n = head; n != null; n = n.next) n.removed = true;
            if (tail != null) addDeadTail(tail);
            map.Clear(); head = tail = null;
        }
        void addDeadTail(Node n) { if (deadTails == null) deadTails = new List<Node>(); deadTails.Add(n); }
        public IEnumerator<KeyValuePair<K, V>> GetEnumerator()
        {
            var n = head;
            while (n != null)
            {
                if (!n.removed) yield return new KeyValuePair<K, V>(n.key, n.val);
                var nx = n.next;
                while (nx != null && nx.removed) nx = nx.next;
                n = nx;
            }
        }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        public IEnumerable<K> Keys { get { foreach (var kv in this) yield return kv.Key; } }
        public IEnumerable<V> Values { get { foreach (var kv in this) yield return kv.Value; } }
        public K FirstKey() { return head != null ? head.key : default(K); }
    }

    public sealed class OrderedSet<T> : IEnumerable<T>
    {
        readonly OrderedMap<T, bool> m;
        public OrderedSet() { m = new OrderedMap<T, bool>(); }
        public OrderedSet(IEnumerable<T> src) : this() { foreach (var x in src) Add(x); }
        public int Count { get { return m.Count; } }
        public int size { get { return m.Count; } }
        public bool Add(T v) { if (m.Has(v)) return false; m.Set(v, true); return true; }
        public bool Has(T v) { return m.Has(v); }
        public bool Contains(T v) { return m.Has(v); }
        public bool Delete(T v) { return m.Delete(v); }
        public bool Remove(T v) { return m.Delete(v); }
        public void Clear() { m.Clear(); }
        public T First() { return m.FirstKey(); }
        public IEnumerator<T> GetEnumerator() { foreach (var kv in m) yield return kv.Key; }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }
}
