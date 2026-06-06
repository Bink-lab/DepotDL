// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Windows;
using System.Windows.Controls;
using DepotDL.GUI.ViewModels;

namespace DepotDL.GUI.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => SyncPasswordBox();
        }

        private void SyncPasswordBox()
        {
            if (DataContext is SettingsViewModel vm)
                OnlineFixPasswordBox.Password = vm.OnlineFixPass;
        }

        private void OnlineFixPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel vm)
                vm.OnlineFixPass = OnlineFixPasswordBox.Password;
        }
    }
}
