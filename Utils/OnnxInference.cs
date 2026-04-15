using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.IO;
using System.Reflection;

namespace LaMaInpaintProject.Utils
{
    public class OnnxInference : IDisposable
    {
        private InferenceSession _session;
        private readonly string _modelPath;
        private readonly string _imageInputName;
        private readonly string _maskInputName;
        private bool _disposed;

        public OnnxInference()
        {
            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                                 ?? AppContext.BaseDirectory;
            string modelPath = Path.Combine(assemblyDir, "lama_fp32.onnx");
     
            _session = new InferenceSession(modelPath, new SessionOptions());

            // Resolve input names
            var inputMeta = _session.InputMetadata;

            if (inputMeta.Count < 2)
                throw new InvalidOperationException(
                    $"Expected ≥2 inputs, found {inputMeta.Count}: {string.Join(", ", inputMeta.Keys)}");

            string? imageName = null, maskName = null;
            foreach (var kv in inputMeta)
            {
                int channels = kv.Value.Dimensions.Length >= 2 ? kv.Value.Dimensions[1] : -1;
                if (channels == 3 && imageName == null) imageName = kv.Key;
                else if (channels == 1 && maskName == null) maskName = kv.Key;
            }

            if (imageName == null || maskName == null)
            {
                var keys = inputMeta.Keys.ToList();
                imageName = keys[0];
                maskName = keys[1];
            }

            _imageInputName = imageName;
            _maskInputName = maskName;
        }

        public float[] Run(float[] imageRGB, float[] maskGray, int width, int height)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var imgTensor = new DenseTensor<float>(imageRGB, new[] { 1, 3, height, width });
            var maskTensor = new DenseTensor<float>(maskGray, new[] { 1, 1, height, width });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_imageInputName, imgTensor),
                NamedOnnxValue.CreateFromTensor(_maskInputName, maskTensor)
            };

            using var results = _session.Run(inputs);
            return results.First().AsEnumerable<float>().ToArray();
        }
        public void Dispose()
        {
            if (_disposed) return;
            _session.Dispose();
            _disposed = true;
        }
    }
}

//using Microsoft.ML.OnnxRuntime;
//using Microsoft.ML.OnnxRuntime.Tensors;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.IO;
//using System.Reflection;

//namespace LaMaInpaintProject.Utils
//{
//    /// <summary>
//    /// Small helper that loads an ONNX model and runs inference.
//    /// The model file is expected to be named "model.onnx" and located next to the plugin DLL.
//    /// </summary>
//    public class OnnxInference : IDisposable
//    {
//        private InferenceSession _session;
//        private readonly string _modelPath;
//        private readonly string _imageInputName;
//        private readonly string _maskInputName;
//        private bool _disposed;

//        /// <summary>
//        /// Create the inference session.
//        /// Uses DirectML (GPU) provider when available; falls back to CPU when not.
//        /// </summary>
//        public OnnxInference()
//        {
//            // Resolve model path relative to the running assembly location
//            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
//            _modelPath = Path.Combine(assemblyDir, "model.onnx");

//            // Thử DirectML trước, nếu fail thì dùng CPU
//            _session = TryCreateSession(_modelPath, useGpu: true)
//                    ?? TryCreateSession(_modelPath, useGpu: false)
//                    ?? throw new InvalidOperationException("Cannot create ONNX session.");

//            // Create session options and attempt to enable DirectML (GPU). If the provider
//            // is not available the call will throw; we catch and continue so the CPU provider
//            // will be used instead.
//            var options = new SessionOptions();
//            try
//            {
//                options.AppendExecutionProvider_DML(0);
//            }
//            catch
//            {
//                // Ignore provider errors; ONNX Runtime will use CPU execution instead.
//            }

//            // Create the InferenceSession. This will throw if the model file is missing or invalid.
//            _session = new InferenceSession(_modelPath, options);

//            // inputMetadata có cấu trúc là
//            // Dictionary<string, NodeMetadata>, trong đó key là tên input (ví dụ "image", "mask") và value chứa thông tin về shape, type, v.v.
//            var inputMeta = _session.InputMetadata;
//            if (inputMeta.Count < 2)
//                throw new InvalidOperationException(
//                    $"Expected at least 2 model inputs, found {inputMeta.Count}. " +
//                    $"Inputs: {string.Join(", ", inputMeta.Keys)}");
//            string? imageName = null, maskName = null;
//            foreach (var kv in inputMeta)
//            {
//                var dims = kv.Value.Dimensions; // may contain -1 for dynamic dims
//                int channels = dims.Length >= 2 ? dims[1] : -1;

//                if (channels == 3 && imageName == null)
//                    imageName = kv.Key;
//                else if (channels == 1 && maskName == null)
//                    maskName = kv.Key;
//            }
//            if (imageName == null || maskName == null)
//            {
//                var keys = inputMeta.Keys.ToList();
//                imageName = keys[0];
//                maskName = keys[1];
//            }
//            _imageInputName = imageName;
//            _maskInputName = maskName;
//        }

//        /// <summary>
//        /// Run inference on a single image and mask.
//        /// imageRGB: flattened float array in R-G-B channel order (shape: 1x3xH xW)
//        /// maskGray: single-channel mask (shape: 1x1xH xW)
//        /// The input names "image" and "mask" must match the ONNX model's input names.
//        /// </summary>
//        public float[] Run(float[] imageRGB, float[] maskGray, int width, int height)
//        {
//            ObjectDisposedException.ThrowIf(_disposed, this);

//            // Build tensors matching the model input shapes: Batch x Channel x Height x Width
//            var imgTensor = new DenseTensor<float>(imageRGB, new[] { 1, 3, height, width });
//            var maskTensor = new DenseTensor<float>(maskGray, new[] { 1, 1, height, width });

//            var inputs = new List<NamedOnnxValue>
//            {
//                // Input names must correspond to the ONNX model's inputs
//                NamedOnnxValue.CreateFromTensor(_imageInputName, imgTensor),
//                NamedOnnxValue.CreateFromTensor(_maskInputName, maskTensor)
//            };

//            using var results = _session.Run(inputs);
//            // Return the first output flattened to a float array
//            return results.First().AsEnumerable<float>().ToArray();
//        }

//        // ---------- helper ---------------------------------------------------------------------------------
//        private static InferenceSession? TryCreateSession(string modelPath, bool useGpu)
//        {
//            try
//            {
//                var options = new SessionOptions();
//                if (useGpu)
//                    options.AppendExecutionProvider_DML(0);
//                return new InferenceSession(modelPath, options);
//            }
//            catch
//            {
//                return null;
//            }
//        }
//        public void Dispose()
//        {
//            if (_disposed) return;
//            _session.Dispose();
//            _disposed = true;
//        }
//    }
//}
