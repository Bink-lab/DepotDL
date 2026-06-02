// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.Linq;

namespace DepotDownloader
{
    static class DepotKeyStore
    {
        private static readonly Dictionary<uint, byte[]> depotKeysCache = new Dictionary<uint, byte[]>();

        public static void AddAll(string[] values)
        {
            foreach (var value in values)
            {
                var split = value.Split(';');

                if (split.Length != 2)
                {
                    Console.WriteLine("Warning: Skipping invalid depot key line: {0}", value);
                    continue;
                }

                if (!uint.TryParse(split[0], out var depotId))
                {
                    Console.WriteLine("Warning: Skipping depot key line with invalid depot ID: {0}", value);
                    continue;
                }

                try
                {
                    depotKeysCache.Add(depotId, StringToByteArray(split[1]));
                }
                catch (ArgumentException)
                {
                    Console.WriteLine("Warning: Duplicate depot ID {0} in depot keys file, skipping.", depotId);
                }
            }
        }

        private static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                .ToArray();
        }

        public static bool ContainsKey(uint depotId)
        {
            return depotKeysCache.ContainsKey(depotId);
        }

        public static byte[] Get(uint depotId)
        {
            return depotKeysCache[depotId];
        }


    }
}
