using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using NetParity.Core;
using NetParity.Core.Models;

namespace NetParity;

public partial class MainWindow : Window
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const int ToggleHotkeyId = 0xB1AD;

    private static readonly Brush Good = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88));
    private static readonly Brush Fair = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x00));
    private static readonly Brush Poor = new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D));
    private static readonly Brush Idle = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

    private readonly AppSettings _settings;
    private readonly LatencyHealth _health = new();
    private readonly System.Windows.Forms.NotifyIcon _tray;

    private MetricsService _metrics;
    private HwndSource? _hotkeySource;
    private bool _hotkeyRegistered;
    private bool _hotkeyWarningShown;

    public MainWindow()
    {
        InitializeComponent();

        _settings = SettingsStore.Load();
        ApplySettings();

        _metrics = new MetricsService(_settings.UnitMode, _settings.LatencyHost, _settings.ShowLatency);

        _tray = BuildTrayIcon();

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PlaceWindow();

        _metrics.MetricsUpdated += OnMetricsUpdated;
        _metrics.Start();

        RegisterHotKey();
        SyncMenuState();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        CapturePosition();
        SettingsStore.Save(_settings);

        _metrics.MetricsUpdated -= OnMetricsUpdated;
        _metrics.Dispose();

        UnregisterHotKey();

        _tray.Visible = false;
        _tray.Dispose();
    }

    /// <summary>
    /// The tray icon is the recovery path, not a convenience. Without it, a user who hides
    /// the overlay by double-clicking and then finds the hotkey is taken by another
    /// application has no way back to the window short of Task Manager.
    /// </summary>
    private System.Windows.Forms.NotifyIcon BuildTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show or hide", null, (_, _) => ToggleVisibility());
        menu.Items.Add("Reset position", null, (_, _) => Dispatcher.Invoke(ResetPosition));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Close());

        var icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "NetParity",
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };

        icon.DoubleClick += (_, _) => ToggleVisibility();
        return icon;
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = Array.Find(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith("NetParity.ico", StringComparison.OrdinalIgnoreCase));

        if (resource is not null)
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is not null)
            {
                return new System.Drawing.Icon(stream);
            }
        }

        return System.Drawing.SystemIcons.Application;
    }

    private void WarnHotkeyUnavailable()
    {
        if (_hotkeyWarningShown)
        {
            return;
        }

        _hotkeyWarningShown = true;

        _tray.ShowBalloonTip(
            5000,
            "NetParity",
            "Ctrl+Alt+N is already claimed by another application. Use the tray icon to hide or show NetParity.",
            System.Windows.Forms.ToolTipIcon.Warning);
    }

    private void PlaceWindow()
    {
        if (_settings.WindowLeft >= 0 && _settings.WindowTop >= 0)
        {
            Left = _settings.WindowLeft;
            Top = _settings.WindowTop;
            return;
        }

        Left = Math.Max(0, SystemParameters.WorkArea.Right - Width - 24);
        Top = 24;
    }

    private void CapturePosition()
    {
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
    }

    private void ApplySettings()
    {
        Opacity = Math.Clamp(_settings.Opacity, 0.3, 1.0);
    }

    private void OnMetricsUpdated(object? sender, SystemMetrics metrics)
    {
        // Invoke rather than InvokeAsync: posting faster than the dispatcher drains
        // would build a backlog that makes the numbers visibly lag behind reality.
        Dispatcher.Invoke(() =>
        {
            CpuValue.Text = Format.Percent(metrics.CpuPercent);
            RamValue.Text = Format.Percent(metrics.RamPercent);

            DownloadValue.Text = Format.Speed(metrics.Download.Value);
            DownloadUnit.Text = metrics.Download.Unit;
            UploadValue.Text = Format.Speed(metrics.Upload.Value);
            UploadUnit.Text = metrics.Upload.Unit;

            RenderLatency(metrics.Latency);
        });
    }

    private void RenderLatency(LatencyStats latency)
    {
        if (!_settings.ShowLatency)
        {
            PingValue.Text = "--";
            JitterValue.Text = "--";
            PingValue.Foreground = Idle;
            JitterValue.Foreground = Idle;
            return;
        }

        var ping = latency.RoundTripMs;
        var jitter = latency.JitterMs;
        var loss = latency.PacketLossPercent ?? 0;

        PingValue.Text = ping.HasValue ? Format.Milliseconds(ping.Value) : "--";
        JitterValue.Text = jitter.HasValue ? Format.Milliseconds(jitter.Value) : "--";

        var worst = _health.WorstOf(ping, jitter, loss);
        var brush = worst switch
        {
            LatencyQuality.Good => Good,
            LatencyQuality.Fair => Fair,
            _ => Poor
        };

        PingValue.Foreground = ping.HasValue ? brush : Idle;
        JitterValue.Foreground = jitter.HasValue ? brush : Idle;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleVisibility();
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove throws if the button was released early. Nothing to recover.
        }

        CapturePosition();
        SettingsStore.Save(_settings);
    }

    private void OnHoverStart(object sender, RoutedEventArgs e) => Root.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));

    private void OnHoverEnd(object sender, RoutedEventArgs e) => Root.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

    private void OnContextMenu(object sender, MouseButtonEventArgs e)
    {
        if (Menu.PlacementTarget is null)
        {
            Menu.PlacementTarget = Root;
        }

        Menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void SyncMenuState()
    {
        ShowLatencyItem.IsChecked = _settings.ShowLatency;
        StartupItem.IsChecked = IsStartupEnabled();

        UnitsAutoItem.IsChecked = _settings.UnitMode == SpeedUnit.Auto;
        UnitsBitsItem.IsChecked = _settings.UnitMode == SpeedUnit.Bits;
        UnitsBytesItem.IsChecked = _settings.UnitMode == SpeedUnit.Bytes;
    }

    private void OnToggleLatency(object sender, RoutedEventArgs e)
    {
        _settings.ShowLatency = ShowLatencyItem.IsChecked;
        SettingsStore.Save(_settings);

        // This now genuinely starts and stops probing. Previously it only flipped a
        // drawing flag, so leaving the overlay off still sent a packet per second to a
        // third party while claiming in the README that it had stopped.
        _metrics.LatencyEnabled = _settings.ShowLatency;
    }

    private void OnUnitsAuto(object sender, RoutedEventArgs e) => SetUnitMode(SpeedUnit.Auto);

    private void OnUnitsBits(object sender, RoutedEventArgs e) => SetUnitMode(SpeedUnit.Bits);

    private void OnUnitsBytes(object sender, RoutedEventArgs e) => SetUnitMode(SpeedUnit.Bytes);

    private void SetUnitMode(SpeedUnit mode)
    {
        _settings.UnitMode = mode;
        SettingsStore.Save(_settings);
        SyncMenuState();

        // Unit mode is a plain property now. It used to require disposing and rebuilding
        // the whole service from the UI thread to change three labels.
        _metrics.UnitMode = mode;
    }

    private void OnSetLatencyTarget(object sender, RoutedEventArgs e)
    {
        var input = new TextBox
        {
            Text = _settings.LatencyHost,
            Margin = new Thickness(8, 4, 8, 8)
        };

        var dialog = new Window
        {
            Title = "Ping target",
            Width = 300,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            Foreground = Brushes.White,
            ShowInTaskbar = false
        };

        var ok = new Button { Content = "Save", Width = 80, Margin = new Thickness(0, 0, 8, 12) };
        ok.Click += (_, _) => dialog.DialogResult = true;

        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = "Hostname or IP to measure round-trip time against:",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 4)
        });
        panel.Children.Add(input);
        panel.Children.Add(ok);

        dialog.Content = panel;
        dialog.Loaded += (_, _) => input.Focus();

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(input.Text))
        {
            _settings.LatencyHost = input.Text.Trim();
            SettingsStore.Save(_settings);
            _metrics.SetLatencyTarget(_settings.LatencyHost, _settings.LatencyPort);
        }
    }

    private void OnResetPosition(object sender, RoutedEventArgs e) => ResetPosition();

    private void ResetPosition()
    {
        _settings.WindowLeft = -1;
        _settings.WindowTop = -1;
        SettingsStore.Save(_settings);
        PlaceWindow();
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0";

        MessageBox.Show(
            this,
            $"NetParity {version}\n\n" +
            "CPU, RAM and network figures are computed on the same basis Task Manager uses.\n" +
            "Ping and jitter are measured live and fall back to a TCP handshake when ICMP is filtered.\n\n" +
            "Everything runs locally. No data leaves this machine.",
            "About NetParity",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void OnToggleStartup(object sender, RoutedEventArgs e)
    {
        SetStartupEnabled(StartupItem.IsChecked);
        _settings.RunAtStartup = StartupItem.IsChecked;
        SettingsStore.Save(_settings);
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue("NetParity") is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void SetStartupEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return;
            }

            if (enable)
            {
                var executable = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(executable))
                {
                    key.SetValue("NetParity", $"\"{executable}\"");
                }
            }
            else
            {
                key.DeleteValue("NetParity", throwOnMissingValue: false);
            }
        }
        catch (Exception)
        {
            // A locked-down registry key must not take the app down.
        }
    }

    private void RegisterHotKey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        // MOD_ALT | MOD_CONTROL, and MOD_NOREPEAT so holding the combo down does not
        // strobe the overlay.
        const uint modifiers = 0x0001 | 0x0002 | 0x4000;
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(Key.N);

        _hotkeySource = HwndSource.FromHwnd(handle);
        _hotkeySource?.AddHook(OnHotKey);
        _hotkeyRegistered = RegisterHotKey(handle, ToggleHotkeyId, modifiers, virtualKey);

        if (!_hotkeyRegistered)
        {
            // Another application already owns Ctrl+Alt+N, which is common. Say so rather
            // than failing silently and leaving the user unable to find the window.
            Dispatcher.BeginInvoke(WarnHotkeyUnavailable);
        }
    }

    private void UnregisterHotKey()
    {
        var handle = new WindowInteropHelper(this).Handle;

        if (_hotkeyRegistered)
        {
            UnregisterHotKey(handle, ToggleHotkeyId);
            _hotkeyRegistered = false;
        }

        _hotkeySource?.RemoveHook(OnHotKey);
        _hotkeySource = null;
    }

    private IntPtr OnHotKey(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == ToggleHotkeyId)
        {
            ToggleVisibility();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ToggleVisibility() => Visibility = Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;

    private static class Format
    {
        public static string Percent(double value) =>
            double.IsFinite(value) ? value.ToString("F0") : "0";

        public static string Speed(double value) =>
            double.IsFinite(value) ? value.ToString("0.##") : "0";

        public static string Milliseconds(double value) =>
            double.IsFinite(value) ? value.ToString(value >= 100 ? "F0" : "F1") : "--";
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

internal enum LatencyQuality
{
    Good,
    Fair,
    Poor
}

/// <summary>
/// Grades a link from round-trip time, jitter and loss.
/// </summary>
/// <remarks>
/// Thresholds follow common game-networking guidance rather than raw ICMP limits,
/// because the question the user is asking is "will this feel laggy", not "is the
/// packet loss percentage inside RFC 2544".
/// </remarks>
internal sealed class LatencyHealth
{
    public LatencyQuality WorstOf(double? ping, double? jitter, double loss)
    {
        var quality = LatencyQuality.Good;

        if (ping is { } value)
        {
            if (value >= 150)
            {
                quality = LatencyQuality.Poor;
            }
            else if (value >= 60)
            {
                quality = LatencyQuality.Fair;
            }
        }

        if (jitter is { } variation)
        {
            if (variation >= 30)
            {
                quality = LatencyQuality.Poor;
            }
            else if (variation >= 10 && quality == LatencyQuality.Good)
            {
                quality = LatencyQuality.Fair;
            }
        }

        if (loss >= 5)
        {
            quality = LatencyQuality.Poor;
        }
        else if (loss >= 1 && quality == LatencyQuality.Good)
        {
            quality = LatencyQuality.Fair;
        }

        return quality;
    }
}
