using System;
using System.Runtime.InteropServices;

// A synchronized Kinect frame is projected into the color camera before matching.
internal sealed class AzureKinectColorFrame
{
    private byte[] source = Array.Empty<byte>();

    internal MediaPipeHandWorker.Frame Read(IntPtr bodyFrame, ref AzureKinectNative.Calibration calibration, AzureKinectCapture.Body body, long capturedTicks)
    {
        if (body?.Joints == null || body.Joints.Length < 16) return null;
        var hand = body.Joints[15];
        if (hand.Confidence < 2 || !float.IsFinite(hand.X) || !float.IsFinite(hand.Y) || !float.IsFinite(hand.Z) || hand.Z <= 0) return null;
        var point = new AzureKinectNative.Float3 { X = hand.X, Y = hand.Y, Z = hand.Z };
        if (AzureKinectNative.k4a_calibration_3d_to_2d(ref calibration, ref point, 0, 1, out var target, out int valid) != 0 ||
            valid == 0 || !float.IsFinite(target.X) || !float.IsFinite(target.Y)) return null;
        IntPtr capture = AzureKinectNative.k4abt_frame_get_capture(bodyFrame);
        IntPtr image = IntPtr.Zero;
        try
        {
            if (capture == IntPtr.Zero) return null;
            image = AzureKinectNative.k4a_capture_get_color_image(capture);
            if (image == IntPtr.Zero) return null;
            int width = AzureKinectNative.k4a_image_get_width_pixels(image);
            int height = AzureKinectNative.k4a_image_get_height_pixels(image);
            int stride = AzureKinectNative.k4a_image_get_stride_bytes(image);
            if (width <= 0 || height <= 0 || width > 4096 || height > 2160 || stride < width * 4 || stride > 16384 ||
                target.X < 0 || target.Y < 0 || target.X >= width || target.Y >= height) return null;
            int length = stride * height;
            if (AzureKinectNative.k4a_image_get_size(image).ToUInt64() < (ulong)length) return null;
            IntPtr buffer = AzureKinectNative.k4a_image_get_buffer(image);
            if (buffer == IntPtr.Zero) return null;
            if (source.Length != length) source = new byte[length];
            Marshal.Copy(buffer, source, 0, length);
            // 720p BGRA -> 360p RGBA; no disk or network I/O.
            int w = width / 2, h = height / 2;
            var pixels = new byte[w * h * 4];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int input = y * 2 * stride + x * 8, output = (y * w + x) * 4;
                pixels[output] = source[input + 2]; pixels[output + 1] = source[input + 1];
                pixels[output + 2] = source[input]; pixels[output + 3] = 255;
            }
            return new MediaPipeHandWorker.Frame
            {
                Width = w, Height = h, BodyId = body.Id, CapturedTicks = capturedTicks,
                TargetX = target.X / width, TargetY = target.Y / height, Pixels = pixels
            };
        }
        finally
        {
            if (image != IntPtr.Zero) AzureKinectNative.k4a_image_release(image);
            if (capture != IntPtr.Zero) AzureKinectNative.k4a_capture_release(capture);
        }
    }
}
