using System.Windows;
using DepotDL.GUI.Views;

namespace DepotDL.GUI.Services
{
    public static class DialogService
    {
        public static void ShowError(string title, string message)
        {
            var dialog = new ErrorDialog(title, message)
            {
                Owner = Application.Current.MainWindow
            };
            dialog.ShowDialog();
        }

        public static bool ShowConfirm(string title, string message)
            => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
               == MessageBoxResult.Yes;

        public static string? ShowInput(string title, string initialValue = "")
        {
            var dialog = new InputDialog(title, initialValue)
            {
                Owner = Application.Current.MainWindow
            };
            dialog.ShowDialog();
            return dialog.Result;
        }
    }
}
