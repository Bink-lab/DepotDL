// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DepotDL.GUI.Views
{
    public partial class InputDialog : Window
    {
        public string DialogTitle { get; }
        public string InputText { get; set; } = string.Empty;
        public string? Result { get; private set; }

        public InputDialog() : this("Input") { }

        public InputDialog(string title, string initialValue = "")
        {
            DialogTitle = title;
            InputText = initialValue;
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
            InputBox.Focus();
            InputBox.SelectAll();
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e) => Confirm();
        private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close();

        private void InputBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return) Confirm();
            else if (e.Key == Key.Escape) Close();
        }

        private void Confirm()
        {
            if (string.IsNullOrWhiteSpace(InputText)) return;
            Result = InputText.Trim();
            Close();
        }
    }
}
