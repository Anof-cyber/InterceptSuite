﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Windows.Interop;
using InterceptSuite.Models;
using InterceptSuite.Helpers;
using InterceptSuite.ViewModels;

namespace InterceptSuite;

public partial class MainWindow : Window, INotifyPropertyChanged, IDisposable
{
    // DLL imports for dark title bar
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // Collections
    private readonly ObservableCollection<ConnectionEvent> _connectionEvents = new();
    private readonly ObservableCollection<LogEvent> _historyEvents = new();
    private readonly List<string> _statusMessages = new();

    // Properties for data binding
    private int _activeConnections;
    public int ActiveConnections
    {
        get => _activeConnections;
        set { _activeConnections = value; OnPropertyChanged(nameof(ActiveConnections)); }
    }

    private int _totalConnections;
    public int TotalConnections
    {
        get => _totalConnections;
        set { _totalConnections = value; OnPropertyChanged(nameof(TotalConnections)); }
    }

    private int _bytesSent;
    public int BytesSent
    {
        get => _bytesSent;
        set { _bytesSent = value; OnPropertyChanged(nameof(BytesSent)); }
    }

    private int _bytesReceived;
    public int BytesReceived
    {
        get => _bytesReceived;
        set { _bytesReceived = value; OnPropertyChanged(nameof(BytesReceived)); }
    }    // DLL interaction
    private bool _proxyRunning = false;
    private DllManager? _dllManager = null;

    // Timer for UI updates
    private DispatcherTimer? _updateTimer = null;

    // Interception state
    private bool _isInterceptionEnabled = false;
    private int _interceptDirection = 0; // 0=None, 1=Client->Server, 2=Server->Client, 3=Both
    private int _currentInterceptConnectionId = -1;
    private string _currentInterceptDirection = "";
    private string _currentInterceptSrcIp = "";
    private string _currentInterceptDstIp = "";
    private int _currentInterceptDstPort = 0;
    private byte[] _currentInterceptData = Array.Empty<byte>();
    private bool _isWaitingForInterceptResponse = false;
    private bool _isInterceptDataModified = false;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public MainWindow()
    {
        InitializeComponent();

        // Apply dark title bar when window loads
        Loaded += MainWindow_Loaded;        // Initialize DLL manager with callbacks
        _dllManager = new DllManager(
            ProxyDataCallback, // was LogCallback
            StatusCallback,
            ConnectionCallback,
            StatsCallback,
            DisconnectCallback,
            InterceptCallback
        );

        // Initialize ListViews
        ConnectionsList.ItemsSource = _connectionEvents;
        HistoryList.ItemsSource = _historyEvents;        // Initialize timer for UI updates
        _updateTimer = new DispatcherTimer();
        _updateTimer.Interval = TimeSpan.FromMilliseconds(100);
        _updateTimer.Tick += UpdateTimer_Tick;
        _updateTimer.Start();        // Initial navigation selection
        InterceptButton.IsEnabled = false;  // Mark as selected


        _ = LoadDllAndInitializeAsync();
    }    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        // Update current time in status bar
        CurrentTimeText.Text = DateTime.Now.ToString("HH:mm:ss");

