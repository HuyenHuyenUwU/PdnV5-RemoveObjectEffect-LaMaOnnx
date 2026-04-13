using LaMaInpaintProject.Utils;
using PaintDotNet;
using PaintDotNet.Imaging;
using System;
using System.Drawing;
using System.Drawing.Imaging;

using PixelFormat = System.Drawing.Imaging.PixelFormat;
using ImageLockMode = System.Drawing.Imaging.ImageLockMode;


namespace LaMaInpaintProject
{
    /// <summary>
    /// Orchestrates inpainting: nhận Surface + Pdn Region, chạy ONNX, rồi trả kết quả về dạng float[].
    ///
    /// Mask được build trực tiếp từ PdnRegion (không cần MaskSurface hay Bitmap trung gian):
    ///   pixel trong selection → 1.0f
    ///   pixel ngoài selection → 0.0f
    /// </summary>
    public sealed class LaMaEffect : IDisposable
    {
        private readonly OnnxInference _onnx;
        private bool _disposed;

        public LaMaEffect()
        {
            _onnx = new OnnxInference();
        }

        /// <summary>
        /// Chạy inpainting trên toàn bộ canvas.
        /// </summary>
        /// <param name='src'>Surface nguồn (ảnh gốc).</param>
        /// <param name='selection'>PdnRegion đại diện cho vùng selection.</param>
        /// <returns>
        ///   Float tensor kết quả, layout channel-first [R|G|B], kích thước 3×W×H.
        ///   Dùng ImageProcessing.TensorArrayToBitmap() hoặc ghi thẳng vào Surface.
        /// </returns>
        /// 

        public float[] Run(Surface src, PdnRegion selection)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            int w = src.Width, h = src.Height;

            using Bitmap srcBmp = SurfaceToBitmap(src);

            float[] imgTensor = ImageProcessing.BitmapToTensorArray(srcBmp);
            float[] maskTensor = BuildMaskFromRegion(selection, w, h);

            return _onnx.Run(imgTensor, maskTensor, w, h);
        }

        /// <summary>
        /// Copy pixel từ PDN Surface sang System.Drawing.Bitmap để ImageProcessing có thể đọc.
        /// </summary>
        private static unsafe Bitmap SurfaceToBitmap(Surface surface)
        {
            int w = surface.Width, h = surface.Height;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            var bd = bmp.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++)
                {
                    ColorBgra* src = surface.GetRowPointerUnchecked(y);
                    byte* dst = (byte*)bd.Scan0 + y * bd.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        dst[x * 4 + 0] = src[x].B;
                        dst[x * 4 + 1] = src[x].G;
                        dst[x * 4 + 2] = src[x].R;
                        dst[x * 4 + 3] = src[x].A;
                    }
                }
            }
            finally { bmp.UnlockBits(bd); }

            return bmp;
        }

        /// <summary>
        /// Build mask float[] trực tiếp từ PdnRegion — không cần Bitmap trung gian.
        /// Dùng GetRegionScansReadOnlyInt() để lấy rectangle list, fill 1.0f vào vùng selection.
        /// </summary>
        private static float[] BuildMaskFromRegion(PdnRegion selection, int w, int h)
        {
            float[] mask = new float[w * h]; // khởi tạo = 0.0f (ngoài selection)

            foreach (Rectangle rect in selection.GetRegionScansReadOnlyInt())
            {
                // Clamp để tránh out-of-bounds nếu selection vượt canvas
                int x0 = Math.Max(rect.Left, 0);
                int y0 = Math.Max(rect.Top, 0);
                int x1 = Math.Min(rect.Right, w);
                int y1 = Math.Min(rect.Bottom, h);

                for (int y = y0; y < y1; y++)
                {
                    int rowBase = y * w;
                    for (int x = x0; x < x1; x++)
                        mask[rowBase + x] = 1.0f;
                }
            }

            return mask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _onnx.Dispose();
            _disposed = true;
        }
    }
}