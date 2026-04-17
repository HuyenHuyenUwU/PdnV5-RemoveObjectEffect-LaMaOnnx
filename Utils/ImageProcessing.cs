using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace LaMaInpaintProject.Utils
{
    /// <summary>
    /// Image processing helpers used to convert between System.Drawing.Bitmaps and
    /// the float arrays expected by the ONNX model (channel-first: R, G, B).
    /// Unsafe code with LockBits is used for performance.
    /// </summary>
    public static class ImageProcessing
    {
        /// <summary>
        /// Convert a 32bpp ARGB bitmap into a flattened float array in R-G-B order.
        /// Output layout: [R plane (H*W), G plane (H*W), B plane (H*W)].
        /// </summary>
        public static float[] BitmapToTensorArray(Bitmap bmp)
        {
            int width = bmp.Width;
            int height = bmp.Height;
            float[] result = new float[3 * width * height];

            // Lock the bitmap data for fast pointer access. Caller must allow unsafe code.
            var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* ptr = (byte*)data.Scan0; // pointer to first pixel
                    int stride = data.Stride;      // number of bytes per row

                    for (int y = 0; y < height; y++)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            // Pixel memory layout is B G R A for Format32bppArgb
                            byte b = row[x * 4 + 0];
                            byte g = row[x * 4 + 1];
                            byte r = row[x * 4 + 2];

                            // Map to channel-first flattened layout
                            result[0 * (width * height) + y * width + x] = r / 255f;
                            result[1 * (width * height) + y * width + x] = g / 255f;
                            result[2 * (width * height) + y * width + x] = b / 255f;
                        }
                    }
                }
            }
            finally
            {
                // Always unlock bits to avoid locking the bitmap indefinitely
                bmp.UnlockBits(data);
            }

            return result;
        }

        /// <summary>
        /// Convert a 32bpp ARGB bitmap into a single-channel grayscale mask as float values [0,1].
        /// The returned mask is row-major with size width*height.
        /// </summary>
        public static float[] BitmapToMaskArray(Bitmap bmp)
        {
            int width = bmp.Width;
            int height = bmp.Height;
            float[] mask = new float[width * height];

            var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* ptr = (byte*)data.Scan0;
                    int stride = data.Stride;

                    for (int y = 0; y < height; y++)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            byte b = row[x * 4 + 0];
                            byte g = row[x * 4 + 1];
                            byte r = row[x * 4 + 2];

                            // Compute luminance and normalize to [0,1]
                            mask[y * width + x] = ((0.299f * r) + (0.587f * g) + (0.114f * b)) / 255f;
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            return mask;
        }

        /// <summary>
        /// Write a flattened R-G-B tensor array into a 32bpp ARGB bitmap.
        /// Input layout must match BitmapToTensorArray (three planes of size H*W).
        /// Bytes are clamped to [0,255].
        /// </summary>
        public static void TensorArrayToBitmap(float[] data, Bitmap target)
        {
            int width = target.Width;
            int height = target.Height;

            var bmpData = target.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* ptr = (byte*)bmpData.Scan0;
                    int stride = bmpData.Stride;

                    for (int y = 0; y < height; y++)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            // Read channels from channel-first layout and convert to bytes
                            byte r = (byte)Math.Clamp(data[0 * (width * height) + y * width + x] * 255, 0, 255);
                            byte g = (byte)Math.Clamp(data[1 * (width * height) + y * width + x] * 255, 0, 255);
                            byte b = (byte)Math.Clamp(data[2 * (width * height) + y * width + x] * 255, 0, 255);

                            // Store as B G R A
                            row[x * 4 + 0] = b;
                            row[x * 4 + 1] = g;
                            row[x * 4 + 2] = r;
                            row[x * 4 + 3] = 255;
                        }
                    }
                }
            }
            finally
            {
                target.UnlockBits(bmpData);
            }
        }
    }
}
