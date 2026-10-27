using System;
using System.Runtime.InteropServices;

// A synchronized Kinect frame is projected into the color camera before matching.
internal sealed class AzureKinectColorFrame
{
    private byte[] source = Array.Empty<byte>();
    internal string Status { get; private set; } = "Waiting for body";

    private static bool Project(AzureKinectNative.Joint joint, ref AzureKinectNative.Calibration calibration, out AzureKinectNative.Float2 pixel)
    {
        pixel = default;
        if (joint.Confidence < 1 || !float.IsFinite(joint.X) || !float.IsFinite(joint.Y) || !float.IsFinite(joint.Z) || joint.Z <= 0) return false;
        var point = new AzureKinectNative.Float3 { X = joint.X, Y = joint.Y, Z = joint.Z };
        return AzureKinectNative.k4a_calibration_3d_to_2d(ref calibration, ref point, 0, 1, out pixel, out int valid) == 0 &&
            valid != 0 && float.IsFinite(pixel.X) && float.IsFinite(pixel.Y);
    }

    internal MediaPipeHandWorker.Frame Read(IntPtr bodyFrame, ref AzureKinectNative.Calibration calibration, AzureKinectCapture.Body body, long capturedTicks)
    {
        Status = "No usable right hand / wrist projection";
        if (body?.Joints == null || body.Joints.Length < 16) return null;
        // Kinect's hand center can lose confidence against the torso. Keep RGB
        // available at low confidence and use the corresponding RGB wrist point.
        // This position only proposes a target; RGB must still verify a fresh hand.
        bool wrist = Project(body.Joints[14], ref calibration, out var target);
        if (!wrist && !Project(body.Joints[15], ref calibration, out target)) return null;
        int confidence = body.Joints[wrist ? 14 : 15].Confidence;
        bool hasOtherWrist = Project(body.Joints[7], ref calibration, out var otherWrist);
        Status = "No synchronized RGB image";
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
                target.X < 0 || target.Y < 0 || target.X >= width || target.Y >= height)
            { Status = "Right target outside RGB image"; return null; }
            int length = stride * height;
            if (AzureKinectNative.k4a_image_get_size(image).ToUInt64() < (ulong)length) return null;
            IntPtr buffer = AzureKinectNative.k4a_image_get_buffer(image);
            if (buffer == IntPtr.Zero) return null;
            if (source.Length != length) source = new byte[length];
            Marshal.Copy(buffer, source, 0, length);
            // Preserve the sensor's 720p detail for small/rotated hands.
            int w = width, h = height;
            var pixels = new byte[w * h * 4];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int input = y * stride + x * 4, output = (y * w + x) * 4;
                pixels[output] = source[input + 2]; pixels[output + 1] = source[input + 1];
                pixels[output + 2] = source[input]; pixels[output + 3] = 255;
            }
            Status = $"RGB target: right {(wrist ? "wrist" : "palm")}, confidence {confidence}/3";
            return new MediaPipeHandWorker.Frame
            {
                Width = w, Height = h, BodyId = body.Id, CapturedTicks = capturedTicks,
                TargetX = target.X / width, TargetY = target.Y / height, Pixels = pixels,
                TargetJoint = wrist ? 0 : 9,
                OtherWristX = hasOtherWrist && otherWrist.X >= 0 && otherWrist.X < width && otherWrist.Y >= 0 && otherWrist.Y < height ? otherWrist.X / width : -1,
                OtherWristY = hasOtherWrist && otherWrist.X >= 0 && otherWrist.X < width && otherWrist.Y >= 0 && otherWrist.Y < height ? otherWrist.Y / height : -1
            };
        }
        finally
        {
            if (image != IntPtr.Zero) AzureKinectNative.k4a_image_release(image);
            if (capture != IntPtr.Zero) AzureKinectNative.k4a_capture_release(capture);
        }
    }
}
