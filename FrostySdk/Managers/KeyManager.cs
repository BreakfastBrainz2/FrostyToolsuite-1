using System.Collections.Generic;

namespace FrostySdk.Managers;

public static class KeyManager
{
    private static readonly Dictionary<string, byte[]> s_keys = new();

    public static void AddKey(string id, byte[] data)
    {
        s_keys.TryAdd(id, data);
    }

    public static byte[] GetKey(string id)
    {
        if (!s_keys.ContainsKey(id))
        {
            throw new KeyNotFoundException($"Could not find key with id: {id}");
        }

        return s_keys[id];
    }

    public static bool HasKey(string id) => s_keys.ContainsKey(id);
}
