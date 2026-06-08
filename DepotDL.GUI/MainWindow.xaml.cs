// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using DepotDL.GUI.ViewModels;

namespace DepotDL.GUI
{
    public partial class MainWindow : Window
    {
        private bool _isDragging;
        private Point _dragStart;
        private readonly CancellationTokenSource _cts = new();

        private FrameworkElement? _currentPage;
        private Dictionary<NavPage, FrameworkElement> _pages = new();
        private int _navVersion;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            Loaded += MainWindow_Loaded;
            Closing += (_, _) => _cts.Cancel();
            SizeChanged += (_, _) => UpdateContentClip();
            StateChanged += (_, _) => Margin = new Thickness(WindowState == WindowState.Maximized ? 7 : 0);

            _pages = new Dictionary<NavPage, FrameworkElement>
            {
                { NavPage.Store,     StoreViewEl },
                { NavPage.Library,   LibraryViewEl },
                { NavPage.Download,  DownloadViewEl },
                { NavPage.Settings,  SettingsViewEl },
            };
            _currentPage = LibraryViewEl;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }
            if (e.ChangedButton == MouseButton.Left)
            {
                _isDragging = true;
                _dragStart = e.GetPosition(this);
                ((System.Windows.FrameworkElement)sender).CaptureMouse();
            }
        }

        private void TitleBar_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ((System.Windows.FrameworkElement)sender).ReleaseMouseCapture();
        }

        private void TitleBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(this);
                var delta = pos - _dragStart;
                Left += delta.X;
                Top += delta.Y;
            }
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
            => ToggleMaximize();

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
            => Close();



        private void ToggleMaximize()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                Margin = new Thickness(0);
            }
            else
            {
                WindowState = WindowState.Maximized;
                Margin = new Thickness(7);
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateContentClip();
            await RunSplashscreenAsync();

            var vm = (MainViewModel)DataContext;
            vm.PropertyChanged += OnVmPropertyChanged;
            if (vm.CurrentPage != NavPage.Library)
                NavigateTo(vm.CurrentPage);
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentPage))
                NavigateTo(((MainViewModel)DataContext).CurrentPage);
        }

        private void NavigateTo(NavPage page)
        {
            if (!_pages.TryGetValue(page, out var newPage) || newPage == _currentPage) return;

            var oldPage = _currentPage;
            _currentPage = newPage;

            var myVersion = ++_navVersion;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var durIn = new Duration(TimeSpan.FromSeconds(0.20));

            foreach (var p in _pages.Values)
            {
                if (p != oldPage && p != newPage)
                {
                    p.BeginAnimation(OpacityProperty, null);
                    p.Visibility = Visibility.Collapsed;
                }
            }

            void ShowNew()
            {
                if (_navVersion != myVersion) return;
                newPage.BeginAnimation(OpacityProperty, null);
                newPage.Opacity = 0;
                newPage.Visibility = Visibility.Visible;
                newPage.BeginAnimation(OpacityProperty, new DoubleAnimation(1, durIn) { EasingFunction = ease });
            }

            if (oldPage != null)
            {
                var fadeOut = new DoubleAnimation(0, new Duration(TimeSpan.FromSeconds(0.15)));
                fadeOut.Completed += (_, _) =>
                {
                    oldPage.Visibility = Visibility.Collapsed;
                    ShowNew();
                };
                oldPage.BeginAnimation(OpacityProperty, fadeOut);
            }
            else
            {
                ShowNew();
            }
        }

        private void UpdateContentClip()
        {
            ContentGrid.Clip = new System.Windows.Media.RectangleGeometry(
                new Rect(0, 0, ContentGrid.ActualWidth, ContentGrid.ActualHeight), 14, 14);
        }

        private async System.Threading.Tasks.Task RunSplashscreenAsync()
        {
            double totalWidth = 200;
            var vm = (ViewModels.MainViewModel)DataContext;

            var progress = new Progress<(double pct, string status)>(update =>
            {
                SplashProgressBar.Width = update.pct * totalWidth;
                SplashStatus.Text = update.status;
            });

            await vm.InitializeAsync(progress, _cts.Token);
            await System.Threading.Tasks.Task.Delay(1000);

            var sb = new System.Windows.Media.Animation.Storyboard();

            var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 0,
                Duration = new Duration(System.TimeSpan.FromSeconds(0.5)),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };
            System.Windows.Media.Animation.Storyboard.SetTarget(opacityAnim, SplashOverlay);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(opacityAnim, new PropertyPath("Opacity"));
            sb.Children.Add(opacityAnim);

            var scaleXAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 1.06,
                Duration = new Duration(System.TimeSpan.FromSeconds(0.55)),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };
            System.Windows.Media.Animation.Storyboard.SetTarget(scaleXAnim, SplashOverlay);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleXAnim, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
            sb.Children.Add(scaleXAnim);

            var scaleYAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = 1.06,
                Duration = new Duration(System.TimeSpan.FromSeconds(0.55)),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };
            System.Windows.Media.Animation.Storyboard.SetTarget(scaleYAnim, SplashOverlay);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleYAnim, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
            sb.Children.Add(scaleYAnim);

            var translateYAnim = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = -30,
                Duration = new Duration(System.TimeSpan.FromSeconds(0.5)),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };
            System.Windows.Media.Animation.Storyboard.SetTarget(translateYAnim, SplashOverlay);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(translateYAnim, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)"));
            sb.Children.Add(translateYAnim);

            sb.Completed += (s, ev) =>
            {
                SplashOverlay.Visibility = Visibility.Collapsed;
            };

            sb.Begin(this);
        }
    }
}
