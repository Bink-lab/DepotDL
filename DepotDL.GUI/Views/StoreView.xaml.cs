// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace DepotDL.GUI.Views
{
    public partial class StoreView : UserControl
    {
        private bool _isDragging;
        private Point _dragStart;
        private double _scrollStart;
        private IPointer? _capturedPointer;

        public StoreView() => InitializeComponent();

        private void ScreenshotScroller_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(ScreenshotScroller).Properties.IsLeftButtonPressed) return;
            _isDragging = true;
            _dragStart = e.GetPosition(ScreenshotScroller);
            _scrollStart = ScreenshotScroller.Offset.X;
            _capturedPointer = e.Pointer;
            e.Pointer.Capture(ScreenshotScroller);
            ScreenshotScroller.Cursor = new Cursor(StandardCursorType.SizeWestEast);
            e.Handled = true;
        }

        private void ScreenshotScroller_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDragging) return;
            var delta = _dragStart.X - e.GetPosition(ScreenshotScroller).X;
            ScreenshotScroller.Offset = ScreenshotScroller.Offset.WithX(_scrollStart + delta);
        }

        private void ScreenshotScroller_PointerReleased(object? sender, PointerReleasedEventArgs e)
            => StopDrag();

        private void ScreenshotScroller_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
            => StopDrag();

        private void StopDrag()
        {
            if (!_isDragging) return;
            _isDragging = false;
            _capturedPointer?.Capture(null);
            _capturedPointer = null;
            ScreenshotScroller.Cursor = new Cursor(StandardCursorType.Hand);
        }

        private void ScreenshotScroller_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            ScreenshotScroller.Offset = ScreenshotScroller.Offset.WithX(
                ScreenshotScroller.Offset.X - e.Delta.Y * 40);
            e.Handled = true;
        }
    }
}
