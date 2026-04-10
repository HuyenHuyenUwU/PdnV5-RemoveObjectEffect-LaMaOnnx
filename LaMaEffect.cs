using System;
using System.Drawing;
using LaMaInpaintProject.Utils;

namespace LaMaInpaintProject
{
    // Simplified effect wrapper used for testing outside Paint.NET.
    // This class demonstrates how to call the ONNX utilities with System.Drawing bitmaps.
    public class LaMaEffect
    {
        private readonly OnnxInference _aiEngine;

        public LaMaEffect()
        {
            _aiEngine = new OnnxInference();
        }

        // Run on regular System.Drawing.Bitmaps (example usage outside Paint.NET)
        public Bitmap RunInference(Bitmap source, Bitmap mask)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (mask is null) throw new ArgumentNullException(nameof(mask));
            if (source.Width != mask.Width || source.Height != mask.Height)
                throw new ArgumentException("Source and mask must have same dimensions");

            int width = source.Width;
            int height = source.Height;

            float[] img = ImageProcessing.BitmapToTensorArray(source);
            float[] m = ImageProcessing.BitmapToMaskArray(mask);

            float[] output = _aiEngine.Run(img, m, width, height);

            var result = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            ImageProcessing.TensorArrayToBitmap(output, result);
            return result;
        }
    }
}
