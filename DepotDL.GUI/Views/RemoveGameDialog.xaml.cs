using System.Windows;

namespace DepotDL.GUI.Views
{
    public partial class RemoveGameDialog : Window
    {
        public bool DeleteFiles { get; set; }

        public RemoveGameDialog(string gameName)
        {
            InitializeComponent();
            DataContext = this;
            GameName = gameName;
        }

        public string GameName { get; set; }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
