using System;
using System.Windows;
using System.Windows.Forms.Integration;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace StreamMesh.UI.Controls
{
    /// <summary>
    /// Hosts a native Win32 surface for hardware-accelerated libmpv (--wid)
    /// while rendering crisp transparent WPF OSD overlays in a synchronized owned window.
    /// </summary>
    [ContentProperty(nameof(OverlayContent))]
    public class MpvVideoHost : System.Windows.Controls.UserControl, IDisposable
    {
        private readonly WindowsFormsHost _wfHost;
        private readonly System.Windows.Forms.Panel _videoPanel;
        private Window? _overlayWindow;
        private Window? _ownerWindow;
        private bool _disposed;

        public static readonly DependencyProperty OverlayContentProperty =
            DependencyProperty.Register(
                nameof(OverlayContent),
                typeof(UIElement),
                typeof(MpvVideoHost),
                new PropertyMetadata(null, OnOverlayContentChanged));

        public UIElement? OverlayContent
        {
            get => (UIElement?)GetValue(OverlayContentProperty);
            set => SetValue(OverlayContentProperty, value);
        }

        public IntPtr VideoHandle
        {
            get
            {
                if (!_videoPanel.IsHandleCreated)
                    _videoPanel.CreateControl();
                return _videoPanel.Handle;
            }
        }

        public event EventHandler? VideoMouseActivity;
        public event EventHandler? VideoDoubleClick;

        public MpvVideoHost()
        {
            _videoPanel = new System.Windows.Forms.Panel
            {
                BackColor = System.Drawing.Color.Black,
                Dock = System.Windows.Forms.DockStyle.Fill,
                Margin = System.Windows.Forms.Padding.Empty,
                TabStop = false
            };

            _videoPanel.MouseMove += (_, _) => VideoMouseActivity?.Invoke(this, EventArgs.Empty);
            _videoPanel.MouseDown += (_, _) => VideoMouseActivity?.Invoke(this, EventArgs.Empty);
            _videoPanel.DoubleClick += (_, _) => VideoDoubleClick?.Invoke(this, EventArgs.Empty);

            _wfHost = new WindowsFormsHost
            {
                Child = _videoPanel,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };

            Content = _wfHost;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += (_, _) => SyncOverlayBounds();
            IsVisibleChanged += (_, _) => SyncOverlayBounds();
            LayoutUpdated += (_, _) => SyncOverlayBounds();
        }

        private static void OnOverlayContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MpvVideoHost host && host._overlayWindow != null)
                host._overlayWindow.Content = e.NewValue;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_disposed) return;

            var win = Window.GetWindow(this);
            if (!ReferenceEquals(_ownerWindow, win))
            {
                DetachOwnerHooks();
                _ownerWindow = win;
                AttachOwnerHooks();
            }

            EnsureOverlayWindow();
            SyncOverlayBounds();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_overlayWindow != null && _overlayWindow.IsVisible)
                _overlayWindow.Hide();
        }

        private void AttachOwnerHooks()
        {
            if (_ownerWindow == null) return;
            _ownerWindow.LocationChanged += OwnerChanged;
            _ownerWindow.SizeChanged += OwnerChanged;
            _ownerWindow.StateChanged += OwnerChanged;
            _ownerWindow.IsVisibleChanged += OwnerVisibleChanged;
            _ownerWindow.Closing += OwnerClosing;
        }

        private void DetachOwnerHooks()
        {
            if (_ownerWindow == null) return;
            _ownerWindow.LocationChanged -= OwnerChanged;
            _ownerWindow.SizeChanged -= OwnerChanged;
            _ownerWindow.StateChanged -= OwnerChanged;
            _ownerWindow.IsVisibleChanged -= OwnerVisibleChanged;
            _ownerWindow.Closing -= OwnerClosing;
        }

        private void OwnerChanged(object? sender, EventArgs e) => SyncOverlayBounds();
        private void OwnerVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => SyncOverlayBounds();

        private void OwnerClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            DisposeOverlayWindow();
        }

        private void EnsureOverlayWindow()
        {
            if (_overlayWindow != null || _ownerWindow == null) return;

            _overlayWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                ResizeMode = ResizeMode.NoResize,
                Owner = _ownerWindow,
                Content = OverlayContent
            };

            _overlayWindow.PreviewKeyDown += (_, keyArgs) =>
            {
                if (_ownerWindow is UI.Windows.MainWindow mainWin)
                    mainWin.HandleForwardedKeyDown(keyArgs);
            };

            _overlayWindow.MouseMove += (_, _) => VideoMouseActivity?.Invoke(this, EventArgs.Empty);
            _overlayWindow.MouseLeftButtonDown += (_, mouseArgs) =>
            {
                VideoMouseActivity?.Invoke(this, EventArgs.Empty);

                bool isEmptySurface = mouseArgs.OriginalSource is System.Windows.Controls.Grid g && g.Name == "PlayerOverlay";
                if (!isEmptySurface) return;

                if (mouseArgs.ClickCount == 2)
                {
                    VideoDoubleClick?.Invoke(this, EventArgs.Empty);
                    mouseArgs.Handled = true;
                }
                else if (_ownerWindow is UI.Windows.MainWindow mainWin)
                {
                    mainWin.TryDragMiniPlayer();
                }
            };
        }

        public void SyncOverlayBounds()
        {
            if (_disposed || _overlayWindow == null || _ownerWindow == null) return;

            try
            {
                bool visible = IsVisible &&
                               ActualWidth > 1 &&
                               ActualHeight > 1 &&
                               _ownerWindow.IsVisible &&
                               _ownerWindow.WindowState != WindowState.Minimized &&
                               PresentationSource.FromVisual(this) is PresentationSource source &&
                               source.CompositionTarget != null;

                if (!visible)
                {
                    if (_overlayWindow.IsVisible) _overlayWindow.Hide();
                    return;
                }

                var targetSource = PresentationSource.FromVisual(this)!;
                var fromDevice = targetSource.CompositionTarget!.TransformFromDevice;

                System.Windows.Point topLeftPx = PointToScreen(new System.Windows.Point(0, 0));
                System.Windows.Point bottomRightPx = PointToScreen(new System.Windows.Point(ActualWidth, ActualHeight));
                System.Windows.Point topLeftDip = fromDevice.Transform(topLeftPx);
                System.Windows.Point bottomRightDip = fromDevice.Transform(bottomRightPx);

                double w = Math.Max(1, bottomRightDip.X - topLeftDip.X);
                double h = Math.Max(1, bottomRightDip.Y - topLeftDip.Y);

                if (Math.Abs(_overlayWindow.Left - topLeftDip.X) > 0.5) _overlayWindow.Left = topLeftDip.X;
                if (Math.Abs(_overlayWindow.Top - topLeftDip.Y) > 0.5) _overlayWindow.Top = topLeftDip.Y;
                if (Math.Abs(_overlayWindow.Width - w) > 0.5) _overlayWindow.Width = w;
                if (Math.Abs(_overlayWindow.Height - h) > 0.5) _overlayWindow.Height = h;

                if (_overlayWindow.Topmost != _ownerWindow.Topmost)
                    _overlayWindow.Topmost = _ownerWindow.Topmost;

                if (!_overlayWindow.IsVisible)
                {
                    _overlayWindow.Show();
                    if (_ownerWindow.IsActive)
                        _ownerWindow.Activate();
                }
            }
            catch { }
        }

        private void DisposeOverlayWindow()
        {
            if (_overlayWindow == null) return;
            try
            {
                _overlayWindow.Content = null;
                _overlayWindow.Close();
            }
            catch { }
            _overlayWindow = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DetachOwnerHooks();
            DisposeOverlayWindow();
            try { _wfHost.Dispose(); } catch { }
            try { _videoPanel.Dispose(); } catch { }
        }
    }
}
