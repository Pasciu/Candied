using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Candied.Studio.Native;
using Candied.Studio.Services;
using Candied.Studio.ViewModels;
using Candied.Vision.Hosting;

namespace Candied.Studio.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        _viewModel.GetContainerHwndFunc = () => GameHwndHost.ContainerHwnd;
        _viewModel.GetContainerPixelSizeFunc = () =>
        {
            UpdateStageViewportSize();
            return GameHwndHost.GetCurrentPixelSize();
        };
        _viewModel.ViewportPresetChanged += OnViewportPresetOrSidebarChanged;

        GameHwndHost.HostResized += (pxW, pxH) => _viewModel.OnHostResized(pxW, pxH);

        if (_viewModel.FilteredLogEntries is INotifyCollectionChanged notifyCollection)
        {
            notifyCollection.CollectionChanged += (_, _) =>
            {
                if (LogsList.Items.Count > 0)
                {
                    LogsList.ScrollIntoView(LogsList.Items[^1]);
                }
            };
        }

        Loaded += (_, _) =>
        {
            EnsureVisibleAndActive();
            UpdateStageViewportSize();
        };
        Closed += OnWindowClosed;
    }

    private void OnViewportPresetOrSidebarChanged()
    {
        SidebarColumn.Width = _viewModel.IsSidebarVisible
            ? new GridLength(485)
            : new GridLength(0);

        Dispatcher.InvokeAsync(UpdateStageViewportSize, System.Windows.Threading.DispatcherPriority.Render);
    }

    private void OnStageContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateStageViewportSize();
    }

    private void UpdateStageViewportSize()
    {
        if (StageContainer.ActualWidth <= 0 || StageContainer.ActualHeight <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        string presetKey = _viewModel.SelectedViewportPreset?.Key ?? "auto_16_9";

        var (dipW, dipH, pxW, pxH) = ViewportCalculator.CalculateStageViewport(
            StageContainer.ActualWidth,
            StageContainer.ActualHeight,
            dpi.DpiScaleX,
            dpi.DpiScaleY,
            presetKey);

        GameBezelBorder.Width = dipW;
        GameBezelBorder.Height = dipH;

        if (GameHwndHost.ContainerHwnd != IntPtr.Zero)
        {
            Win32Native.MoveWindow(GameHwndHost.ContainerHwnd, 0, 0, pxW, pxH, true);
        }

        _viewModel.OnHostResized(pxW, pxH);
    }

    private void OnScreenshotImageMouseMove(object sender, MouseEventArgs e)
    {
        var pt = e.GetPosition(ScreenshotViewerContainer);
        _viewModel.UpdateHoverCoordinates(
            pt.X,
            pt.Y,
            ScreenshotViewerContainer.ActualWidth,
            ScreenshotViewerContainer.ActualHeight);
    }

    private void OnScreenshotImageMouseDown(object sender, MouseButtonEventArgs e)
    {
        var pt = e.GetPosition(ScreenshotViewerContainer);
        _ = _viewModel.ClickOnScreenshotPreviewAsync(
            pt.X,
            pt.Y,
            ScreenshotViewerContainer.ActualWidth,
            ScreenshotViewerContainer.ActualHeight);
    }

    public void EnsureVisibleAndActive()
    {
        try
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            var workArea = SystemParameters.WorkArea;
            if (Width > workArea.Width)
            {
                Width = Math.Max(MinWidth, workArea.Width - 40);
            }

            if (Height > workArea.Height)
            {
                Height = Math.Max(MinHeight, workArea.Height - 40);
            }

            if (double.IsNaN(Left) || Left < workArea.Left || Left + Width > workArea.Right)
            {
                Left = Math.Max(workArea.Left, Math.Min(Left, workArea.Right - Width));
                if (double.IsNaN(Left))
                {
                    Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
                }
            }

            if (double.IsNaN(Top) || Top < workArea.Top || Top + Height > workArea.Bottom)
            {
                Top = Math.Max(workArea.Top, Math.Min(Top, workArea.Bottom - Height));
                if (double.IsNaN(Top))
                {
                    Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
                }
            }

            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.EnsureHandle();

            Win32Native.ShowWindow(hwnd, Win32Native.SW_SHOWNORMAL);
            Win32Native.ShowWindow(hwnd, Win32Native.SW_RESTORE);

            Win32Native.SetWindowPos(
                hwnd,
                Win32Native.HWND_TOP,
                0,
                0,
                0,
                0,
                Win32Native.SWP_NOMOVE | Win32Native.SWP_NOSIZE | Win32Native.SWP_SHOWWINDOW | Win32Native.SWP_FRAMECHANGED);

            Visibility = Visibility.Visible;
            Show();
            Activate();

            Win32Native.SetForegroundWindow(hwnd);

            bool isVisible = Win32Native.IsWindowVisible(hwnd);
            Win32Native.GetWindowRect(hwnd, out var rect);
            var dpi = VisualTreeHelper.GetDpi(this);
            int physicalW = rect.Right - rect.Left;
            int physicalH = rect.Bottom - rect.Top;

            StudioLogger.Info($"EnsureVisibleAndActive executed. HWND: {hwnd}, IsVisible: {isVisible}, PhysicalRect: [{rect.Left},{rect.Top},{rect.Right},{rect.Bottom}] ({physicalW}x{physicalH} px), Dips: {Width}x{Height}, DpiScale: {dpi.PixelsPerDip:F2}, WindowState: {WindowState}, Visibility: {Visibility}");
        }
        catch (Exception ex)
        {
            StudioLogger.Error($"EnsureVisibleAndActive encountered an error: {ex.Message}", ex);
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _ = _viewModel.StopTargetAsync();
    }
}
