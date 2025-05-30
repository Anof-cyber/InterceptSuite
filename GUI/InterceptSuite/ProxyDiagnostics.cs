using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows;

namespace InterceptSuite
{
    public partial class MainWindow
    {        // Enhanced method to provide better diagnostics during proxy startup
        private void EnhancedStartProxy()
        {
            if (_dllManager == null || !_dllManager.IsLoaded)
            {
                MessageBox.Show("DLL not loaded", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Add diagnostics before starting
            DiagnosticReport();

            if (_dllManager.StartProxy())
            {
                _proxyRunning = true;
                StatusText.Text = "Running";
                StatusText.Foreground = System.Windows.Media.Brushes.Green;
                StartProxyButton.IsEnabled = false;
                StopProxyButton.IsEnabled = true;

                // Show SOCKS5 configuration instructions
                AddStatusMessage("[SOCKS5] Proxy started successfully. Configure browser to use SOCKS5 proxy:");
                AddStatusMessage("[SOCKS5] - Host: " + (BindAddressComboBox.SelectedItem?.ToString() ?? "127.0.0.1"));
                AddStatusMessage("[SOCKS5] - Port: " + PortTextBox.Text);
                AddStatusMessage("[SOCKS5] - Type: SOCKS5");
            }
            else
            {
                MessageBox.Show("Failed to start proxy", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }        // Generate a diagnostic report
        private void DiagnosticReport()
        {
            string bindAddr = BindAddressComboBox.SelectedItem?.ToString() ?? "127.0.0.1";
            int port;
            if (!int.TryParse(PortTextBox.Text, out port))
                port = 4444; // Default

            // Create a StringBuilder for our report
            StringBuilder report = new StringBuilder();
            report.AppendLine("[DIAGNOSTIC] Generating proxy diagnostic report:");
            report.AppendLine("[DIAGNOSTIC] Available network interfaces:");
            int count = 0;
            foreach (var item in BindAddressComboBox.Items)
            {
                report.AppendLine($"[DIAGNOSTIC] - {++count}: {item}");
            }

            // Selected binding address
            report.AppendLine($"[DIAGNOSTIC] Selected binding address: {BindAddressComboBox.SelectedItem}");
            report.AppendLine($"[DIAGNOSTIC] Configured port: {PortTextBox.Text}");

            try
            {
                // Check port availability (already parsed above)
                bool isPortAvailable = DiagnosticsHelper.IsPortAvailable(bindAddr, port);
                report.AppendLine($"[DIAGNOSTIC] Port {port} available: {isPortAvailable}");

                if (!isPortAvailable)
                {
                    report.AppendLine("[DIAGNOSTIC] WARNING: Port appears to be in use by another application");
                }
            }
            catch (Exception)
            {
                report.AppendLine("[DIAGNOSTIC] ERROR: Invalid port configuration");
            }

            // Check firewall status
            report.AppendLine("[DIAGNOSTIC] Note: Ensure your firewall is not blocking the application");

            // Add instructions for Firefox configuration
            report.AppendLine("[DIAGNOSTIC] For Firefox HTTPS connections, ensure 'Proxy DNS when using SOCKS v5' is checked");

            // Output the report
            AddStatusMessage(report.ToString());
        }

        // Helper method to check if a port is available
        private bool IsPortAvailable(int port)
        {
            try
            {
                // Check for TCP listeners on the port
                IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
                TcpConnectionInformation[] tcpConnInfoArray = ipGlobalProperties.GetActiveTcpConnections();

                foreach (TcpConnectionInformation tcpi in tcpConnInfoArray)
                {
                    if (tcpi.LocalEndPoint.Port == port)
                    {
                        return false;
                    }
                }

                // Check for TCP listeners
                System.Net.IPEndPoint[] tcpListeners = ipGlobalProperties.GetActiveTcpListeners();

                foreach (System.Net.IPEndPoint endpoint in tcpListeners)
                {
                    if (endpoint.Port == port)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception)
            {
                // If we can't check, assume the port is not available
                return false;
            }
        }
    }
}
