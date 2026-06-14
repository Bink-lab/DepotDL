// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DepotDL.GUI.Views
{
    public partial class ConfirmDialog : Window
    {
        public string DialogTitle { get; }
        public string Message { get; }

        public ConfirmDialog() : this("Confirm", string.Empty) { }

        public ConfirmDialog(string title, string message)
        {
            DialogTitle = title;
            Message = message;
            InitializeComponent();
            DataContext = this;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            if (Owner is Window owner)
            {
                Position = owner.Position;
                Width = owner.Bounds.Width;
                Height = owner.Bounds.Height;
            }
            else
            {
                Width = 420;
                Height = 260;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e) => Close(true);
        private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);
    }
}
