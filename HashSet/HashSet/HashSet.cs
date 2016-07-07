using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace HashSet
{
    /*
     * HashSet
     * Dmitriy Karachev
     * 07.07.16
     * 
     * Основан на хэш таблице. 
     * Разрешение коллизий методом двойного хэширования.
     * H(key,attempts)=mainH(key)+attempts*offH(key)
     */

    #region Proxy

    internal class HashSetProxy<T>
    {
        private HashSet<T> _a;

        private HashSetProxy(HashSet<T> a)
        {
            _a = a;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.Collapsed)]
        public T[] Items
        {
            get
            {
                var items = (from item in _a select item).ToArray();
                return items;
            }
        }
    }

    #endregion

    [DebuggerDisplay("Count = {Count}")]
    [DebuggerTypeProxy(typeof(HashSetProxy<>))]
    public class HashSet<T>:ISet<T>
    {
        #region Consts and static fields

        private const double LoadFactor = 0.8;//[0.1,1] 
        private const int DefaultCapacity = 4;
        #endregion

        #region Fields

        private Entry[] _entries;
        private int _count;
        private int _countLimit;//Для Resize()
        private int _version;
        private IEqualityComparer<T> _eqComparer; 
             
        #endregion

        #region Properties

        public int Count => _count;
        public IEqualityComparer<T> EqualityComparer => _eqComparer;
        public bool IsReadOnly => true;

        #endregion

        #region Constructors

        public HashSet() : this(DefaultCapacity, null) { }

        public HashSet(int capacity) : this(capacity, null) { }
        
        public HashSet(IEqualityComparer<T> equalityComparer) : this(DefaultCapacity, equalityComparer) { } 

        public HashSet(int capacity, IEqualityComparer<T> equalityComparer)
        {
            Init(capacity,equalityComparer);
        }
        #endregion

        #region Methods

        public bool Add(T item)
        {
            if (_count == _countLimit) Resize();

            int hash;
            var mainhash = GetMainHash(item, _entries.Length, out hash);
            var offhash = GetOffHash(mainhash, _entries.Length);

            var index = mainhash;

            while (true)
            {
                if (IsEntryEmpty(_entries, index))
                {
                    _entries[index].HashCode = hash;
                    _entries[index].Value = item;
                    _count++;
                    _version++;
                    return true;
                }

                if (_entries[index].HashCode == hash && _eqComparer.Equals(_entries[index].Value, item))
                    return false;

                index += offhash;
                index %= _entries.Length;
            }
        }

        public void Clear()
        {
            Init(0, _eqComparer);
        }

        public bool Contains(T item)
        {
            int hash;
            var mainhash = GetMainHash(item, _entries.Length, out hash);
            var offhash = GetOffHash(mainhash, _entries.Length);

            var index = mainhash;

            while (true)
            {
                if (IsEntryEmpty(_entries, index)) return false;

                if (_entries[index].HashCode == hash && _eqComparer.Equals(_entries[index].Value, item))
                    return true;

                index += offhash;
                index %= _entries.Length;
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (arrayIndex > array.Length)
                throw new ArgumentOutOfRangeException("Индекс массива выходит за пределы длины массива");
            if (array.Length < _count + arrayIndex) 
                throw new ArgumentException("Массив не имеет достаточно места");

            for (int i = 0; i < _entries.Length; i++)
            {
                if (IsEntryEmpty(_entries, i)) continue;

                array[arrayIndex] = _entries[i].Value;
                arrayIndex++;
            }
        }

        public bool Remove(T item)
        {
            int hash;
            var mainhash = GetMainHash(item, _entries.Length, out hash);
            var offhash = GetOffHash(mainhash, _entries.Length);

            var index = mainhash;

            while (true)
            {
                if (IsEntryEmpty(_entries, index)) return false;

                if (_entries[index].HashCode == hash && _eqComparer.Equals(_entries[index].Value, item))
                {
                    _entries[index].HashCode = -1;
                    _entries[index].Value = default(T);
                    _version++;
                    _count--;
                    return true;
                }

                index += offhash;
                index %= _entries.Length;
            }
        }

        void ICollection<T>.Add(T item)
        {
            Add(item);
        }

        #endregion

        #region ISet methods

        public void UnionWith(IEnumerable<T> other)
        {
            if(other==null)
                throw new ArgumentNullException("Аргумент не должен принимать значение null");

            foreach (T item in other)
            {
                Add(item);
            }
        }

        public void IntersectWith(IEnumerable<T> other)
        {
            if(other==null)
                throw new ArgumentNullException("Аргумент не должен принимать значение null");

            if (_count == 0) return;
            var intersectEnriesIndexes=new HashSet<int>();
            foreach (T item in other)
            {
                var index = FindEntry(item);
                if (index >= 0)
                    intersectEnriesIndexes.Add(index);
            }

            for (int i = 0; i < _entries.Length; i++)
            {
                if (IsEntryEmpty(_entries, i)) continue;

                if (intersectEnriesIndexes.Remove(i)) continue;

                _entries[i].HashCode = -1;
                _entries[i].Value = default(T);//TODO: добавить метод очистить запись
                _count--;
            }
        }

        public void ExceptWith(IEnumerable<T> other)
        {
            if (other == null)
                throw new ArgumentNullException("Аргумент не должен принимать значение null");

            foreach (T item in other)
            {
                Remove(item);
            }
        }

        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            if (other == null)
                throw new ArgumentNullException("Аргумент не должен принимать значение null");

            foreach (T item in other)
            {
                if (!Remove(item))
                    Add(item);
            }
        }
        public bool IsSubsetOf(IEnumerable<T> other)//Подмножество
        {
            if (other == null)
                throw new ArgumentNullException("Аргумент не должен принимать значение null");

            var indexesOfOtherInSet = new HashSet<int>();
            foreach (T item in other)
            {
                var index = FindEntry(item);
                if (index >= 0) indexesOfOtherInSet.Add(index);
            }

            return indexesOfOtherInSet.Count == _count;
        }

        public bool IsSupersetOf(IEnumerable<T> other)//Надмноженство
        {
            foreach (T item in other)
            {
                if (!Contains(item)) return false;
            }
            return true;
        }

        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            int count = 0;
            foreach (T item in other)
            {
                if (!Contains(item)) return false;
                count++;
            }
            return count != _count;
        }

        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            if (other == null)
                throw new ArgumentNullException("Аргумент не должен принимать значение null");

            var indexesOfOtherInSet = new HashSet<int>();
            foreach (T item in other)
            {
                var index = FindEntry(item);
                indexesOfOtherInSet.Add(index);
            }

            return indexesOfOtherInSet.Count-1 == _count;
        }

        public bool Overlaps(IEnumerable<T> other)
        {
            foreach (T item in other)
            {
                if (Contains(item)) return true;
            }
            return false;
        }

        public bool SetEquals(IEnumerable<T> other)
        {
            int count = 0;
            foreach (T item in other)
            {
                if (!Contains(item)) return false;
                count++;
            }
            return count == _count;
        }

        #endregion

        #region Enumerable methods

        public IEnumerator<T> GetEnumerator()
        {
            var version = _version;
            for (int i = 0; i < _entries.Length; i++)
            {
                if (version != _version) throw new InvalidOperationException("Коллекция была изменена");
                if (IsEntryEmpty(_entries, i)) continue;
                yield return _entries[i].Value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        #region Internal and private methods

        private void Init(int capacity,IEqualityComparer<T> comparer)
        {
            var realsize = Convert.ToInt32(capacity/LoadFactor);
            realsize = HashHelper.GetPrime(realsize);
            _entries = new Entry[realsize];
            _count = 0;
            _countLimit = Convert.ToInt32(realsize*LoadFactor);
            _version = 0;
            _eqComparer = comparer ?? EqualityComparer<T>.Default;

            for (int i = 0; i < _entries.Length; i++)
            {
                _entries[i].HashCode = -1;
            }
        }

        private int GetMainHash(T value,int length,out int hash)
        {
            hash = _eqComparer.GetHashCode(value) & 0x7FFFFFFF;
            var mainhash = hash%length;
            return mainhash;
        }

        private int GetOffHash(int mainhash,int length)
        {
            return 1 + mainhash%(length - 1);
        }       

        private void Resize()
        {
            var newsize = HashHelper.GetPrime(2*_entries.Length);
            var newentries = new Entry[newsize];
            for (int i = 0; i < newentries.Length; i++)
            {
                newentries[i].HashCode = -1;
            }

            for (int i = 0; i < _entries.Length; i++)
            {
                if (IsEntryEmpty(_entries, i)) continue;

                int hash;
                var mainhash = GetMainHash(_entries[i].Value, newentries.Length, out hash);
                var offhash = GetOffHash(mainhash, newentries.Length);
                var index = mainhash;

                while (true)
                {
                    if (IsEntryEmpty(newentries, index))
                    {
                        newentries[index].HashCode = hash;
                        newentries[index].Value = _entries[i].Value;
                        break;
                    }
                    index += offhash;
                    index %= newentries.Length;
                }
            }
            _version++;
            _entries = newentries;
            _countLimit = Convert.ToInt32(LoadFactor*newentries.Length);
        }

        private int FindEntry(T item)
        {
            int hash;
            var mainhash = GetMainHash(item, _entries.Length, out hash);
            var offhash = GetOffHash(mainhash, _entries.Length);

            var index = mainhash;
            while (true)
            {
                if (IsEntryEmpty(_entries, index)) return -1;

                if (_entries[index].HashCode == hash && _eqComparer.Equals(_entries[index].Value, item))
                {
                    return index;
                }

                index += offhash;
                index %= _entries.Length;
            }
        }

        private static bool IsEntryEmpty(Entry[] entries,int index)
        {
            return entries[index].HashCode == -1;
        }

        #endregion

        #region Nested types
        internal struct Entry
        {
            public T Value;
            public int HashCode;//-1 if not used
        }
        #endregion
    }
}
