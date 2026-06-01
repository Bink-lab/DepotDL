using System;
using System.Collections.Generic;

namespace DepotDL.GUI.Models
{
    public class LibraryGame
    {
        public string GameName { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public string LuaPath { get; set; } = string.Empty;
        public string OutputDir { get; set; } = string.Empty;
        public List<string> DepotIds { get; set; } = new();
        public DateTime InstallDate { get; set; }
        public long TotalSizeBytes { get; set; }
        public bool IsVerified { get; set; } = true;
    }
}
