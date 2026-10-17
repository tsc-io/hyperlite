namespace Hyperlite.Explorer
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using System.Windows.Interop;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using System.Windows.Navigation;
    using System.Windows.Shapes;

    internal class ChromeWindow : Window
    {
        private const int EdgeSize = 23;

        /// <summary>
        /// The WM_GETMINMAXINFO message id.
        /// </summary>
        private const int WM_GETMINMAXINFO = 0x0024;

        /// <summary>
        /// The WM_SIZING message id.
        /// </summary>
        private const int WM_SIZING = 0x0214;

        /// <summary>
        /// The WM_SYSCOMMAND message id.
        /// </summary>
        private const int WM_SYSCOMMAND = 0x112;

        private static readonly TimeSpan DoubleClickDelay = TimeSpan.FromMilliseconds(500);

        private static readonly Brush InactiveBorderBrush = new SolidColorBrush(Color.FromRgb(0x91, 0x91, 0x91));

        private Grid chromeFrame;

        private DateTime headerLastClicked;

        private HwndSource hwndSource;

        private HwndSource[] hwndSourceSurrounds;

        private Path resize;

        private Border roundBorder;

        private Window wndBottom;

        private Window wndLeft;

        private Window wndRight;

        private Window wndTop;

        public ChromeWindow()
        {
            this.Loaded += (s, e) =>
                {
                    if (InactiveBorderBrush.CanFreeze)
                    {
                        InactiveBorderBrush.Freeze();
                    }

                    this.ResizeSurrounds();
                    this.ShowSurrounds();

                    this.roundBorder = (Border)this.Template.FindName("RoundBorder", this);
                    this.chromeFrame = (Grid)this.Template.FindName("ChromeFrame", this);
                    this.resize = (Path)this.Template.FindName("Resize", this);

                    var chromeHeaderRectangle = (Rectangle)this.Template.FindName("ChromeHeader", this);
                    chromeHeaderRectangle.PreviewMouseDown += this.HandleChromeHeaderPreviewMouseDown;

                    var chromeButton = (Button)this.Template.FindName("ChromeMinimizeButton", this);
                    chromeButton.Click += this.HandleChromeMinimizeClick;

                    chromeButton = (Button)this.Template.FindName("ChromeRestoreButton", this);
                    chromeButton.Click += this.HandleChromeRestoreClick;

                    chromeButton = (Button)this.Template.FindName("ChromeCloseButton", this);
                    chromeButton.Click += this.HandleChromeCloseClick;

                    foreach (var chromeBorder in this.chromeFrame.Children.OfType<Rectangle>())
                    {
                        chromeBorder.PreviewMouseDown += this.HandleChromeFramePreviewMouseDown;
                        chromeBorder.MouseMove += this.HandleChromeFrameMouseMove;
                    }

                    this.PreviewMouseMove += this.HandlePreviewMouseMove;
                    this.LocationChanged += this.HandleLocationChanged;
                    this.SizeChanged += this.HandleLocationChanged;
                    this.StateChanged += this.HandleWndStateChanged;

                    this.Activated += this.HandleActivated;
                    this.Deactivated += this.HandleDeactivated;
                };
        }

        private enum ResizeDirection
        {
            Left = 1,

            Right = 2,

            Top = 3,

            TopLeft = 4,

            TopRight = 5,

            Bottom = 6,

            BottomLeft = 7,

            BottomRight = 8,
        }

        /// <param name="e">An <see cref="T:System.EventArgs"/> that contains the event data.</param>
        protected override void OnClosed(EventArgs e)
        {
            this.CloseSurrounds();
            base.OnClosed(e);
        }

        /// <summary>
        /// Raises the <see cref="E:System.Windows.FrameworkElement.Initialized"/> event.
        /// This method is invoked whenever
        /// <see cref="P:System.Windows.FrameworkElement.IsInitialized"/> is set to true internally.
        /// </summary>
        /// <param name="e">The <see cref="T:System.Windows.RoutedEventArgs"/>
        /// that contains the event data.</param>
        protected override void OnInitialized(EventArgs e)
        {
            this.AllowsTransparency = false;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.NoResize;

            this.InitializeSurrounds();
            base.OnInitialized(e);
        }

        /// <param name="e">An <see cref="T:System.EventArgs"/> that contains the event data.
        /// </param>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            this.hwndSource = (HwndSource)PresentationSource.FromVisual(this);

            // Returns the HwndSource object for the window
            // which presents WPF content in a Win32 window.
            HwndSource.FromHwnd(this.hwndSource.Handle).AddHook(this.WindowProc);
        }

        private static Window CreateTransparentWindow()
        {
            return new Window
            {
                AllowsTransparency = true,
                Visibility = Visibility.Hidden,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Background = null,
                Width = 0,
                Height = 0
            };
        }

        private static void HandleGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));

            // Adjust the maximized size and position to fit the work area
            // of the correct monitor.
            const int MONITOR_DEFAULTTONEAREST = 0x00000002;

            var monitor = NativeMethods.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new MONITORINFO();
                NativeMethods.GetMonitorInfo(monitor, monitorInfo);

                var rcWorkArea = monitorInfo.rcWork;
                var rcMonitorArea = monitorInfo.rcMonitor;

                mmi.ptMaxPosition.x = Math.Abs(rcWorkArea.left - rcMonitorArea.left);
                mmi.ptMaxPosition.y = Math.Abs(rcWorkArea.top - rcMonitorArea.top);

                mmi.ptMaxSize.x = Math.Abs(rcWorkArea.right - rcWorkArea.left);
                mmi.ptMaxSize.y = Math.Abs(rcWorkArea.bottom - rcWorkArea.top);
            }

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        private void CloseSurrounds()
        {
            this.wndTop.Close();
            this.wndLeft.Close();
            this.wndBottom.Close();
            this.wndRight.Close();
        }

        [DebuggerStepThrough]
        private Decorator GetDecorator(string imageUri, double radius = 0)
        {
            return new Border { CornerRadius = new CornerRadius(radius), Background = new ImageBrush(new BitmapImage(new Uri(BaseUriHelper.GetBaseUri(this), imageUri))) };
        }

        private void HandleActivated(object sender, EventArgs e)
        {
            this.SetSurroundShadows();
            this.roundBorder.BorderBrush = Brushes.Black;
            this.resize.Fill = Brushes.Black;
        }

        private void HandleChromeCloseClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void HandleChromeFrameMouseMove(object sender, MouseEventArgs e)
        {
            var clickedRectangle = (Rectangle)sender;

            switch (clickedRectangle.Name)
            {
                case "Top":
                    this.Cursor = Cursors.SizeNS;
                    break;
                case "Bottom":
                    this.Cursor = Cursors.SizeNS;
                    break;
                case "Left":
                    this.Cursor = Cursors.SizeWE;
                    break;
                case "Right":
                    this.Cursor = Cursors.SizeWE;
                    break;
                case "TopLeft":
                    this.Cursor = Cursors.SizeNWSE;
                    break;
                case "TopRight":
                    this.Cursor = Cursors.SizeNESW;
                    break;
                case "BottomLeft":
                    this.Cursor = Cursors.SizeNESW;
                    break;
                case "BottomRight":
                    this.Cursor = Cursors.SizeNWSE;
                    break;
            }
        }

        private void HandleChromeFramePreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var clickedRectangle = (Rectangle)sender;

            switch (clickedRectangle.Name)
            {
                case "Top":
                    this.Cursor = Cursors.SizeNS;
                    this.ResizeWindow(ResizeDirection.Top);
                    break;
                case "Bottom":
                    this.Cursor = Cursors.SizeNS;
                    this.ResizeWindow(ResizeDirection.Bottom);
                    break;
                case "Left":
                    this.Cursor = Cursors.SizeWE;
                    this.ResizeWindow(ResizeDirection.Left);
                    break;
                case "Right":
                    this.Cursor = Cursors.SizeWE;
                    this.ResizeWindow(ResizeDirection.Right);
                    break;
                case "TopLeft":
                    this.Cursor = Cursors.SizeNWSE;
                    this.ResizeWindow(ResizeDirection.TopLeft);
                    break;
                case "TopRight":
                    this.Cursor = Cursors.SizeNESW;
                    this.ResizeWindow(ResizeDirection.TopRight);
                    break;
                case "BottomLeft":
                    this.Cursor = Cursors.SizeNESW;
                    this.ResizeWindow(ResizeDirection.BottomLeft);
                    break;
                case "BottomRight":
                    this.Cursor = Cursors.SizeNWSE;
                    this.ResizeWindow(ResizeDirection.BottomRight);
                    break;
            }
        }

        private void HandleChromeHeaderPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DateTime.Now.Subtract(this.headerLastClicked) <= DoubleClickDelay)
            {
                // Execute the code inside the event handler for the
                // restore button click passing null for the sender
                // and null for the event args.
                this.HandleChromeRestoreClick(null, null);
            }

            this.headerLastClicked = DateTime.Now;
            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void HandleChromeMinimizeClick(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void HandleChromeRestoreClick(object sender, RoutedEventArgs e)
        {
            this.WindowState = (this.WindowState == WindowState.Normal) ? WindowState.Maximized : WindowState.Normal;
            this.chromeFrame.IsHitTestVisible = this.WindowState != WindowState.Maximized;
            this.resize.Visibility = (this.WindowState == WindowState.Maximized) ? Visibility.Hidden : Visibility.Visible;
            this.roundBorder.Visibility = (this.WindowState == WindowState.Maximized) ? Visibility.Hidden : Visibility.Visible;
        }

        private void HandleDeactivated(object sender, EventArgs e)
        {
            this.SetSurroundShadows(false);
            this.roundBorder.BorderBrush = InactiveBorderBrush;
            this.resize.Fill = InactiveBorderBrush;
        }

        private void HandleLocationChanged(object sender, EventArgs e)
        {
            this.ResizeSurrounds();
        }

        [DebuggerStepThrough]
        private void HandlePreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (Mouse.LeftButton != MouseButtonState.Pressed)
            {
                this.Cursor = Cursors.Arrow;
            }
        }

        private void HandleSizing(IntPtr wParam, IntPtr lParam)
        {
            const int WMSZ_LEFT = 1;
            const int WMSZ_RIGHT = 2;
            const int WMSZ_TOP = 3;
            const int WMSZ_TOPLEFT = 4;
            const int WMSZ_TOPRIGHT = 5;
            const int WMSZ_BOTTOM = 6;
            const int WMSZ_BOTTOMLEFT = 7;
            const int WMSZ_BOTTOMRIGHT = 8;

            var rect = (RECT)Marshal.PtrToStructure(lParam, typeof(RECT));
            var wd = rect.right - rect.left - (int)this.MinWidth;
            var hd = rect.bottom - rect.top - (int)this.MinHeight;

            if (wd < 0 || hd < 0)
            {
                if (wd < 0)
                {
                    switch (wParam.ToInt32())
                    {
                        case WMSZ_LEFT:
                        case WMSZ_TOPLEFT:
                        case WMSZ_BOTTOMLEFT:
                            if (wd < 0)
                            {
                                rect.left += wd;
                            }

                            break;
                        case WMSZ_RIGHT:
                        case WMSZ_TOPRIGHT:
                        case WMSZ_BOTTOMRIGHT:
                            if (wd < 0)
                            {
                                rect.right -= wd;
                            }

                            break;
                    }
                }

                if (hd < 0)
                {
                    switch (wParam.ToInt32())
                    {
                        case WMSZ_TOP:
                        case WMSZ_TOPLEFT:
                        case WMSZ_TOPRIGHT:
                            if (hd < 0)
                            {
                                rect.top += hd;
                            }

                            break;
                        case WMSZ_BOTTOM:
                        case WMSZ_BOTTOMLEFT:
                        case WMSZ_BOTTOMRIGHT:
                            if (hd < 0)
                            {
                                rect.bottom -= hd;
                            }

                            break;
                    }
                }

                Marshal.StructureToPtr(rect, lParam, false);
            }
        }

        private void HandleWndStateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Normal)
            {
                this.ShowSurrounds();
            }
            else
            {
                this.HideSurrounds();
            }
        }

        private void HideSurrounds()
        {
            this.wndTop.Hide();
            this.wndLeft.Hide();
            this.wndBottom.Hide();
            this.wndRight.Hide();
        }

        private void InitializeSurrounds()
        {
            this.wndTop = CreateTransparentWindow();
            this.wndLeft = CreateTransparentWindow();
            this.wndBottom = CreateTransparentWindow();
            this.wndRight = CreateTransparentWindow();

            this.SetSurroundShadows();
        }

        private void ResizeSurrounds()
        {
            if (PresentationSource.FromVisual(this.wndTop) != null)
            {
                if (this.hwndSourceSurrounds == null)
                {
                    this.hwndSourceSurrounds = new[]
                        {
                            (HwndSource)PresentationSource.FromVisual(this.wndTop),
                            (HwndSource)PresentationSource.FromVisual(this.wndLeft),
                            (HwndSource)PresentationSource.FromVisual(this.wndBottom),
                            (HwndSource)PresentationSource.FromVisual(this.wndRight)
                        };
                }

                var scaleX = this.hwndSourceSurrounds[0].CompositionTarget.TransformToDevice.M11;
                var scaleY = this.hwndSourceSurrounds[0].CompositionTarget.TransformToDevice.M22;

                var dwp = NativeMethods.BeginDeferWindowPos(4);

                NativeMethods.DeferWindowPos(
                    dwp, this.hwndSourceSurrounds[0].Handle,
                    IntPtr.Zero,
                    (int)((this.Left - EdgeSize) * scaleX),
                    (int)((this.Top - EdgeSize) * scaleY),
                    (int)((this.Width + EdgeSize * 2) * scaleX),
                    (int)(EdgeSize * scaleY),
                    0x10);

                NativeMethods.DeferWindowPos(dwp,
                    this.hwndSourceSurrounds[1].Handle,
                    IntPtr.Zero,
                    (int)((this.Left - EdgeSize) * scaleX),
                    (int)(this.Top * scaleY),
                    (int)(EdgeSize * scaleX),
                    (int)(this.Height * scaleY),
                    0x10);

                NativeMethods.DeferWindowPos(
                    dwp,
                    this.hwndSourceSurrounds[2].Handle,
                    IntPtr.Zero,
                    (int)((this.Left - EdgeSize) * scaleX),
                    (int)((this.Top + this.Height) * scaleY),
                    (int)((this.Width + EdgeSize * 2) * scaleX),
                    (int)(EdgeSize * scaleY),
                    0x10);

                NativeMethods.DeferWindowPos(
                    dwp,
                    this.hwndSourceSurrounds[3].Handle,
                    IntPtr.Zero,
                    (int)((this.Left + this.Width) * scaleX),
                    (int)(this.Top * scaleY),
                    (int)(EdgeSize * scaleX),
                    (int)(this.Height * scaleY),
                    0x10);

                NativeMethods.EndDeferWindowPos(dwp);
            }
            else
            {
                this.wndTop.Left = this.Left - EdgeSize;
                this.wndTop.Top = this.Top - EdgeSize;
                this.wndTop.Width = this.Width + EdgeSize * 2;
                this.wndTop.Height = EdgeSize;

                this.wndLeft.Left = this.Left - EdgeSize;
                this.wndLeft.Top = this.Top;
                this.wndLeft.Width = EdgeSize;
                this.wndLeft.Height = this.Height;

                this.wndBottom.Left = this.Left - EdgeSize;
                this.wndBottom.Top = this.Top + this.Height;
                this.wndBottom.Width = this.Width + EdgeSize * 2;
                this.wndBottom.Height = EdgeSize;

                this.wndRight.Left = this.Left + this.Width;
                this.wndRight.Top = this.Top;
                this.wndRight.Width = EdgeSize;
                this.wndRight.Height = this.Height;
            }
        }

        private void ResizeWindow(ResizeDirection direction)
        {
            NativeMethods.SendMessage(this.hwndSource.Handle, WM_SYSCOMMAND, (IntPtr)(61440 + direction), IntPtr.Zero);
        }

        private void SetSurroundShadows(bool active = true)
        {
            if (active)
            {
                const double CornerRadius = 1.75;

                this.wndTop.Content = this.GetDecorator("Resources/ActiveShadowTop.png");
                this.wndLeft.Content = this.GetDecorator("Resources/ActiveShadowLeft.png", CornerRadius);
                this.wndBottom.Content = this.GetDecorator("Resources/ActiveShadowBottom.png");
                this.wndRight.Content = this.GetDecorator("Resources/ActiveShadowRight.png", CornerRadius);
            }
            else
            {
                this.wndTop.Content = this.GetDecorator("Resources/InactiveShadowTop.png");
                this.wndLeft.Content = this.GetDecorator("Resources/InactiveShadowLeft.png");
                this.wndBottom.Content = this.GetDecorator("Resources/InactiveShadowBottom.png");
                this.wndRight.Content = this.GetDecorator("Resources/InactiveShadowRight.png");
            }

            this.ResizeSurrounds();
        }

        private void ShowSurrounds()
        {
            this.wndTop.Show();
            this.wndLeft.Show();
            this.wndBottom.Show();
            this.wndRight.Show();
        }

        [DebuggerStepThrough]
        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_GETMINMAXINFO:
                    HandleGetMinMaxInfo(hwnd, lParam);
                    handled = true;
                    break;

                case WM_SIZING:
                    this.HandleSizing(wParam, lParam);
                    handled = true;
                    break;
            }

            return (IntPtr)0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public readonly POINT ptReserved;

            public POINT ptMaxSize;

            public POINT ptMaxPosition;

            public readonly POINT ptMinTrackSize;

            public readonly POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;

            public int y;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        private struct RECT
        {
            public int left;

            public int top;

            public int right;

            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MONITORINFO
        {
            public readonly int cbSize;

            public readonly RECT rcMonitor;

            public readonly RECT rcWork;

            public readonly int dwFlags;

            public MONITORINFO()
            {
                this.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                this.rcMonitor = new RECT();
                this.rcWork = new RECT();
                this.dwFlags = 0;
            }
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern IntPtr BeginDeferWindowPos(int nNumWindows);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr DeferWindowPos(IntPtr hWinPosInfo, IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool EndDeferWindowPos(IntPtr hWinPosInfo);

            [DllImport("user32")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

            [DllImport("User32")]
            public static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);
        }
    }
}
