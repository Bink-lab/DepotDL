// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using SteamKit2;

namespace DepotDL.Shared
{
    public static class ConflictGroupBuilder
    {
        public static Dictionary<string, SemaphoreSlim> Build(
            IReadOnlyList<(string DepotId, string? ManifestId)> depots,
            IReadOnlyDictionary<string, string> manifestMap)
        {
            var parent = Enumerable.Range(0, depots.Count).ToArray();

            int Find(int i)
            {
                while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                return i;
            }
            void Union(int a, int b)
            {
                a = Find(a); b = Find(b);
                if (a != b) parent[b] = a;
            }

            var fileToFirst = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < depots.Count; i++)
            {
                var d = depots[i];
                string? mfPath = null;
                if (!string.IsNullOrEmpty(d.ManifestId))
                {
                    manifestMap.TryGetValue($"{d.DepotId}_{d.ManifestId}", out mfPath);
                    if (mfPath == null) manifestMap.TryGetValue(d.ManifestId, out mfPath);
                }
                if (mfPath == null) manifestMap.TryGetValue(d.DepotId, out mfPath);
                if (mfPath == null || !File.Exists(mfPath)) continue;

                try
                {
                    var manifest = DepotManifest.LoadFromFile(mfPath);
                    if (manifest?.Files == null) continue;
                    foreach (var f in manifest.Files)
                    {
                        if (string.IsNullOrEmpty(f.FileName)) continue;
                        if ((f.Flags & EDepotFileFlag.Directory) != 0) continue;
                        if (fileToFirst.TryGetValue(f.FileName, out var prior))
                            Union(prior, i);
                        else
                            fileToFirst[f.FileName] = i;
                    }
                }
                catch { }
            }

            var groupSems = new Dictionary<int, SemaphoreSlim>();
            var result = new Dictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < depots.Count; i++)
            {
                var root = Find(i);
                if (!groupSems.TryGetValue(root, out var sem))
                    groupSems[root] = sem = new SemaphoreSlim(1, 1);
                result[depots[i].DepotId] = sem;
            }
            return result;
        }
    }
}
