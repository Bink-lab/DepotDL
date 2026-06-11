// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.IO;

namespace DepotDL.Shared
{
    public static class ManifestHelper
    {
        public static Dictionary<string, string> BuildManifestMap(string? dir)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return map;
            foreach (var file in Directory.GetFiles(dir, "*.manifest"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var parts = name.Split('_');
                if (parts.Length >= 2)
                {
                    map[$"{parts[0]}_{parts[1]}"] = file;
                    map[parts[0]] = file;
                    map[parts[1]] = file;
                }
                else
                {
                    map[name] = file;
                }
            }
            return map;
        }
    }
}
