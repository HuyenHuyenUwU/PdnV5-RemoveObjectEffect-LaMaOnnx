using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.PropertySystem;
using System.Drawing;

namespace MyPlugin
{
    public class InvertEffect : PropertyBasedEffect
    {
        public InvertEffect()
            : base("Invert Color", (PaintDotNet.Imaging.IBitmapSource?)null, "Color", new EffectOptions())
        {
        }

        protected override PropertyCollection OnCreatePropertyCollection()
        {
            return new PropertyCollection();
        }

        protected override void OnRender(Rectangle[] rois, int startIndex, int length)
        {
            var src = this.SrcArgs.Surface;
            var dst = this.DstArgs.Surface;

            for (int i = startIndex; i < startIndex + length; i++)
            {
                Rectangle rect = rois[i];

                for (int y = rect.Top; y < rect.Bottom; y++)
                {
                    for (int x = rect.Left; x < rect.Right; x++)
                    {
                        ColorBgra color = src[x, y];

                        color.R = (byte)(255 - color.R);
                        color.G = (byte)(255 - color.G);
                        color.B = (byte)(255 - color.B);

                        dst[x, y] = color;
                    }
                }
            }
        }
    }
}