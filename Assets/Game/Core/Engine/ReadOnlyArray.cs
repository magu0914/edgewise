using System;
using System.Collections;
using System.Collections.Generic;

namespace Edgewise.Core
{
    /// <summary>
    /// 配列の読み取り専用ビュー（構造体なのでラップしてもアロケーションしない）。
    /// GameState は内部配列を共有・再利用するため、外部には必ずこの形で公開する。
    /// </summary>
    public readonly struct ReadOnlyArray<T> : IReadOnlyList<T>, IEquatable<ReadOnlyArray<T>>
    {
        readonly T[] _items;

        internal ReadOnlyArray(T[] items)
        {
            _items = items ?? Array.Empty<T>();
        }

        public int Count => _items?.Length ?? 0;

        public T this[int index] => _items[index];

        public T[] ToArray()
        {
            if (_items == null) return Array.Empty<T>();
            var copy = new T[_items.Length];
            Array.Copy(_items, copy, _items.Length);
            return copy;
        }

        public int IndexOf(T item)
        {
            if (_items == null) return -1;
            var cmp = EqualityComparer<T>.Default;
            for (int i = 0; i < _items.Length; i++)
            {
                if (cmp.Equals(_items[i], item)) return i;
            }
            return -1;
        }

        public bool Contains(T item) => IndexOf(item) >= 0;

        public Enumerator GetEnumerator() => new Enumerator(_items);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>要素ごとの比較（構造的な等価）。</summary>
        public bool Equals(ReadOnlyArray<T> other)
        {
            int n = Count;
            if (n != other.Count) return false;
            var cmp = EqualityComparer<T>.Default;
            for (int i = 0; i < n; i++)
            {
                if (!cmp.Equals(_items[i], other._items[i])) return false;
            }
            return true;
        }

        public override bool Equals(object obj) => obj is ReadOnlyArray<T> other && Equals(other);

        public override int GetHashCode()
        {
            int h = 17;
            var cmp = EqualityComparer<T>.Default;
            for (int i = 0; i < Count; i++) h = h * 31 + (_items[i] == null ? 0 : cmp.GetHashCode(_items[i]));
            return h;
        }

        public override string ToString()
        {
            var parts = new string[Count];
            for (int i = 0; i < parts.Length; i++) parts[i] = _items[i] == null ? "null" : _items[i].ToString();
            return "[" + string.Join(", ", parts) + "]";
        }

        public struct Enumerator : IEnumerator<T>
        {
            readonly T[] _items;
            int _index;

            internal Enumerator(T[] items)
            {
                _items = items;
                _index = -1;
            }

            public T Current => _items[_index];

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                if (_items == null) return false;
                _index++;
                return _index < _items.Length;
            }

            public void Reset() => _index = -1;

            public void Dispose()
            {
            }
        }
    }
}
