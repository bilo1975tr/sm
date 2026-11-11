using System;
using System.Collections.Generic;

namespace StreamMesh.Core.Utils
{
    /// <summary>
    /// Thread-safe, bounded Least Recently Used (LRU) segment cache to prevent unbounded memory growth.
    /// Evicts the least recently accessed segments first when the capacity threshold is reached.
    /// </summary>
    public class LruSegmentCache
    {
        private readonly int _capacity;
        private readonly object _lock = new();
        private readonly Dictionary<string, LinkedListNode<CacheItem>> _map;
        private readonly LinkedList<CacheItem> _lruList = new();

        private class CacheItem
        {
            public string Key { get; }
            public byte[] Data { get; }

            public CacheItem(string key, byte[] data)
            {
                Key = key;
                Data = data;
            }
        }

        public LruSegmentCache(int capacity = 96)
        {
            _capacity = Math.Max(16, capacity);
            _map = new Dictionary<string, LinkedListNode<CacheItem>>(_capacity, StringComparer.Ordinal);
        }

        public bool TryGetValue(string key, out byte[]? data)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _lruList.Remove(node);
                    _lruList.AddLast(node);
                    data = node.Value.Data;
                    return true;
                }
                data = null;
                return false;
            }
        }

        public bool ContainsKey(string key)
        {
            lock (_lock)
            {
                return _map.ContainsKey(key);
            }
        }

        public void Set(string key, byte[] data)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var existingNode))
                {
                    _lruList.Remove(existingNode);
                    _lruList.AddLast(existingNode);
                    return;
                }

                if (_map.Count >= _capacity)
                {
                    var oldest = _lruList.First;
                    if (oldest != null)
                    {
                        _lruList.RemoveFirst();
                        _map.Remove(oldest.Value.Key);
                    }
                }

                var node = new LinkedListNode<CacheItem>(new CacheItem(key, data));
                _lruList.AddLast(node);
                _map[key] = node;
            }
        }

        public byte[]? this[string key]
        {
            get => TryGetValue(key, out var d) ? d : null;
            set { if (value != null) Set(key, value); }
        }

        public bool TryRemove(string key, out byte[]? data)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _lruList.Remove(node);
                    _map.Remove(key);
                    data = node.Value.Data;
                    return true;
                }
                data = null;
                return false;
            }
        }

        public bool Remove(string key)
        {
            return TryRemove(key, out _);
        }

        public void Clear()
        {
            lock (_lock)
            {
                _map.Clear();
                _lruList.Clear();
            }
        }

        public int Count
        {
            get
            {
                lock (_lock) return _map.Count;
            }
        }
    }
}
