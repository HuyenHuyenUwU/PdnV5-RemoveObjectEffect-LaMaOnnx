using LaMaInpaintProject.Utils;
using PaintDotNet;
using PaintDotNet.Imaging;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private const int MODEL_SIZE = 512; // LaMa model requires 512x512 input

        public LaMaEffect()
        {
            _onnx = new OnnxInference();
        }

        /// <summary>
        /// Chạy inpainting trên toàn bộ canvas.
        /// </summary>
        /// <param name='src'>Surface nguồn (ảnh gốc).</param>
        /// <param name='selection'>PdnRegion đại diện cho vùng selection.</param>
        /// Quá trình convert: Surface (aka cái canvas mình đang làm việc Paint.Net) → Bitmap (để resize & debug) → float[] tensor (input cho ONNX)
        /// <returns>
        ///   Float array chứa kết quả inpaint, có kích thước w*h*3 (RGB), được flatten theo row-major order
        /// </returns>
        /// 

        public float[] Run(Surface src, PdnRegion selection)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            int w = src.Width, h = src.Height;

            // ── 1. Build float tensors ở kích thước gốc ──────────────────────
            float[] imgOrig = SurfaceToTensor(src);           // 3 × w × h
            float[] maskOrig = BuildMaskTensor(selection, w, h); // 1 × w × h

            // ── 2. Resize tensor xuống MODEL_SIZE × MODEL_SIZE ────────────────
            float[] imgResized = ResizeTensor(imgOrig, w, h, MODEL_SIZE, MODEL_SIZE, channels: 3);
            float[] maskResized = ResizeTensorNearest(maskOrig, w, h, MODEL_SIZE, MODEL_SIZE);

            // Kết quả từ model: channel-first [3 × 512 × 512]
            float[] modelOutput = _onnx.Run(imgResized, maskResized, MODEL_SIZE, MODEL_SIZE);
            
            // Debug: kiểm tra kích thước output từ model
            // for (int i = 0; i < modelOutput.Length; i++) // normalize back to [0,1] range if model output is [0,255]
            //     modelOutput[i] /= 255f;
            // Model trả về kích thước gốc (w×h), không phải 512×512
            // Kiểm tra để quyết định có cần resize không
            if (modelOutput.Length == 3 * w * h)
                return modelOutput; // đúng kích thước rồi, dùng luôn

            // Resize kết quả về kích thước gốc (w × h) để trả về cho UI. Lưu ý: model output đã là RGB, nên channels=3.
            return ResizeTensor(modelOutput, MODEL_SIZE, MODEL_SIZE, w, h, channels: 3);
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

        // ── Helpers ────────────────────────────────────────────────────────────
        private static unsafe float[] SurfaceToTensor(Surface surface)
        {
            int w = surface.Width, h = surface.Height;
            int plane = w * h;
            float[] result = new float[3 * plane];

            for (int y = 0; y < h; y++)
            {
                ColorBgra* row = surface.GetRowPointerUnchecked(y);
                int rowBase = y * w;
                for (int x = 0; x < w; x++)
                {
                    result[0 * plane + rowBase + x] = row[x].R / 255f;
                    result[1 * plane + rowBase + x] = row[x].G / 255f;
                    result[2 * plane + rowBase + x] = row[x].B / 255f;
                }
            }
            return result;
        }

        private static float[] BuildMaskTensor(PdnRegion selection, int w, int h)
        {
            float[] mask = new float[w * h];
            foreach (Rectangle rect in selection.GetRegionScansReadOnlyInt())
            {
                int x0 = Math.Max(rect.Left, 0), x1 = Math.Min(rect.Right, w);
                int y0 = Math.Max(rect.Top, 0), y1 = Math.Min(rect.Bottom, h);
                for (int y = y0; y < y1; y++)
                {
                    int rb = y * w;
                    for (int x = x0; x < x1; x++)
                        mask[rb + x] = 1f;
                }
            }
            return mask;
        }

        // ── Bilinear resize cho ảnh RGB (channel-first: C×H×W) ─
        private static float[] ResizeTensor(float[] src,
            int sw, int sh, int dw, int dh, int channels)
        {
            float[] dst = new float[channels * dw * dh];
            float xScale = (float)sw / dw;
            float yScale = (float)sh / dh;

            for (int c = 0; c < channels; c++)
            {
                int srcPlane = c * sw * sh;
                int dstPlane = c * dw * dh;

                for (int dy = 0; dy < dh; dy++)
                {
                    float fy = (dy + 0.5f) * yScale - 0.5f;
                    int y0 = Math.Max((int)MathF.Floor(fy), 0);
                    int y1 = Math.Min(y0 + 1, sh - 1);
                    float wy = fy - MathF.Floor(fy);

                    for (int dx = 0; dx < dw; dx++)
                    {
                        float fx = (dx + 0.5f) * xScale - 0.5f;
                        int x0 = Math.Max((int)MathF.Floor(fx), 0);
                        int x1 = Math.Min(x0 + 1, sw - 1);
                        float wx = fx - MathF.Floor(fx);

                        float v = src[srcPlane + y0 * sw + x0] * (1 - wx) * (1 - wy)
                                + src[srcPlane + y0 * sw + x1] * wx * (1 - wy)
                                + src[srcPlane + y1 * sw + x0] * (1 - wx) * wy
                                + src[srcPlane + y1 * sw + x1] * wx * wy;

                        dst[dstPlane + dy * dw + dx] = v;
                    }
                }
            }
            return dst;
        }

        // ── Nearest-neighbor resize cho mask (tránh blur) ─────────────────────
        private static float[] ResizeTensorNearest(float[] src,
            int sw, int sh, int dw, int dh)
        {
            float[] dst = new float[dw * dh];
            float xScale = (float)sw / dw;
            float yScale = (float)sh / dh;

            for (int dy = 0; dy < dh; dy++)
            {
                int sy = Math.Min((int)(dy * yScale), sh - 1);
                for (int dx = 0; dx < dw; dx++)
                {
                    int sx = Math.Min((int)(dx * xScale), sw - 1);
                    dst[dy * dw + dx] = src[sy * sw + sx] >= 0.5f ? 1f : 0f;
                }
            }
            return dst;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _onnx.Dispose();
            _disposed = true;
        }
    }
}