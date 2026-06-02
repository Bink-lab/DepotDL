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
    }
}
