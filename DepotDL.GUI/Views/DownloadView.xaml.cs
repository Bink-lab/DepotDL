// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using DepotDL.GUI.ViewModels;

namespace DepotDL.GUI.Views
{
    public partial class DownloadView : UserControl
    {
        public DownloadView()
        {
            InitializeComponent();
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.Data.Contains(DataFormats.Files)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object? sender, DragEventArgs e)
        {
            if (!e.Data.Contains(DataFormats.Files)) return;
            var items = e.Data.GetFiles()?.ToList();
            if (items == null || items.Count == 0) return;
            if (DataContext is not DownloadViewModel vm) return;

            foreach (var item in items)
            {
                var path = item.TryGetLocalPath();
                if (path == null) continue;
                var ext = Path.GetExtension(path);
                if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    vm.ImportZipFile(path);
                    return;
                }
                if (ext.Equals(".lua", StringComparison.OrdinalIgnoreCase))
                {
                    vm.LoadLuaFile(path);
                    return;
                }
            }
        }
    }
}
