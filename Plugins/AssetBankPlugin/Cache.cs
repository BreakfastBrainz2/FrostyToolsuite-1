using FrostySdk.IO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace AssetBankPlugin
{
    /// <summary>
    /// Caches all AntRefs and in which bundle they are stored.
    /// All collections are ConcurrentDictionary because multiple bundles are loaded
    /// in parallel (AnonymousMethod__1 / __2 etc.) and plain Dictionary is not
    /// thread-safe for concurrent writes, causing ntdll heap corruption overhead.
    /// </summary>
    public class Cache
    {
        public static ConcurrentDictionary<Guid, int> AntStateBundleIndices = new ConcurrentDictionary<Guid, int>();
        public static ConcurrentDictionary<Guid, Guid> AntRefMap = new ConcurrentDictionary<Guid, Guid>();

        public static void ReadState(string path)
        {
            using (var s = File.OpenRead(path))
            using (var r = new NativeReader(s))
            {
                uint refCount = r.ReadUInt();
                for (int i = 0; i < refCount; i++)
                {
                    Guid guid = r.ReadGuid();
                    int index = r.ReadInt();
                    AntStateBundleIndices[guid] = index;
                }
            }
        }

        public static void WriteState(string path)
        {
            using (var s = File.OpenWrite(path))
            using (var w = new NativeWriter(s))
            {
                // Snapshot the pairs once to avoid enumerator issues under concurrent access.
                var pairs = AntStateBundleIndices.ToArray();
                w.Write(pairs.Length);
                foreach (var kvp in pairs)
                {
                    w.Write(kvp.Key);
                    w.Write(kvp.Value);
                }
            }
        }

        public static void ReadMap(string path)
        {
            using (var s = File.OpenRead(path))
            using (var r = new NativeReader(s))
            {
                uint refCount = r.ReadUInt();
                for (int i = 0; i < refCount; i++)
                {
                    Guid guid = r.ReadGuid();
                    Guid index = r.ReadGuid();
                    AntRefMap[guid] = index;
                }
            }
        }

        public static void WriteMap(string path)
        {
            using (var s = File.OpenWrite(path))
            using (var w = new NativeWriter(s))
            {
                var pairs = AntRefMap.ToArray();
                w.Write(pairs.Length);
                foreach (var kvp in pairs)
                {
                    w.Write(kvp.Key);
                    w.Write(kvp.Value);
                }
            }
        }
    }
}