// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DepotDL.GUI.Views
{
    public partial class StoreView : UserControl
    {
        private bool _isDragging;
        private Point _dragStart;
        private double _scrollStart;

        public StoreView() => InitializeComponent();

        private void ScreenshotScroller_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragStart = e.GetPosition(ScreenshotScroller);
            _scrollStart = ScreenshotScroller.HorizontalOffset;
            ScreenshotScroller.CaptureMouse();
            ScreenshotScroller.Cursor = Cursors.SizeWE;
            e.Handled = true;
        }

        private void ScreenshotScroller_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            var delta = _dragStart.X - e.GetPosition(ScreenshotScroller).X;
            ScreenshotScroller.ScrollToHorizontalOffset(_scrollStart + delta);
        }

        private void ScreenshotScroller_MouseUp(object sender, MouseButtonEventArgs e)
        {
            StopDrag();
        }

        private void ScreenshotScroller_MouseLeave(object sender, MouseEventArgs e)
        {
            StopDrag();
        }

        private void StopDrag()
        {
            if (!_isDragging) return;
            _isDragging = false;
            ScreenshotScroller.ReleaseMouseCapture();
            ScreenshotScroller.Cursor = Cursors.Hand;
        }
    }
}
