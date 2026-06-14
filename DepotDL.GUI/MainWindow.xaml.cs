// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using DepotDL.GUI.ViewModels;

namespace DepotDL.GUI
{
    public partial class MainWindow : Window
    {
        private readonly CancellationTokenSource _cts = new();
        private Control? _currentPage;
        private Dictionary<NavPage, Control> _pages = new();
        private int _navVersion;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            Loaded += MainWindow_Loaded;
            Closing += (_, _) => _cts.Cancel();
            SizeChanged += (_, _) => UpdateContentClip();

            _pages = new Dictionary<NavPage, Control>
            {
                { NavPage.Store,    StoreViewEl },
                { NavPage.Library,  LibraryViewEl },
                { NavPage.Download, DownloadViewEl },
                { NavPage.Settings, SettingsViewEl },
            };
            _currentPage = LibraryViewEl;
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private void MinimizeBtn_Click(object? sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeBtn_Click(object? sender, RoutedEventArgs e)
            => ToggleMaximize();

        private void CloseBtn_Click(object? sender, RoutedEventArgs e)
            => Close();

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            UpdateContentClip();
            await RunSplashscreenAsync();

            var vm = (MainViewModel)DataContext!;
            vm.PropertyChanged += OnVmPropertyChanged;
            if (vm.CurrentPage != NavPage.Library)
                NavigateTo(vm.CurrentPage);
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentPage))
                NavigateTo(((MainViewModel)DataContext!).CurrentPage);
        }

        private async void NavigateTo(NavPage page)
        {
            if (!_pages.TryGetValue(page, out var newPage) || newPage == _currentPage) return;

            var oldPage = _currentPage;
            _currentPage = newPage;
            var myVersion = ++_navVersion;

            foreach (var p in _pages.Values)
                if (p != oldPage && p != newPage)
                    p.IsVisible = false;

            if (oldPage != null)
            {
                oldPage.Opacity = 0;
                await Task.Delay(150);
                if (_navVersion != myVersion) return;
                oldPage.IsVisible = false;
            }

            newPage.Opacity = 0;
            newPage.IsVisible = true;
            await Dispatcher.UIThread.InvokeAsync(() => { });
            if (_navVersion != myVersion) return;
            newPage.Opacity = 1;
        }

        private void UpdateContentClip()
        {
            ContentGrid.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, ContentGrid.Bounds.Width, ContentGrid.Bounds.Height),
                RadiusX = 14,
                RadiusY = 14
            };
        }

        private async Task RunSplashscreenAsync()
        {
            double totalWidth = 200;
            var vm = (MainViewModel)DataContext!;

            var progress = new Progress<(double pct, string status)>(update =>
            {
                SplashProgressBar.Width = update.pct * totalWidth;
                SplashStatus.Text = update.status;
            });

            await vm.InitializeAsync(progress, _cts.Token);
            await Task.Delay(1000);

            var tg = SplashOverlay.RenderTransform as TransformGroup;
            ScaleTransform? scaleT = null;
            TranslateTransform? translateT = null;
            if (tg != null)
            {
                foreach (var t in tg.Children)
                {
                    if (t is ScaleTransform st) scaleT = st;
                    else if (t is TranslateTransform tt) translateT = tt;
                }
            }

            var startTime = DateTime.UtcNow;
            const double durationMs = 500.0;
            while (true)
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                var t = Math.Min(1.0, elapsed / durationMs);
                var eased = CubicEaseInOut(t);
                SplashOverlay.Opacity = 1 - eased;
                if (scaleT != null) { scaleT.ScaleX = 1 + 0.06 * eased; scaleT.ScaleY = 1 + 0.06 * eased; }
                if (translateT != null) translateT.Y = -30 * eased;
                if (t >= 1.0) break;
                await Task.Delay(16);
            }
            SplashOverlay.IsVisible = false;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == WindowStateProperty)
                Margin = new Thickness(change.GetNewValue<WindowState>() == WindowState.Maximized ? 7 : 0);
        }

        private static double CubicEaseInOut(double t)
            => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }
}
