// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.IO;
using System.Windows;
using System.Windows.Controls;
using DepotDL.GUI.ViewModels;

namespace DepotDL.GUI.Views
{
    public partial class DownloadView : UserControl
    {
        public DownloadView() => InitializeComponent();

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length == 0) return;
            if (DataContext is not DownloadViewModel vm) return;

            foreach (var f in files)
            {
                var ext = Path.GetExtension(f);
                if (ext.Equals(".zip", System.StringComparison.OrdinalIgnoreCase))
                {
                    vm.ImportZipFile(f);
                    return;
                }
                if (ext.Equals(".lua", System.StringComparison.OrdinalIgnoreCase))
                {
                    vm.LoadLuaFile(f);
                    return;
                }
            }
        }
    }
}
