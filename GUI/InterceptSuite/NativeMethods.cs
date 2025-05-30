using System;
using System.Runtime.InteropServices;
using System.Text;

namespace InterceptSuite
{    /// <summary>
    /// This class centralizes all P/Invoke declarations for better organization and maintenance
    /// </summary>
    internal static class NativeMethods
    {
        // DLL P/Invoke declarations for Intercept.dll
        [DllImport("Intercept.dll", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool start_proxy();

        [DllImport("Intercept.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void stop_proxy();

        [DllImport("Intercept.dll", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool set_config(
            [MarshalAs(UnmanagedType.LPStr)] string bind_addr,
            int port,
            [MarshalAs(UnmanagedType.LPStr)] string log_file,
            int verbose_mode);

        [DllImport("Intercept.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int get_system_ips(
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder buffer,
            int buffer_size);

        [DllImport("Intercept.dll", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool get_proxy_config(
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder bind_addr,
            ref int port,
            [MarshalAs(UnmanagedType.LPStr)] StringBuilder log_file,
            ref int verbose_mode);

        [DllImport("Intercept.dll", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool get_proxy_stats(
            ref int connections,
            ref int bytes_transferred);

        // Callback function registration
        [DllImport("Intercept.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void set_log_callback(LogCallbackDelegate callback);

        [DllImport("Intercept.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void set_status_callback(StatusCallbackDelegate callback);

        [DllImport("Intercept.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void set_connection_callback(ConnectionCallbackDelegate callback);

        [DllImport("Intercept.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void set_stats_callback(StatsCallbackDelegate callback);

        [DllImport("Intercept.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void set_disconnect_callback(DisconnectCallbackDelegate callback);

        // Interception control functions
        [DllImport("Intercept.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void set_intercept_callback(InterceptCallbackDelegate callback);

        [DllImport("Intercept.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void set_intercept_enabled([MarshalAs(UnmanagedType.Bool)] bool enabled);

        [DllImport("Intercept.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void set_intercept_direction(int direction);

        [DllImport("Intercept.dll", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void respond_to_intercept(int connection_id, int action, [MarshalAs(UnmanagedType.LPArray)] byte[] modified_data, int modified_length);

        // Win32 API for DLL loading
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool AddDllDirectory(string lpPathName);

        // Windows API functions for DLL loading
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FreeLibrary(IntPtr hModule);

        // Callback function delegates
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void LogCallbackDelegate(
            [MarshalAs(UnmanagedType.LPStr)] string timestamp,
            [MarshalAs(UnmanagedType.LPStr)] string src_ip,
            [MarshalAs(UnmanagedType.LPStr)] string dst_ip,
            int dst_port,
            [MarshalAs(UnmanagedType.LPStr)] string message_type,
            [MarshalAs(UnmanagedType.LPStr)] string data);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void StatusCallbackDelegate([MarshalAs(UnmanagedType.LPStr)] string message);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ConnectionCallbackDelegate(
            [MarshalAs(UnmanagedType.LPStr)] string client_ip,
            int client_port,
            [MarshalAs(UnmanagedType.LPStr)] string target_host,
            int target_port,
            int connection_id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void StatsCallbackDelegate(
            int total_connections,
            int active_connections,
            int total_bytes_transferred);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void DisconnectCallbackDelegate(
            int connection_id,
            [MarshalAs(UnmanagedType.LPStr)] string reason);


        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void InterceptCallbackDelegate(
            int connection_id,
            [MarshalAs(UnmanagedType.LPStr)] string direction,
            [MarshalAs(UnmanagedType.LPStr)] string src_ip,
            [MarshalAs(UnmanagedType.LPStr)] string dst_ip,
            int dst_port,
            IntPtr data,
            int data_length);
    }
}
