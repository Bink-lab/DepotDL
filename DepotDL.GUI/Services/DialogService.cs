// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using DepotDL.GUI.Views;

namespace DepotDL.GUI.Services
{
    public static class DialogService
    {
        private static Window? MainWindow =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        public static async Task ShowErrorAsync(string title, string message)
        {
            var owner = MainWindow;
            if (owner == null) return;
            var dialog = new ErrorDialog(title, message);
            await dialog.ShowDialog(owner);
        }

        public static async Task<bool> ShowConfirmAsync(string title, string message)
        {
            var owner = MainWindow;
            if (owner == null) return false;
            var dialog = new ConfirmDialog(title, message);
            return await dialog.ShowDialog<bool>(owner);
        }

        public static async Task<string?> ShowInputAsync(string title, string initialValue = "")
        {
            var owner = MainWindow;
            if (owner == null) return null;
            var dialog = new InputDialog(title, initialValue);
            await dialog.ShowDialog(owner);
            return dialog.Result;
        }

        public static async Task<string?> OpenFileAsync(string title, string filterName, string[] patterns)
        {
            var owner = MainWindow;
            if (owner == null) return null;
            var topLevel = TopLevel.GetTopLevel(owner);
            if (topLevel == null) return null;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(filterName) { Patterns = patterns },
                    new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
                }
            });
            return files.Count > 0 ? files[0].Path.LocalPath : null;
        }

        public static async Task<string?> OpenFolderAsync(string title)
        {
            var owner = MainWindow;
            if (owner == null) return null;
            var topLevel = TopLevel.GetTopLevel(owner);
            if (topLevel == null) return null;
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });
            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        }
    }
}
