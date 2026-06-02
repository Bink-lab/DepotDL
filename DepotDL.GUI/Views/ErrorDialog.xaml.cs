using System.Windows;

namespace DepotDL.GUI.Views
{
    public partial class ErrorDialog : Window
    {
        public string DialogTitle { get; }
        public string Message { get; }

        public ErrorDialog(string title, string message)
        {
            InitializeComponent();
            DataContext = this;
            DialogTitle = title;
            Message = message;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
