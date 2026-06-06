// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace DepotDL.GUI.Views
{
    public partial class InputDialog : Window, INotifyPropertyChanged
    {
        public string DialogTitle { get; }

        private string _inputText = string.Empty;
        public string InputText
        {
            get => _inputText;
            set { _inputText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InputText))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string? Result { get; private set; }

        public InputDialog(string title, string initialValue = "")
        {
            InitializeComponent();
            DataContext = this;
            DialogTitle = title;
            InputText = initialValue;
        }

        protected override void OnSourceInitialized(System.EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (Owner != null)
            {
                Left = Owner.Left;
                Top = Owner.Top;
                Width = Owner.ActualWidth;
                Height = Owner.ActualHeight;
            }
            InputBox.Focus();
            InputBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) => Confirm();
        private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Confirm();
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
