using System;
using System.Runtime.InteropServices;

// Depth-only diagnostic frame; generated on the worker and never written to disk.
internal sealed class AzureKinectHandDepth
{
    internal sealed class Image
    {
        internal int Width, Height;
        // Unity texture order: bottom row first, RGBA32.
        internal byte[] Pixels;
    }
    internal sealed class Region
    {
        internal float CenterX, CenterY, RadiusX, RadiusY;
        internal float HandDepth;
        internal int SelectedCount, TipCount;
    }
    private byte[] depthBytes = Array.Empty<byte>();
    private byte[] bodyBytes = Array.Empty<byte>();

    internal Image Measure(IntPtr frame, ref AzureKinectNative.Calibration calibration, AzureKinectCapture.Body[] bodies)
    {
        IntPtr capture = AzureKinectNative.k4abt_frame_get_capture(frame);
        IntPtr depth = IntPtr.Zero, mask = IntPtr.Zero;
        try
        {
            if (capture == IntPtr.Zero) return null;
            depth = AzureKinectNative.k4a_capture_get_depth_image(capture);
            mask = AzureKinectNative.k4abt_frame_get_body_index_map(frame);
            if (depth == IntPtr.Zero || mask == IntPtr.Zero) return null;
            int width = AzureKinectNative.k4a_image_get_width_pixels(depth);
            int height = AzureKinectNative.k4a_image_get_height_pixels(depth);
            int depthStride = AzureKinectNative.k4a_image_get_stride_bytes(depth);
            int maskStride = AzureKinectNative.k4a_image_get_stride_bytes(mask);
            if (width <= 0 || height <= 0 || width > 2048 || height > 2048 || depthStride < width * 2 || maskStride < width ||
                AzureKinectNative.k4a_image_get_width_pixels(mask) != width || AzureKinectNative.k4a_image_get_height_pixels(mask) != height ||
                depthStride > 16384 || maskStride > 16384) return null;
            int depthLength = depthStride * height, maskLength = maskStride * height;
            if (AzureKinectNative.k4a_image_get_size(depth).ToUInt64() < (ulong)depthLength || AzureKinectNative.k4a_image_get_size(mask).ToUInt64() < (ulong)maskLength) return null;
            IntPtr depthBuffer = AzureKinectNative.k4a_image_get_buffer(depth), maskBuffer = AzureKinectNative.k4a_image_get_buffer(mask);
            if (depthBuffer == IntPtr.Zero || maskBuffer == IntPtr.Zero) return null;
            if (depthBytes.Length != depthLength) depthBytes = new byte[depthLength];
            if (bodyBytes.Length != maskLength) bodyBytes = new byte[maskLength];
            Marshal.Copy(depthBuffer, depthBytes, 0, depthLength);
            Marshal.Copy(maskBuffer, bodyBytes, 0, maskLength);
            var image = new Image { Width = width, Height = height, Pixels = new byte[width * height * 4] };
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int p = y * depthStride + x * 2;
                int z = depthBytes[p] | depthBytes[p + 1] << 8;
                int v = z == 0 ? 0 : Math.Max(20, Math.Min(255, 255 - (z - 500) * 235 / 3500));
                int q = ((height - 1 - y) * width + x) * 4;
                image.Pixels[q] = image.Pixels[q + 1] = image.Pixels[q + 2] = (byte)v;
                image.Pixels[q + 3] = 255;
            }
            // Use the first tracked body's hand, matching the diagnostic controller.
            if (bodies.Length > 0)
                bodies[0].RightHandArea = Area(ref calibration, bodies[0], 0, width, height, depthStride, maskStride, image);
            return image;
        }
        finally
        {
            if (mask != IntPtr.Zero) AzureKinectNative.k4a_image_release(mask);
            if (depth != IntPtr.Zero) AzureKinectNative.k4a_image_release(depth);
            if (capture != IntPtr.Zero) AzureKinectNative.k4a_capture_release(capture);
        }
    }

    private float Area(ref AzureKinectNative.Calibration calibration, AzureKinectCapture.Body body, byte bodyIndex, int width, int height, int depthStride, int maskStride, Image image)
    {
        var joints = body.Joints;
        if (joints == null || joints.Length < 16) return 0;
        var wrist = joints[14]; var hand = joints[15];
        if (!Valid(wrist) || !Valid(hand) || hand.Z < 500 || hand.Z > 3500) return 0;
        if (!Project(ref calibration, hand.X, hand.Y, hand.Z, out var center) ||
            !Project(ref calibration, wrist.X, wrist.Y, wrist.Z, out var wristPixel) ||
            !Project(ref calibration, hand.X + 120, hand.Y, hand.Z, out var xEdge) ||
            !Project(ref calibration, hand.X, hand.Y + 120, hand.Z, out var yEdge)) return 0;
        float rx = Math.Abs(xEdge.X - center.X), ry = Math.Abs(yEdge.Y - center.Y);
        if (rx < 5 || ry < 5 || rx > 160 || ry > 160 || center.X - rx < 0 || center.X + rx >= width || center.Y - ry < 0 || center.Y + ry >= height) return 0;
        float dx = center.X - wristPixel.X, dy = center.Y - wristPixel.Y;
        float length = (float)Math.Sqrt(dx * dx + dy * dy);
        if (length < 2) return 0;
        dx /= length; dy /= length;
        int count = 0, fingertips = 0;
        for (int y = (int)(center.Y - ry); y <= (int)(center.Y + ry); y++)
        for (int x = (int)(center.X - rx); x <= (int)(center.X + rx); x++)
        {
            float offsetX = x - center.X, offsetY = y - center.Y;
            if (offsetX * offsetX / (rx * rx) + offsetY * offsetY / (ry * ry) > 1 || offsetX * dx + offsetY * dy < -0.35f * Math.Min(rx, ry)) continue;
            if (bodyBytes[y * maskStride + x] != bodyIndex) continue;
            int p = y * depthStride + x * 2;
            int z = depthBytes[p] | depthBytes[p + 1] << 8;
            if (z != 0 && Math.Abs(z - hand.Z) <= 120)
            {
                count++;
                if (offsetX * dx + offsetY * dy > 0.2f * Math.Min(rx, ry)) fingertips++;
                int q = ((height - 1 - y) * width + x) * 4;
                image.Pixels[q] = 32; image.Pixels[q + 1] = 235; image.Pixels[q + 2] = 235;
            }
        }
        body.RightHandRegion = new Region { CenterX = center.X, CenterY = center.Y, RadiusX = rx, RadiusY = ry, HandDepth = hand.Z, SelectedCount = count, TipCount = fingertips };
        return count >= 12 ? (count * 0.35f + fingertips * 2f) * hand.Z * hand.Z / 1000000f : 0;
    }
    private static bool Valid(AzureKinectNative.Joint joint) => joint.Confidence >= 2 && float.IsFinite(joint.X) && float.IsFinite(joint.Y) && float.IsFinite(joint.Z);
    private static bool Project(ref AzureKinectNative.Calibration calibration, float x, float y, float z, out AzureKinectNative.Float2 pixel)
    {
        var point = new AzureKinectNative.Float3 { X = x, Y = y, Z = z };
        return AzureKinectNative.k4a_calibration_3d_to_2d(ref calibration, ref point, 0, 0, out pixel, out int valid) == 0 && valid != 0 && float.IsFinite(pixel.X) && float.IsFinite(pixel.Y);
    }
}
