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

        protected override void OnSourceInitialized(System.EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (Owner != null)
            {
                Left   = Owner.Left;
                Top    = Owner.Top;
                Width  = Owner.ActualWidth;
                Height = Owner.ActualHeight;
            }
            else
            {
                Width  = 420;
                Height = 300;
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
