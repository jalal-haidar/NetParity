Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

# Ensure we have the necessary classes
$window = New-Object Windows.Window
$window.Title = "SpeedTracker"
$window.Height = 100
$window.Width = 220
$window.WindowStyle = [Windows.WindowStyle]::None
$window.AllowsTransparency = $true
$window.Background = [Windows.Media.Brushes]::Transparent
$window.Topmost = $true
$window.ShowInTaskbar = $false
$window.WindowStartupLocation = [Windows.WindowStartupLocation]::Manual
$window.Left = 10
$window.Top = 10

# UI Setup
$border = New-Object Windows.Controls.Border
$border.CornerRadius = New-Object Windows.CornerRadius(12)
$border.BorderThickness = New-Object Windows.Thickness(1.5)
$border.Margin = New-Object Windows.Thickness(5)
$border.Background = [Windows.Media.BrushConverter]::new().ConvertFromString("#E61A1A1A")
$border.BorderBrush = [Windows.Media.BrushConverter]::new().ConvertFromString("#33FFFFFF")

$grid = New-Object Windows.Controls.Grid
$grid.Margin = New-Object Windows.Thickness(10,5,10,5)
[void]$grid.RowDefinitions.Add((New-Object Windows.Controls.RowDefinition))
[void]$grid.RowDefinitions.Add((New-Object Windows.Controls.RowDefinition))
[void]$grid.ColumnDefinitions.Add((New-Object Windows.Controls.ColumnDefinition))
[void]$grid.ColumnDefinitions.Add((New-Object Windows.Controls.ColumnDefinition))

function New-MetricLabel($label, $color, $row, $col, $isRight = $false) {
    $sp = New-Object Windows.Controls.StackPanel
    $sp.Orientation = [Windows.Controls.Orientation]::Horizontal
    $sp.VerticalAlignment = [Windows.VerticalAlignment]::Center
    if ($isRight) { $sp.HorizontalAlignment = [Windows.HorizontalAlignment]::Right }
    [Windows.Controls.Grid]::SetRow($sp, $row)
    [Windows.Controls.Grid]::SetColumn($sp, $col)

    $icon = New-Object Windows.Controls.TextBlock
    $icon.Text = $label
    $icon.Foreground = [Windows.Media.BrushConverter]::new().ConvertFromString($color)
    $icon.FontSize = 14
    $icon.FontWeight = [Windows.FontWeights]::Bold
    $icon.Margin = New-Object Windows.Thickness(0,0,5,0)
    
    $val = New-Object Windows.Controls.TextBlock
    $val.Text = "0"
    $val.Foreground = [Windows.Media.Brushes]::White
    $val.FontSize = 14
    $val.FontFamily = New-Object Windows.Media.FontFamily("Segoe UI Semibold")

    $unit = New-Object Windows.Controls.TextBlock
    $unit.Text = if ($label -match "^D$|^U$") { " Mbps" } else { " %" }
    $unit.Foreground = [Windows.Media.BrushConverter]::new().ConvertFromString("#88FFFFFF")
    $unit.FontSize = 10
    $unit.VerticalAlignment = [Windows.VerticalAlignment]::Bottom
    $unit.Margin = New-Object Windows.Thickness(2,0,0,2)

    [void]$sp.Children.Add($icon)
    [void]$sp.Children.Add($val)
    [void]$sp.Children.Add($unit)
    [void]$grid.Children.Add($sp)
    return [PSCustomObject]@{ Value = $val; Unit = $unit }
}

$txtDown = New-MetricLabel "D" "#00F2FF" 0 0
$txtUp   = New-MetricLabel "U" "#FF00E5" 1 0
$txtCPU  = New-MetricLabel "CPU" "#00FF88" 0 1 $true
$txtRAM  = New-MetricLabel "RAM" "#FFCC00" 1 1 $true

$border.Child = $grid
$window.Content = $border

