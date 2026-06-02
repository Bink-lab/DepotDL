// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.IsolatedStorage;
using ProtoBuf;

namespace DepotDownloader
{
    [ProtoContract]
    class AccountSettingsStore
    {
        // Member 1 was a Dictionary<string, byte[]> for SentryData.

        [ProtoMember(2, IsRequired = false)]
        public ConcurrentDictionary<string, int> ContentServerPenalty { get; private set; }

        // Member 3 was a Dictionary<string, string> for LoginKeys.

        [ProtoMember(4, IsRequired = false)]
        public Dictionary<string, string> LoginTokens { get; private set; }

        [ProtoMember(5, IsRequired = false)]
        public Dictionary<string, string> GuardData { get; private set; }

        string FileName;

        AccountSettingsStore()
        {
            ContentServerPenalty = new ConcurrentDictionary<string, int>();
            LoginTokens = new(StringComparer.OrdinalIgnoreCase);
            GuardData = new(StringComparer.OrdinalIgnoreCase);
        }

        static bool Loaded
        {
            get { return Instance != null; }
        }

        public static AccountSettingsStore Instance;
        static readonly IsolatedStorageFile IsolatedStorage = IsolatedStorageFile.GetUserStoreForAssembly();
        static readonly object _syncRoot = new();

        public static void LoadFromFile(string filename)
        {
            lock (_syncRoot)
            {
                if (Loaded)
                    throw new InvalidOperationException("Config already loaded");

                if (IsolatedStorage.FileExists(filename))
                {
                    try
                    {
                        using var fs = IsolatedStorage.OpenFile(filename, FileMode.Open, FileAccess.Read);
                        using var ds = new DeflateStream(fs, CompressionMode.Decompress);
                        Instance = Serializer.Deserialize<AccountSettingsStore>(ds);
                    }
                    catch (IOException ex)
                    {
                        Console.WriteLine("Failed to load account settings: {0}", ex.Message);
                        Instance = new AccountSettingsStore();
                    }
                }
                else
                {
                    Instance = new AccountSettingsStore();
                }

                Instance.FileName = filename;
            }
        }

        public static void Save()
        {
            lock (_syncRoot)
            {
                if (!Loaded)
                    throw new InvalidOperationException("Saved config before loading");

                try
                {
                    using var fs = IsolatedStorage.OpenFile(Instance.FileName, FileMode.Create, FileAccess.Write);
                    using var ds = new DeflateStream(fs, CompressionMode.Compress);
                    Serializer.Serialize(ds, Instance);
                }
                catch (IOException ex)
                {
                    Console.WriteLine("Failed to save account settings: {0}", ex.Message);
                }
            }
        }
    }
}
