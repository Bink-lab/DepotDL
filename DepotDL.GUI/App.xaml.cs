// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
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
            try
            {
                var settings = new Services.SettingsService().Load();
                ApplyTheme(settings.Theme);
            }
            catch {}

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow();
            base.OnFrameworkInitializationCompleted();

            Dispatcher.UIThread.Post(() =>
            {
                if (Current == null) return;
                var variant = Current.RequestedThemeVariant;
                Current.RequestedThemeVariant = variant == ThemeVariant.Dark
                    ? ThemeVariant.Light
                    : ThemeVariant.Dark;
                Current.RequestedThemeVariant = variant;
            }, DispatcherPriority.Loaded);
        }

        public static void ApplyTheme(string? theme)
        {
            if (Current == null) return;
            Current.RequestedThemeVariant = (theme ?? "System") switch
            {
                "Light" => Avalonia.Styling.ThemeVariant.Light,
                "Dark" => Avalonia.Styling.ThemeVariant.Dark,
                _ => Avalonia.Styling.ThemeVariant.Default
            };
        }
    }
}
