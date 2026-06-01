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

            foreach (var f in files)
            {
                if (Path.GetExtension(f).Equals(".lua", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (DataContext is DownloadViewModel vm)
                        vm.LoadLuaFile(f);
                    return;
                }
            }
        }
    }
}
