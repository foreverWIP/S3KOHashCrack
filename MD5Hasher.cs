using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace RSDKv5
{
    public static class MD5Hasher
    {
        static readonly ConcurrentDictionary<int, byte[]> hashByteCache = new();
        private static readonly MD5 MD5Provider = MD5.Create();

        public static byte[] GetHash(byte[] data) => MD5Provider.ComputeHash(data);

        public static byte[] GetHash(string data)
        {
            var dataHashCode = data.GetHashCode();
            if (hashByteCache.TryGetValue(dataHashCode, out var cached))
            {
                return cached;
            }
            var v = GetHash(Encoding.ASCII.GetBytes(data));
            hashByteCache[dataHashCode] = v;
            return v;
        }
    }
}
