// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DepotDL.GUI.Views
{
    public partial class ErrorDialog : Window
    {
        public string DialogTitle { get; }
        public string Message { get; }

        public ErrorDialog() : this("Error", string.Empty) { }

        public ErrorDialog(string title, string message)
        {
            DialogTitle = title;
            Message = message;
            if (Avalonia.Application.Current != null)
                RequestedThemeVariant = Avalonia.Application.Current.RequestedThemeVariant;
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
                Height = 300;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e) => Close();
    }
}
