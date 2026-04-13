using System;
using System.Drawing;
using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.PropertySystem;
using PaintDotNet.Imaging;


namespace LaMaInpaintProject
{
    /// <summary>
    /// Plugin Paint.NET 5.x — xoá đối tượng bằng mô hình LaMa inpainting.
    ///
    /// Cách dùng:
    ///   1. Vẽ selection bao quanh vật thể cần xoá.
    ///   2. Vào menu Effects → AI Effects → Remove Object (LaMa).
    ///   3. Plugin đọc selection làm mask, chạy ONNX, vẽ kết quả lên canvas.
    ///
    /// Kiến trúc render:
    ///   - OnSetRenderInfo : chạy inference MỘT LẦN cho toàn canvas, lưu output tensor.
    ///   - OnRender        : copy pixel từ tensor vào từng tile (thread-safe, read-only).
    /// </summary>
    
    public sealed class RemoveObjectEffect : PropertyBasedEffect
    {
        // Kết quả inference, được tính một lần trong OnSetRenderInfo.
        private float[]? _outputTensor;
        private int _width;
        private int _height;

        // Precompute selection mask một lần dùng chung cho cả OnRender tiles
        private bool[]? _selectionMask;

        public RemoveObjectEffect()
            : base(
                "Remove Object (LaMa)",
                (IBitmapSource?)null,              // icon: gán IBitmapSource khi có asset
                "AI Effects",
                new EffectOptions { Flags = EffectFlags.Configurable })
        {
        }

        // Không có tham số cấu hình — trả về collection rỗng.
        protected override PropertyCollection OnCreatePropertyCollection()
            => new PropertyCollection(Array.Empty<Property>());

        /// <summary>
        /// Được gọi một lần trước vòng lặp render.
        /// Chạy toàn bộ pipeline AI ở đây để OnRender chỉ cần copy pixel.
        /// </summary>
        protected override void OnSetRenderInfo(
            PropertyBasedEffectConfigToken token,
            RenderArgs dstArgs,
            RenderArgs srcArgs)
        {
            base.OnSetRenderInfo(token, dstArgs, srcArgs);

            _width = srcArgs.Surface.Width;
            _height = srcArgs.Surface.Height;

            PdnRegion selection = EnvironmentParameters.GetSelectionAsPdnRegion();
            // Build bool mask một lần — dùng lại trong OnRender thay vì IsVisible() per-pixel
            _selectionMask = BuildBoolMask(selection, _width, _height);

            using var lama = new LaMaEffect();
            _outputTensor = lama.Run(srcArgs.Surface, selection);
        }

        /// <summary>
        /// Được gọi cho từng tile. Chỉ copy pixel — không chạy AI lại.
        /// Pixel ngoài selection được giữ nguyên từ source.
        /// </summary>
        protected override void OnRender(Rectangle[] rois, int startIndex, int length)
        {
            if (_outputTensor is null) return;

            int pixelCount = _width * _height;

            unsafe
            {
                for (int i = startIndex; i < startIndex + length; i++)
                {
                    Rectangle rect = rois[i];

                    for (int y = rect.Top; y < rect.Bottom; y++)
                    {
                        ColorBgra* dst = DstArgs.Surface.GetRowPointerUnchecked(y);
                        ColorBgra* src = SrcArgs.Surface.GetRowPointerUnchecked(y);

                        int rowBase = y * _width;

                        for (int x = rect.Left; x < rect.Right; x++)
                        {
                            if (!_selectionMask[rowBase + x])
                            {
                                dst[x] = src[x];
                            }
                            else
                            {
                                int idx = rowBase + x;
                                dst[x] = ColorBgra.FromBgra(
                                    b: ClampToByte(_outputTensor[2 * pixelCount + idx]),
                                    g: ClampToByte(_outputTensor[1 * pixelCount + idx]),
                                    r: ClampToByte(_outputTensor[0 * pixelCount + idx]),
                                    a: 255);
                            }
                        }
                    }
                }
            }
        }

        private static bool[] BuildBoolMask(PdnRegion selection, int w, int h)
        {
            bool[] mask = new bool[w * h];

            foreach (Rectangle rect in selection.GetRegionScansReadOnlyInt())
            {
                int x0 = Math.Max(rect.Left, 0);
                int y0 = Math.Max(rect.Top, 0);
                int x1 = Math.Min(rect.Right, w);
                int y1 = Math.Min(rect.Bottom, h);

                for (int y = y0; y < y1; y++)
                {
                    int rowBase = y * w;
                    for (int x = x0; x < x1; x++)
                        mask[rowBase + x] = true;
                }
            }

            return mask;
        }

        private static byte ClampToByte(float v)
            => (byte)Math.Clamp((int)(v * 255f), 0, 255);
    }
}