$window.Add_MouseLeftButtonDown({ $window.DragMove() })
$window.Add_MouseRightButtonDown({
    $menu = New-Object Windows.Controls.ContextMenu
    $item = New-Object Windows.Controls.MenuItem
    $item.Header = "Exit SpeedTracker"
    $item.Add_Click({ $window.Close() })
    [void]$menu.Items.Add($item)

    $sep = New-Object Windows.Controls.Separator
    [void]$menu.Items.Add($sep)

    $regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    $regName = "NetParity"
    $exePath = "d:\workspace\internetspeedtracker\StartTracker.bat"
    
    $startItem = New-Object Windows.Controls.MenuItem
    $startItem.Header = "Run at Startup"
    $startItem.IsCheckable = $true
    $startItem.IsChecked = (Get-ItemProperty $regPath -Name $regName -ErrorAction SilentlyContinue) -ne $null
    
    $startItem.Add_Click({
        if ($startItem.IsChecked) {
            Set-ItemProperty -Path $regPath -Name $regName -Value "`"$exePath`""
        } else {
            Remove-ItemProperty -Path $regPath -Name $regName -ErrorAction SilentlyContinue
        }
    })
    [void]$menu.Items.Add($startItem)

    $menu.IsOpen = $true
})

# --- Parity Logic ---

$cpuCounter = New-Object System.Diagnostics.PerformanceCounter("Processor Information", "% Processor Utility", "_Total")
$null = $cpuCounter.NextValue()

# Network Tracking - High Precision .NET Implementation
$script:OldBytes = @(0, 0) # [Recv, Sent]
$sw = [System.Diagnostics.Stopwatch]::StartNew()

function Get-GlobalNetworkBytes {
    $totalRecv = 0; $totalSent = 0
    foreach ($ni in [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()) {
        if ($ni.OperationalStatus -eq "Up" -and $ni.NetworkInterfaceType -ne "Loopback") {
            try {
                $stats = $ni.GetIPStatistics()
                $totalRecv += $stats.BytesReceived
                $totalSent += $stats.BytesSent
            } catch {}
        }
    }
    return @($totalRecv, $totalSent)
}

# Initial seed
$script:OldBytes = Get-GlobalNetworkBytes

# Stats tracking for smoothing
$cpuHistory = @()

$timer = New-Object System.Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromSeconds(1)
$timer.Add_Tick({
    try {
        # Network - High Precision Delta Calculation
        $elapsed = $sw.Elapsed.TotalSeconds
        $sw.Restart()
        
        $newBytes = Get-GlobalNetworkBytes
        
        if ($script:OldBytes[0] -gt 0) {
            # Calculate Delta in Bits per second (match Task Manager)
            # (New - Old) * 8 bits / 1MB / seconds
            $d = (($newBytes[0] - $script:OldBytes[0]) * 8 / 1MB) / $elapsed
            $u = (($newBytes[1] - $script:OldBytes[1]) * 8 / 1MB) / $elapsed
            
            # Dynamic Scaling for Download (Bits)
            if ($d -lt 1.0 -and $d -gt 0) {
                $txtDown.Value.Text = "{0:N0}" -f ($d * 1024)
                $txtDown.Unit.Text = " Kbps"
            } else {
                $txtDown.Value.Text = "{0:N2}" -f [Math]::Max(0, $d)
                $txtDown.Unit.Text = " Mbps"
            }

            # Dynamic Scaling for Upload (Bits)
            if ($u -lt 1.0 -and $u -gt 0) {
                $txtUp.Value.Text = "{0:N0}" -f ($u * 1024)
                $txtUp.Unit.Text = " Kbps"
            } else {
                $txtUp.Value.Text = "{0:N2}" -f [Math]::Max(0, $u)
                $txtUp.Unit.Text = " Mbps"
            }
        }
        $script:OldBytes = $newBytes

        # CPU - Capped at 100 for Processes Tab parity
        $currentCPU = $cpuCounter.NextValue()
        if ($currentCPU -gt 100) { $currentCPU = 100 }
        
        # Simple smoothing to match Task Manager visual flow
        $cpuHistory += $currentCPU
        if ($cpuHistory.Count -gt 2) { $cpuHistory = $cpuHistory[-2..-1] } # Keep last 2
        
        $avgCPU = ($cpuHistory | Measure-Object -Average).Average
        
        # RAM
        $os = Get-CimInstance Win32_OperatingSystem
        $pct = (($os.TotalVisibleMemorySize - $os.FreePhysicalMemory) / $os.TotalVisibleMemorySize) * 100
        $txtRAM.Value.Text = [Math]::Round($pct)
        
        # CPU
        $v = [Math]::Round($avgCPU)
        $txtCPU.Value.Text = if ($v -gt 0) { $v } else { "0" }
        $txtCPU.Unit.Text = " %"
    } catch { }
})

$timer.Start()
[void]$window.ShowDialog()
