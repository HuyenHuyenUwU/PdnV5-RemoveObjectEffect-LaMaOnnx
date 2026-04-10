using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;

namespace LaMaInpaintProject.Utils
{
    /// <summary>
    /// Small helper that loads an ONNX model and runs inference.
    /// The model file is expected to be named "model.onnx" and located next to the plugin DLL.
    /// </summary>
    public class OnnxInference
    {
        private InferenceSession _session;
        private readonly string _modelPath;

        /// <summary>
        /// Create the inference session.
        /// Uses DirectML (GPU) provider when available; falls back to CPU when not.
        /// </summary>
        public OnnxInference()
        {
            // Resolve model path relative to the running assembly location
            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _modelPath = Path.Combine(assemblyDir, "model.onnx");

            // Create session options and attempt to enable DirectML (GPU). If the provider
            // is not available the call will throw; we catch and continue so the CPU provider
            // will be used instead.
            var options = new SessionOptions();
            try
            {
                options.AppendExecutionProvider_DML(0);
            }
            catch
            {
                // Ignore provider errors; ONNX Runtime will use CPU execution instead.
            }

            // Create the InferenceSession. This will throw if the model file is missing or invalid.
            _session = new InferenceSession(_modelPath, options);
        }

        /// <summary>
        /// Run inference on a single image and mask.
        /// imageRGB: flattened float array in R-G-B channel order (shape: 1x3xH xW)
        /// maskGray: single-channel mask (shape: 1x1xH xW)
        /// The input names "image" and "mask" must match the ONNX model's input names.
        /// </summary>
        public float[] Run(float[] imageRGB, float[] maskGray, int width, int height)
        {
            // Build tensors matching the model input shapes: Batch x Channel x Height x Width
            var imgTensor = new DenseTensor<float>(imageRGB, new[] { 1, 3, height, width });
            var maskTensor = new DenseTensor<float>(maskGray, new[] { 1, 1, height, width });

            var inputs = new List<NamedOnnxValue>
            {
                // Input names must correspond to the ONNX model's inputs
                NamedOnnxValue.CreateFromTensor("image", imgTensor),
                NamedOnnxValue.CreateFromTensor("mask", maskTensor)
            };

            using (var results = _session.Run(inputs))
            {
                // Return the first output flattened to a float array
                return results.First().AsEnumerable<float>().ToArray();
            }
        }
    }
}