        if (_dllManager != null && _dllManager.IsLoaded && _proxyRunning)
        {
            int connections = 0;
            int bytes = 0;
            if (_dllManager.GetProxyStats(ref connections, ref bytes))
            {
                TotalConnections = connections;
                ActiveConnections = connections; // In a real app, these might be different
                BytesSent = bytes / 2;
                BytesReceived = bytes / 2;

                // Update UI elements
                ActiveConnectionsText.Text = ActiveConnections.ToString();
                TotalConnectionsText.Text = TotalConnections.ToString();
                BytesSentText.Text = BytesSent.ToString();
                BytesReceivedText.Text = BytesReceived.ToString();
            }
        }
    }private async Task LoadDllAsync()
    {
        if (_dllManager == null)
        {
            Dispatcher.Invoke(() =>
                AddStatusMessage("[ERROR] DLL Manager not initialized")
            );
            return;
        }

        var result = await _dllManager.LoadDllAsync();

        Dispatcher.Invoke(() =>
        {
            if (result.success)
            {
                DllStatusText.Text = "DLL: Loaded";
                DllStatusText.Foreground = System.Windows.Media.Brushes.Green;
                AddStatusMessage("[SYSTEM] DLL loaded successfully");
            }
            else
            {
                AddStatusMessage($"[ERROR] {result.message}");
            }
        });
    }private async Task LoadDllAndInitializeAsync()
    {
        if (_dllManager == null)
        {
            AddStatusMessage("[ERROR] DLL Manager not initialized");
            return;
        }

        // Load the DLL first
        var result = await _dllManager.LoadDllAsync();

        Dispatcher.Invoke(() =>
        {
            if (result.success)
            {                DllStatusText.Text = "DLL: Loaded";
                DllStatusText.Foreground = System.Windows.Media.Brushes.Green;
                AddStatusMessage("[SYSTEM] DLL loaded successfully");

                RefreshNetworkInterfaces();

                LoadProxyConfigFromDll();
            }
            else
            {
                DllStatusText.Text = "DLL: Failed to load";
                DllStatusText.Foreground = System.Windows.Media.Brushes.Red;
                AddStatusMessage($"[ERROR] {result.message}");


                RefreshNetworkInterfaces_Fallback();
            }
        });
    }    private void RefreshNetworkInterfaces()
    {
        if (_dllManager == null || !_dllManager.IsLoaded)
        {
            // Local method
            BindAddressComboBox.Items.Clear();
            try
            {

                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up)
                    {
                        foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                BindAddressComboBox.Items.Add(ip.Address.ToString());
                            }
                        }
                    }
                }



                if (BindAddressComboBox.Items.Count > 0)
                {
                    BindAddressComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                AddStatusMessage($"[ERROR] Failed to enumerate network interfaces: {ex.Message}");

                // Add loopback as fallback
                BindAddressComboBox.Items.Clear();
                BindAddressComboBox.Items.Add("127.0.0.1");
                BindAddressComboBox.SelectedIndex = 0;
            }
            return;
        }        try
        {
            // Use DLL API
            StringBuilder buffer = new StringBuilder(2048);
            int result = _dllManager.GetSystemIps(buffer, buffer.Capacity);

            if (result > 0)
            {
                string ipList = buffer.ToString();
                AddStatusMessage($"[DEBUG] Raw IP list from DLL: '{ipList}'");

                // Handle both comma and semicolon separators
                string[] ipAddresses = ipList.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

                BindAddressComboBox.Items.Clear();
                foreach (string ip in ipAddresses)
                {
                    if (!string.IsNullOrWhiteSpace(ip))
                    {
                        string trimmedIp = ip.Trim();
                        BindAddressComboBox.Items.Add(trimmedIp);
                        AddStatusMessage($"[DEBUG] Added IP: '{trimmedIp}'");
                    }
                }

                if (BindAddressComboBox.Items.Count > 0)
                    BindAddressComboBox.SelectedIndex = 0;

                AddStatusMessage($"[SYSTEM] Found {BindAddressComboBox.Items.Count} network interfaces");
            }
            else
            {
                AddStatusMessage("[ERROR] Failed to get network interfaces from DLL");
                RefreshNetworkInterfaces_Fallback();
            }
        }
        catch (Exception ex)
        {
            AddStatusMessage($"[ERROR] Exception getting network interfaces: {ex.Message}");
            RefreshNetworkInterfaces_Fallback();
        }
    }

    private void RefreshNetworkInterfaces_Fallback()
    {
        // Fallback method using .NET APIs
        BindAddressComboBox.Items.Clear();
        BindAddressComboBox.Items.Add("127.0.0.1");

        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation ip in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            BindAddressComboBox.Items.Add(ip.Address.ToString());
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // Just use loopback in case of error
        }        BindAddressComboBox.SelectedIndex = 0;
    }    // Rename LogCallback to ProxyDataCallback
    private void ProxyDataCallback(string timestamp, string src_ip, string dst_ip, int dst_port,
                            string message_type, string data)
    {
        // Validate and sanitize inputs using helper
        timestamp = DataHelper.ValidateString(timestamp, DateTime.Now.ToString("HH:mm:ss"));
        src_ip = DataHelper.ValidateString(src_ip, "unknown");
        dst_ip = DataHelper.ValidateString(dst_ip, "unknown");
        message_type = DataHelper.ValidateString(message_type, "Unknown");
        data = DataHelper.ValidateString(data, "");

        // Need to dispatch to UI thread
        UIHelper.SafeInvoke(Dispatcher, () =>
        {
            try
            {
                var logEvent = new LogEvent
                {
                    Timestamp = timestamp,
                    SourceIp = src_ip,
                    DestinationIp = dst_ip,
                    Port = dst_port,
                    Type = message_type,
                    Data = data,
                    OriginalData = data,  // Initialize OriginalData with the same data
                    WasModified = false   // Initially, the data is not modified
                };

                _historyEvents.Add(logEvent);

                // Trim history if too large
                if (_historyEvents.Count > 1000)
                    _historyEvents.RemoveAt(0);

                // Auto-scroll the history list
                UIHelper.AutoScrollToEnd(HistoryList);
            }
            catch (Exception ex)
            {
                AddStatusMessage($"Error in ProxyDataCallback: {ex.Message}");
            }
        });
    }    private void StatusCallback(string message)
    {
        // Need to dispatch to UI thread
        UIHelper.SafeInvoke(Dispatcher, () => AddStatusMessage(message));
    }

    private void ConnectionCallback(string client_ip, int client_port, string target_host,
                                   int target_port, int connection_id)
    {
        UIHelper.SafeInvoke(Dispatcher, () =>
        {
            var connectionEvent = new ConnectionEvent
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                Event = "Connect",
                ConnectionId = connection_id,
                SourceIp = client_ip,
                SourcePort = client_port,
                DestinationIp = target_host,
                DestinationPort = target_port
            };

            _connectionEvents.Add(connectionEvent);

            // Increment active connections
            ActiveConnections++;

            // Trim history if too large
            if (_connectionEvents.Count > 1000)
                _connectionEvents.RemoveAt(0);

            // Auto-scroll if enabled
            UIHelper.AutoScrollToEnd(ConnectionsList);
        });
    }

    private void StatsCallback(int total_connections, int active_connections, int total_bytes_transferred)
    {
        // Need to dispatch to UI thread
        Dispatcher.Invoke(() =>
        {
            TotalConnections = total_connections;
            ActiveConnections = active_connections;
            BytesSent = total_bytes_transferred / 2;
            BytesReceived = total_bytes_transferred / 2;

            // Update UI elements
            ActiveConnectionsText.Text = ActiveConnections.ToString();
            TotalConnectionsText.Text = TotalConnections.ToString();
            BytesSentText.Text = BytesSent.ToString();
            BytesReceivedText.Text = BytesReceived.ToString();
        });
    }    private void DisconnectCallback(int connection_id, string reason)
    {
        // Need to dispatch to UI thread
        UIHelper.SafeInvoke(Dispatcher, () =>
        {
            var connectionEvent = new ConnectionEvent
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                Event = "Disconnect",
                ConnectionId = connection_id,
                SourceIp = "",
                SourcePort = 0,
                DestinationIp = reason,
                DestinationPort = 0
            };

            _connectionEvents.Add(connectionEvent);

            // Decrement active connections if > 0
            if (ActiveConnections > 0)
                ActiveConnections--;

            // Trim history if too large
            if (_connectionEvents.Count > 1000)
                _connectionEvents.RemoveAt(0);

            // Auto-scroll if enabled
            UIHelper.AutoScrollToEnd(ConnectionsList);
        });
    }private void AddStatusMessage(string message)
    {
        _statusMessages.Add(message);

        // Append message to the status bar
        StatusText.Text = message;

        // Trim status messages if too many
        if (_statusMessages.Count > 1000)
            _statusMessages.RemoveAt(0);
    }    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag)
        {
            NavigateToPanel(tag);
        }
    }    private void NavigateToPanel(string panelName)
    {
        var allPanels = new FrameworkElement[] { SettingsPanel, InterceptPanel, ConnectionsPanel, ProxyHistoryPanel };
        var allButtons = new Button[] { SettingsButton, InterceptButton, ConnectionsButton, ProxyHistoryButton };

        var (targetPanel, targetButton) = panelName switch
        {
            "Settings" => (SettingsPanel, SettingsButton),
            "Intercept" => (InterceptPanel, InterceptButton),
            "Connections" => (ConnectionsPanel, ConnectionsButton),
            "ProxyHistory" => (ProxyHistoryPanel, ProxyHistoryButton),
            _ => (InterceptPanel, InterceptButton) // Default fallback
        };

        UIHelper.NavigateToPanel(targetPanel, targetButton, allPanels, allButtons);
    }

    private void StartProxy_Click(object sender, RoutedEventArgs e)
    {
        // Call our enhanced version with better diagnostics and SOCKS5 configuration hints
        EnhancedStartProxy();
    }    private void StopProxy_Click(object sender, RoutedEventArgs e)
    {
        if (_dllManager == null || !_dllManager.IsLoaded || !_dllManager.IsProxyRunning)
            return;

        _dllManager.StopProxy();
        _proxyRunning = false;
        StatusText.Text = "Stopped";
        StatusText.Foreground = System.Windows.Media.Brushes.Red;
        StartProxyButton.IsEnabled = true;
        StopProxyButton.IsEnabled = false;

        AddStatusMessage("[SYSTEM] Proxy stopped");
    }

    // RefreshInterfaces_Click is now implemented in MainWindowPatch.cs

    private void ApplyConfig_Click(object sender, RoutedEventArgs e)
    {
        if (_dllManager == null || !_dllManager.IsLoaded)
        {
            MessageBox.Show("DLL not loaded", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }        try
        {            string bindAddr = BindAddressComboBox.SelectedItem?.ToString() ?? "127.0.0.1";
            int port = int.Parse(PortTextBox.Text);
            string logFile = LogFileTextBox.Text;
            bool verboseMode = VerboseModeCheckBox.IsChecked ?? false;            // Now we can pass the verbose mode directly to the DLL
            if (_dllManager.SetConfig(bindAddr, port, logFile, verboseMode))
            {
                AddStatusMessage($"[CONFIG] Configuration applied: {bindAddr}:{port}");
                AddStatusMessage($"[CONFIG] Log file: {logFile}, Verbose mode: {(verboseMode ? "On" : "Off")}");
            }
            else
            {
                MessageBox.Show("Failed to apply configuration", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }    }    private void BrowseLogFile_Click(object sender, RoutedEventArgs e)
    {
        var selectedFile = FileHelper.BrowseForLogFile(LogFileTextBox.Text);
        if (selectedFile != null)
        {
            LogFileTextBox.Text = selectedFile;
        }
    }

    private void ClearConnections_Click(object sender, RoutedEventArgs e)
    {
        _connectionEvents.Clear();
        AddStatusMessage("[SYSTEM] Connection history cleared");
    }    private void ExportConnections_Click(object sender, RoutedEventArgs e)
    {
        FileHelper.ExportToCsv(
            _connectionEvents,
            "Timestamp,Event,ConnectionID,SourceIP,SourcePort,DestinationIP,DestinationPort",
            evt => $"{evt.Timestamp},{evt.Event},{evt.ConnectionId},{evt.SourceIp},{evt.SourcePort},{evt.DestinationIp},{evt.DestinationPort}",
            FileHelper.GenerateTimestampedFilename("tls_proxy_connections", ".csv"),
            AddStatusMessage);
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _historyEvents.Clear();
        AddStatusMessage("[SYSTEM] Proxy history cleared");
    }    private void ExportHistory_Click(object sender, RoutedEventArgs e)
    {
        FileHelper.ExportToCsv(
            _historyEvents,
            "Timestamp,SourceIP,DestinationIP,Port,Type,Modified,Data",
            evt => $"{evt.Timestamp},{evt.SourceIp},{evt.DestinationIp},{evt.Port},{evt.Type},{evt.ModifiedIndicator},\"{evt.Data.Replace("\"", "\"\"")}\"",
            FileHelper.GenerateTimestampedFilename("tls_proxy_history", ".csv"),
            AddStatusMessage);
    }

    // IDisposable implementation
    private bool _disposed = false;    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources
                _updateTimer?.Stop();
                _updateTimer = null;

                // Clean up the DLL manager - it will handle stopping the proxy safely
                if (_dllManager != null)
                {
                    _dllManager.Dispose();
                    _dllManager = null;
                    _proxyRunning = false; // Update our local state
                }

                // Clear collections
                _connectionEvents.Clear();
                _historyEvents.Clear();
                _statusMessages.Clear();
            }

            // Free unmanaged resources and set large fields to null
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~MainWindow()
    {
        Dispose(false);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Make sure to stop the proxy and dispose resources when the window closes
        Dispose();
        base.OnClosing(e);    }

    // Apply dark title bar to the window
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Apply dark title bar
        ApplyDarkTitleBar();

        // Update window chrome
        UpdateWindowChrome();

        // Initialize intercept UI
        InitializeInterceptUI();
    }

    // Apply dark title bar to the window
    private void ApplyDarkTitleBar()
    {
        try
        {
            // Get the window handle
            IntPtr hwnd = new WindowInteropHelper(this).Handle;

            if (hwnd != IntPtr.Zero)
            {
                // Check if we're on Windows 10/11
                if (Environment.OSVersion.Version.Major >= 10)
                {
                    // DWMWA_USE_IMMERSIVE_DARK_MODE = 20
                    int attribute = 20;
                    int value = 1; // 1 = dark mode
                    DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
                }
            }
        }
        catch (Exception ex)
        {
            // Log the exception or ignore
            Console.WriteLine($"Error setting dark title bar: {ex.Message}");
        }
    }

    // Update window chrome for a better look
    private void UpdateWindowChrome()
    {
        // You could add additional window chrome customizations here if needed
        // like setting custom margins, etc.

        // Force a visual refresh
        InvalidateVisual();
    }

    private void LoadProxyConfigFromDll()
    {
        if (_dllManager == null || !_dllManager.IsLoaded)
        {
            return;
        }

        try
        {
            StringBuilder bindAddrBuffer = new StringBuilder(256);
            StringBuilder logFileBuffer = new StringBuilder(1024);
            int port = 0;
            int verboseMode = 0;

            if (_dllManager.GetProxyConfig(bindAddrBuffer, ref port, logFileBuffer, ref verboseMode))
            {
                // Update UI elements with current config
                string bindAddr = bindAddrBuffer.ToString();
                string logFile = logFileBuffer.ToString();

                // Find and select the bind address if it exists in the dropdown
                int index = BindAddressComboBox.Items.IndexOf(bindAddr);
                if (index >= 0)
                {
                    BindAddressComboBox.SelectedIndex = index;
                }
                else if (BindAddressComboBox.Items.Count > 0)
                {
                    BindAddressComboBox.SelectedIndex = 0;
                }

                // Set port and log file
                PortTextBox.Text = port.ToString();
                LogFileTextBox.Text = logFile;

                // Set verbose mode checkbox
                VerboseModeCheckBox.IsChecked = verboseMode != 0;

                AddStatusMessage($"[CONFIG] Loaded configuration from DLL: {bindAddr}:{port}");
                AddStatusMessage($"[CONFIG] Log file: {logFile}, Verbose mode: {(verboseMode != 0 ? "On" : "Off")}");
            }
        }
        catch (Exception ex)        {
            AddStatusMessage($"[ERROR] Failed to load configuration: {ex.Message}");
        }
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is LogEvent selectedItem)
        {
            string displayText;

            // Format the display based on the message type
            if (selectedItem.Type == "Binary")
            {
                displayText = $"{selectedItem.Data}";
            }
            else if (selectedItem.Type == "Text")
            {
                displayText = $"{selectedItem.Data}";
            }
            else if (selectedItem.Type == "Empty")
            {
                displayText = "[Empty Data]";
            }
            else
            {
                string dataInfo = $"[{selectedItem.Type} - {GetDataSizeDescription(selectedItem.Data)}]";
                displayText = $"{dataInfo}\n{selectedItem.Data}";
            }

            // Check if this message was modified
            if (selectedItem.WasModified && !string.IsNullOrEmpty(selectedItem.OriginalData))
            {
                // Show both original and modified data in split view
                OriginalDataPanel.Visibility = Visibility.Visible;
                DataSplitter.Visibility = Visibility.Visible;

                // Configure grid column layout for split view
                Grid.SetColumn(ModifiedDataPanel, 2);

                // Update labels and content
                CurrentDataLabel.Text = "Modified Data";
                HistoryDataTextBox.Text = displayText;

                string originalDisplayText;

                // Format the original data display similarly
                if (selectedItem.Type == "Binary")
                {
                    originalDisplayText = $"{selectedItem.OriginalData}";
                }
                else if (selectedItem.Type == "Text")
                {
                    originalDisplayText = $"{selectedItem.OriginalData}";
                }
                else if (selectedItem.Type == "Empty")
                {
                    originalDisplayText = "[Empty Data]";
                }
                else
                {
                    string dataInfo = $"[{selectedItem.Type} - {GetDataSizeDescription(selectedItem.OriginalData)}]";
                    originalDisplayText = $"{dataInfo}\n{selectedItem.OriginalData}";
                }

                OriginalDataTextBox.Text = originalDisplayText;
            }
            else
            {                // Show only the current data (no split view)
                OriginalDataPanel.Visibility = Visibility.Collapsed;
                DataSplitter.Visibility = Visibility.Collapsed;

                // Configure grid for single view
                Grid.SetColumn(ModifiedDataPanel, 0);
                Grid.SetColumnSpan(ModifiedDataPanel, 3);

                // Update label and content
                CurrentDataLabel.Text = "Data";
                HistoryDataTextBox.Text = displayText;
            }
        }
        else
        {            // No selection, clear everything
            HistoryDataTextBox.Text = string.Empty;
            OriginalDataTextBox.Text = string.Empty;
            OriginalDataPanel.Visibility = Visibility.Collapsed;
            DataSplitter.Visibility = Visibility.Collapsed;
            Grid.SetColumn(ModifiedDataPanel, 0);
            Grid.SetColumnSpan(ModifiedDataPanel, 3);
            CurrentDataLabel.Text = "Data";
        }
    }    // Helper method to format data size information - now uses DataHelper
    private string GetDataSizeDescription(string data)
    {
        return DataHelper.GetDataSizeDescription(data);
    }private void InterceptCallback(int connectionId, string direction, string srcIp,
                                  string dstIp, int dstPort, byte[] data)
    {
        // Need to dispatch to UI thread
        Dispatcher.Invoke(() =>
        {
            // Clean up previous intercept data if it exists
            if (_currentInterceptData != null)
            {
                _currentInterceptData = null;
            }

            // Store current intercept data
            _currentInterceptConnectionId = connectionId;
            _currentInterceptDirection = direction;
            _currentInterceptSrcIp = srcIp;
            _currentInterceptDstIp = dstIp;
            _currentInterceptDstPort = dstPort;

            // Create a new copy of the data instead of using Clone()
            if (data != null && data.Length > 0)
            {
                _currentInterceptData = new byte[data.Length];
                Buffer.BlockCopy(data, 0, _currentInterceptData, 0, data.Length);
            }
            else
            {
                _currentInterceptData = new byte[0];
            }

            _isWaitingForInterceptResponse = true;
            _isInterceptDataModified = false;

            // Update UI
            UpdateInterceptUI();

            // Switch to intercept tab automatically
            NavigateToPanel("Intercept");
        });
    }    private void UpdateInterceptUI()
    {
        if (_isWaitingForInterceptResponse)
        {
            // Update status
            InterceptStatusText.Text = $"Intercepted data from connection {_currentInterceptConnectionId}";

            // Update connection info
            ConnectionIdText.Text = _currentInterceptConnectionId.ToString();
            DirectionText.Text = _currentInterceptDirection;
            EndpointText.Text = $"{_currentInterceptSrcIp} → {_currentInterceptDstIp}:{_currentInterceptDstPort}";

            // Update data view
            UpdateInterceptDataView();

            // Enable action buttons
            ForwardButton.IsEnabled = true;
            DropButton.IsEnabled = true;
            
            // Update Forward button text to indicate its dual functionality
            ForwardButton.Content = _isInterceptDataModified ? "Forward Modified" : "Forward";
        }
        else
        {
            // Reset UI
            InterceptStatusText.Text = "No intercept pending";
            ConnectionIdText.Text = "-";
            DirectionText.Text = "-";
            EndpointText.Text = "-";
            InterceptDataTextBox.Text = "";

            // Disable action buttons
            ForwardButton.IsEnabled = false;
            DropButton.IsEnabled = false;
            
            // Reset Forward button text
            ForwardButton.Content = "Forward";
        }
    }private void UpdateInterceptDataView()
    {
        if (_currentInterceptData == null || _currentInterceptData.Length == 0)
        {
            InterceptDataTextBox.Text = "";
            return;
        }

        if (TextViewRadio.IsChecked == true)
        {
            // Text view - use DataHelper for safe conversion
            try
            {
                InterceptDataTextBox.Text = DataHelper.SafeToText(_currentInterceptData);
            }
            catch (Exception ex)
            {
                // Fall back to hex if conversion fails
                InterceptDataTextBox.Text = DataHelper.ToHexString(_currentInterceptData);
                AddStatusMessage($"Warning: Failed to convert intercepted data to text: {ex.Message}");
            }
        }
        else if (HexViewRadio.IsChecked == true)
        {
            // Hex view - use DataHelper for consistent hex formatting
            InterceptDataTextBox.Text = DataHelper.ToHexString(_currentInterceptData);
        }
    }

    // Intercept event handlers
    private void InterceptEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_dllManager != null && CheckBox.ReferenceEquals(sender, InterceptEnabledCheckBox))
        {
            _isInterceptionEnabled = InterceptEnabledCheckBox.IsChecked == true;
            _dllManager.SetInterceptEnabled(_isInterceptionEnabled);

            if (_isInterceptionEnabled)
            {
                AddStatusMessage("Interception enabled");
            }
            else
            {
                AddStatusMessage("Interception disabled");
                // Clear any pending intercept
                if (_isWaitingForInterceptResponse)
                {
                    RespondToCurrentIntercept(0); // Forward
                }
            }
        }
    }    private void InterceptDirection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_dllManager != null && ComboBox.ReferenceEquals(sender, InterceptDirectionComboBox))
        {
            var selectedItem = InterceptDirectionComboBox.SelectedItem as ComboBoxItem;
            if (selectedItem?.Tag is string tagValue && int.TryParse(tagValue, out int direction))
            {
                _interceptDirection = direction;
                _dllManager.SetInterceptDirection(direction);

                // Use DataHelper for consistent direction formatting
                string directionText = DataHelper.FormatInterceptDirection(direction);
                AddStatusMessage($"Intercept direction set to: {directionText}");
            }
        }
    }    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        // Automatically determine the action based on whether data was modified
        int action = _isInterceptDataModified ? 2 : 0; // INTERCEPT_ACTION_MODIFY : INTERCEPT_ACTION_FORWARD
        RespondToCurrentIntercept(action);
    }

    private void Drop_Click(object sender, RoutedEventArgs e)
    {
        RespondToCurrentIntercept(1); // INTERCEPT_ACTION_DROP
    }

    private void ViewMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_isWaitingForInterceptResponse)
        {
            UpdateInterceptDataView();
        }
    }    private void InterceptDataTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        bool wasModified = _isInterceptDataModified;
        _isInterceptDataModified = true;
        
        // Update UI if the modification state changed (to update button text)
        if (!wasModified && _isWaitingForInterceptResponse)
        {
            UpdateInterceptUI();
        }
    }

    private void RespondToCurrentIntercept(int action)
    {
        if (!_isWaitingForInterceptResponse || _dllManager == null || _currentInterceptData == null)
            return;

        byte[]? modifiedData = null;
        string originalDataStr = null;
        string modifiedDataStr = null;

        try
        {
            // Convert the original intercept data to string for storage
            try
            {
                // Try UTF-8 first
                originalDataStr = System.Text.Encoding.UTF8.GetString(_currentInterceptData);

                // If it contains unprintable characters, use hex format instead
                if (originalDataStr.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
                {
                    originalDataStr = BitConverter.ToString(_currentInterceptData).Replace("-", " ");
                }
            }
            catch
            {
                // Fallback to hex if UTF-8 conversion fails
                originalDataStr = BitConverter.ToString(_currentInterceptData).Replace("-", " ");
            }

            if (action == 2 && _isInterceptDataModified) // INTERCEPT_ACTION_MODIFY
            {
                try
                {
                    // Store the modified data string for history
                    modifiedDataStr = InterceptDataTextBox.Text;

                    if (TextViewRadio.IsChecked == true)
                    {
                        // Convert text back to bytes
                        modifiedData = System.Text.Encoding.UTF8.GetBytes(InterceptDataTextBox.Text);
                    }
                    else if (HexViewRadio.IsChecked == true)
                    {
                        // Convert hex string back to bytes
                        string hexText = InterceptDataTextBox.Text.Replace(" ", "").Replace("-", "");
                        modifiedData = new byte[hexText.Length / 2];
                        for (int i = 0; i < modifiedData.Length; i++)
                        {
                            modifiedData[i] = Convert.ToByte(hexText.Substring(i * 2, 2), 16);
                        }
                    }

                    // Add to history with original and modified data
                    AddModifiedMessageToHistory(originalDataStr, modifiedDataStr);
                }
                catch (Exception ex)
                {
                    AddStatusMessage($"Error parsing modified data: {ex.Message}");
                    return;
                }
            }

            // Send the response to the DLL
            _dllManager.RespondToIntercept(_currentInterceptConnectionId, action, modifiedData);

            string actionText = action switch
            {
                0 => "forwarded",
                1 => "dropped",
                2 => "forwarded with modifications",
                _ => "processed"
            };
            AddStatusMessage($"Intercepted data {actionText}");
        }
        finally
        {
            // Clean up the intercept data - it's no longer needed
            _currentInterceptData = null;

            // Reset intercept state
            _isWaitingForInterceptResponse = false;
            _isInterceptDataModified = false;        UpdateInterceptUI();
        }
    }

    // Helper method to add a modified message to history
    private void AddModifiedMessageToHistory(string originalData, string modifiedData)
    {
        try
        {
            // Validate inputs using DataHelper
            originalData = DataHelper.ValidateString(originalData, "[Empty]");
            modifiedData = DataHelper.ValidateString(modifiedData, "[Empty]");

            // Use DataHelper to determine message type
            string messageType = DataHelper.GetMessageType(_currentInterceptDirection);

            var logEvent = new LogEvent
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                SourceIp = DataHelper.ValidateString(_currentInterceptSrcIp, "unknown"),
                DestinationIp = DataHelper.ValidateString(_currentInterceptDstIp, "unknown"),
                Port = _currentInterceptDstPort,
                Type = messageType,
                OriginalData = originalData,
                Data = modifiedData,
                WasModified = true
            };

            // Add to history list
            _historyEvents.Add(logEvent);

            // Trim history if too large
            if (_historyEvents.Count > 1000)
                _historyEvents.RemoveAt(0);

            // Auto-scroll using UIHelper
            UIHelper.AutoScrollToEnd(HistoryList);
        }
        catch (Exception ex)
        {
            AddStatusMessage($"Error adding modified message to history: {ex.Message}");
        }
    }

    private void InitializeInterceptUI()
    {
        // Set default values
        InterceptDirectionComboBox.SelectedIndex = 0; // None
        TextViewRadio.IsChecked = true;

        // Initialize intercept state
        UpdateInterceptUI();
    }
}
