extern alias References;

using Oxide.Core;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    /// <summary>
    /// A dictionary which returns null for non-existant keys and removes keys when setting an index to null.
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    public class Hash<TKey, TValue> : IDictionary<TKey, TValue>
    {
        private readonly IDictionary<TKey, TValue> dictionary;

        public Hash()
        {
            dictionary = new Dictionary<TKey, TValue>();
        }

        public Hash(IEqualityComparer<TKey> comparer)
        {
            dictionary = new Dictionary<TKey, TValue>(comparer);
        }

        public TValue this[TKey key]
        {
            get
            {
                if (TryGetValue(key, out TValue value))
                {
                    return value;
                }

                if (typeof(TValue).IsValueType)
                {
                    return (TValue)Activator.CreateInstance(typeof(TValue));
                }

                return default(TValue);
            }

            set
            {
                if (value == null)
                {
                    dictionary.Remove(key);
                }
                else
                {
                    dictionary[key] = value;
                }
            }
        }

        public ICollection<TKey> Keys => dictionary.Keys;
        public ICollection<TValue> Values => dictionary.Values;
        public int Count => dictionary.Count;
        public bool IsReadOnly => dictionary.IsReadOnly;

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => dictionary.GetEnumerator();

        public bool ContainsKey(TKey key) => dictionary.ContainsKey(key);

        public bool Contains(KeyValuePair<TKey, TValue> item) => dictionary.Contains(item);

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int index) => dictionary.CopyTo(array, index);

        public bool TryGetValue(TKey key, out TValue value) => dictionary.TryGetValue(key, out value);

        public void Add(TKey key, TValue value) => dictionary.Add(key, value);

        public void Add(KeyValuePair<TKey, TValue> item) => dictionary.Add(item);

        public bool Remove(TKey key) => dictionary.Remove(key);

        public bool Remove(KeyValuePair<TKey, TValue> item) => dictionary.Remove(item);

        public void Clear() => dictionary.Clear();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal static class EnvironmentHelper
    {
        public static void SetOxideEnvironmentalVariable(string name, string value, bool force = false)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            name = NormalizeKey(name);

            if (force)
            {
                Environment.SetEnvironmentVariable(name, value);
                Interface.Oxide.RootLogger.WriteDebug(Core.Logging.LogType.Info, null, "CSharp", $"Setting environmental variable: {name} => {value ?? "null"}");
            }
            else if (GetOxideEnvironmentalVariable(name) == null)
            {
                Environment.SetEnvironmentVariable(name, value);
                Interface.Oxide.RootLogger.WriteDebug(Core.Logging.LogType.Info, null, "CSharp", $"Setting environmental variable: {name} => {value ?? "null"}");
            }
            else
            {
                Interface.Oxide.RootLogger.WriteDebug(Core.Logging.LogType.Warning, null, "CSharp", $"Failed to set environmental variable: {name} => {value ?? "null"} | Value is already set, please use force parameter to force it");
            }
        }

        public static string GetOxideEnvironmentalVariable(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            name = NormalizeKey(name);
            string value = Environment.GetEnvironmentVariable(name);
            Interface.Oxide.RootLogger.WriteDebug(Core.Logging.LogType.Warning, null, "CSharp", $"Retrieving environmental variable: {name} => {value ?? "null"}");
            return value;
        }

        private static string NormalizeKey(string key)
        {
            if (key.StartsWith("OXIDE:", StringComparison.InvariantCultureIgnoreCase))
            {
                key = key.Replace("oxide:", "OXIDE:");
            }
            else
            {
                key = "OXIDE:" + key;
            }

            return key;
        }
    }
}
