using System;
using System.IO;
using System.Runtime.InteropServices;

// Minimal Windows x64 C ABI, pinned to Sensor 1.4.1 / Body Tracking 1.1.2.
// No managed SDK binaries or model files are required at compile time.
internal static class AzureKinectNative
{
    private const CallingConvention Convention = CallingConvention.Cdecl;

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceConfiguration
    {
        public int ColorFormat, ColorResolution, DepthMode, CameraFps;
        [MarshalAs(UnmanagedType.I1)] public bool SynchronizedImagesOnly;
        public int DepthDelay, WiredSyncMode;
        public uint SubordinateDelay;
        [MarshalAs(UnmanagedType.I1)] public bool DisableStreamingIndicator;
    }

    // k4a_calibration_t: two 128-byte cameras + 16 48-byte extrinsics + two enums.
    // Opaque blittable storage preserves the entire native calibration unchanged.
    [StructLayout(LayoutKind.Sequential, Size = 1032)]
    internal struct Calibration { }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TrackerConfiguration
    {
        public int SensorOrientation, ProcessingMode, GpuDeviceId;
        public IntPtr ModelPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Joint
    {
        public float X, Y, Z, Qw, Qx, Qy, Qz;
        public int Confidence;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Float2 { public float X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Float3 { public float X, Y, Z; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Skeleton
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public Joint[] Joints;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AddDllDirectory(string directory);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string name);

    internal static void Load(string directory)
    {
        if (IntPtr.Size != 8 || Environment.OSVersion.Platform != PlatformID.Win32NT)
            throw new PlatformNotSupportedException("Azure Kinect requires Windows x64.");
        if (!File.Exists(Path.Combine(directory, "k4abt.dll")))
            throw new FileNotFoundException("SDK missing. Run Tools/SetupAzureKinect.ps1 first.");
        if (Marshal.SizeOf<DeviceConfiguration>() != 36 || Marshal.SizeOf<Joint>() != 32 || Marshal.SizeOf<Calibration>() != 1032
            || Marshal.SizeOf<TrackerConfiguration>() != 24 || Marshal.SizeOf<Skeleton>() != 1024)
            throw new InvalidOperationException("Unexpected Azure Kinect C ABI layout.");
        // Keep DLLs loaded for the process lifetime; devices/trackers are still disposed per session.
        // Reusing module handles avoids adding references on every Play/domain reload.
        if (GetModuleHandle("k4abt.dll") != IntPtr.Zero) return;
        if (AddDllDirectory(directory) == IntPtr.Zero) throw new InvalidOperationException("Cannot register SDK DLL directory.");
        foreach (string dll in new[] { "directml.dll", "onnxruntime.dll", "k4a.dll", "k4abt.dll" })
            if (LoadLibraryEx(Path.Combine(directory, dll), IntPtr.Zero, 0x00001100) == IntPtr.Zero)
                throw new InvalidOperationException($"Cannot load {dll} (Windows error {Marshal.GetLastWin32Error()}). Check x64 Visual C++ runtime.");
    }

    [DllImport("k4a", CallingConvention = Convention)] internal static extern uint k4a_device_get_installed_count();
    [DllImport("k4a", CallingConvention = Convention)] internal static extern int k4a_device_open(uint index, out IntPtr device);
    [DllImport("k4a", CallingConvention = Convention)] internal static extern int k4a_device_start_cameras(IntPtr device, ref DeviceConfiguration config);
    [DllImport("k4a", CallingConvention = Convention)] internal static extern int k4a_device_get_calibration(IntPtr device, int depthMode, int colorResolution, out Calibration calibration);
    [DllImport("k4a", CallingConvention = Convention)] internal static extern int k4a_device_get_capture(IntPtr device, out IntPtr capture, int timeoutMs);
    [DllImport("k4a", CallingConvention = Convention)] internal static extern void k4a_capture_release(IntPtr capture);
    [DllImport("k4a", CallingConvention = Convention)] internal static extern IntPtr k4a_capture_get_depth_image(IntPtr capture);
    [DllImport("k4a", CallingConvention = Convention)] internal static extern IntPtr k4a_capture_get_color_image(IntPtr capture);
    [DllImport("k4a", CallingConvention = Convention)] internal static extern IntPtr k4a_image_get_buffer(IntPtr image);
    [DllImport("k4a", CallingConvention = Convention)] internal static extern UIntPtr k4a_image_get_size(IntPtr image);
    [DllImport("k4a", CallingConvention = Convention)] internal static extern int k4a_image_get_width_pixels(IntPtr image);
    [DllImport("k4a", CallingConvention = Convention)] internal static extern int k4a_image_get_height_pixels(IntPtr image);
    [DllImport("k4a", CallingConvention = Convention)] internal static extern int k4a_image_get_stride_bytes(IntPtr image);
    [DllImport("k4a", CallingConvention = Convention)] internal static extern void k4a_image_release(IntPtr image);
    [DllImport("k4a", CallingConvention = Convention)] internal static extern int k4a_calibration_3d_to_2d(ref Calibration calibration, ref Float3 source, int sourceCamera, int targetCamera, out Float2 pixel, out int valid);
    [DllImport("k4a", CallingConvention = Convention)] internal static extern void k4a_device_stop_cameras(IntPtr device);
    [DllImport("k4a", CallingConvention = Convention)] internal static extern void k4a_device_close(IntPtr device);
    [DllImport("k4abt", CallingConvention = Convention)] internal static extern int k4abt_tracker_create(ref Calibration calibration, TrackerConfiguration config, out IntPtr tracker);
    [DllImport("k4abt", CallingConvention = Convention)] internal static extern int k4abt_tracker_enqueue_capture(IntPtr tracker, IntPtr capture, int timeoutMs);
    [DllImport("k4abt", CallingConvention = Convention)] internal static extern int k4abt_tracker_pop_result(IntPtr tracker, out IntPtr frame, int timeoutMs);
    [DllImport("k4abt", CallingConvention = Convention)] internal static extern uint k4abt_frame_get_num_bodies(IntPtr frame);
    [DllImport("k4abt", CallingConvention = Convention)] internal static extern uint k4abt_frame_get_body_id(IntPtr frame, uint index);
    [DllImport("k4abt", CallingConvention = Convention)] internal static extern int k4abt_frame_get_body_skeleton(IntPtr frame, uint index, out Skeleton skeleton);
    [DllImport("k4abt", CallingConvention = Convention)] internal static extern ulong k4abt_frame_get_device_timestamp_usec(IntPtr frame);
    [DllImport("k4abt", CallingConvention = Convention)] internal static extern IntPtr k4abt_frame_get_capture(IntPtr frame);
    [DllImport("k4abt", CallingConvention = Convention)] internal static extern IntPtr k4abt_frame_get_body_index_map(IntPtr frame);
    [DllImport("k4abt", CallingConvention = Convention)] internal static extern void k4abt_frame_release(IntPtr frame);
    [DllImport("k4abt", CallingConvention = Convention)] internal static extern void k4abt_tracker_shutdown(IntPtr tracker);
    [DllImport("k4abt", CallingConvention = Convention)] internal static extern void k4abt_tracker_destroy(IntPtr tracker);
}
