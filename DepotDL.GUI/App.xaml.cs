// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DepotDL.GUI.Helpers;

namespace DepotDL.GUI
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            Animation.RegisterCustomAnimator<ITransform?, TransformOpsAnimator>();
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow();
            base.OnFrameworkInitializationCompleted();
        }
    }
}
