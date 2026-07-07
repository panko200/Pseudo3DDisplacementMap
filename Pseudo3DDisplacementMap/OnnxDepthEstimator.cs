using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.ConstrainedExecution;
using System.Threading;
using System.Threading.Tasks;

namespace Pseudo3DDisplacementMap
{
    class OnnxDepthEstimator : CriticalFinalizerObject, IDisposable
    {
        public static OnnxDepthEstimator Instance { get; } = new OnnxDepthEstimator();

        private readonly ConcurrentBag<InferenceSession> _sessions = new();
        private readonly byte[]? _modelData;

        private int _currentWidth = -1;
        private int _currentHeight = -1;
        private readonly object _rebuildLock = new object();

        private readonly SemaphoreSlim _semaphore = new(4, 4);

        bool Disposed { get; set; }

        private OnnxDepthEstimator()
        {
            // ★ リソースの取得パスを移行先プロジェクトに調整
            using var modelStream = typeof(OnnxDepthEstimator).Assembly
                .GetManifestResourceStream("Pseudo3DDisplacementMap.depth_anything_v2_vits_dynamic.onnx");
            if (modelStream == null) return;

            _modelData = new byte[modelStream.Length];
            modelStream.ReadExactly(_modelData);
        }

        /// <summary>
        /// 深度推定を実行して raw（正規化前）の深度値配列を返します。
        /// </summary>
        public float[] Estimate(Vector4[] image, int width, int height)
        {
            if (_modelData == null || Disposed) return [];

            if (_currentWidth != width || _currentHeight != height)
            {
                lock (_rebuildLock)
                {
                    if (_currentWidth != width || _currentHeight != height)
                    {
                        while (_sessions.TryTake(out var oldSession))
                            oldSession.Dispose();
                        _currentWidth = width;
                        _currentHeight = height;
                    }
                }
            }

            int hw = height * width;
            var tensorBuffer = ArrayPool<float>.Shared.Rent(3 * hw);
            try
            {
                Parallel.For(0, height, y =>
                {
                    var imageSpan = image.AsSpan(y * width, width);
                    int offset = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        var color = imageSpan[x];
                        tensorBuffer[offset + x] = color.X;
                        tensorBuffer[hw + offset + x] = color.Y;
                        tensorBuffer[hw * 2 + offset + x] = color.Z;
                    }
                });

                var tensor = new DenseTensor<float>(tensorBuffer.AsMemory(0, 3 * hw), [1, 3, height, width]);

                _semaphore.Wait();
                InferenceSession? session = null;
                try
                {
                    if (!_sessions.TryTake(out session))
                        session = CreateSession();

                    var inputs = new NamedOnnxValue[]
                    {
                        NamedOnnxValue.CreateFromTensor(session.InputNames[0], tensor)
                    };
                    using var result = session.Run(inputs);

                    return result[0].AsTensor<float>().ToArray();
                }
                finally
                {
                    if (session != null && !Disposed)
                    {
                        if (_currentWidth == width && _currentHeight == height)
                            _sessions.Add(session);
                        else
                            session.Dispose();
                    }
                    _semaphore.Release();
                }
            }
            finally
            {
                ArrayPool<float>.Shared.Return(tensorBuffer);
            }
        }

        private InferenceSession CreateSession()
        {
            var options = new SessionOptions();
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            options.EnableMemoryPattern = false;
            options.EnableCpuMemArena = false;

            try
            {
                options.AppendExecutionProvider_DML(0);
                return new InferenceSession(_modelData!, options);
            }
            catch
            {
                var cpuOptions = new SessionOptions();
                cpuOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                cpuOptions.EnableMemoryPattern = false;
                cpuOptions.EnableCpuMemArena = false;
                return new InferenceSession(_modelData!, cpuOptions);
            }
        }

        public static void Normalize(float[] values, float? fixedMin = null, float? fixedMax = null)
        {
            var span = values.AsSpan();
            int vecSize = Vector<float>.Count;

            float min, max;

            if (fixedMin.HasValue && fixedMax.HasValue)
            {
                min = fixedMin.Value;
                max = fixedMax.Value;
            }
            else
            {
                min = float.MaxValue;
                max = float.MinValue;

                if (span.Length >= vecSize)
                {
                    var vMin = new Vector<float>(float.MaxValue);
                    var vMax = new Vector<float>(float.MinValue);
                    int i = 0;
                    for (; i <= span.Length - vecSize; i += vecSize)
                    {
                        var chunk = new Vector<float>(span.Slice(i));
                        vMin = Vector.Min(vMin, chunk);
                        vMax = Vector.Max(vMax, chunk);
                    }
                    for (int j = 0; j < vecSize; j++)
                    {
                        if (vMin[j] < min) min = vMin[j];
                        if (vMax[j] > max) max = vMax[j];
                    }
                    for (; i < span.Length; i++)
                    {
                        if (span[i] < min) min = span[i];
                        if (span[i] > max) max = span[i];
                    }
                }
                else
                {
                    foreach (var v in span)
                    {
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }
                }
            }

            float range = max - min;
            if (range == 0f)
            {
                span.Fill(0f);
                return;
            }

            float invRange = 1f / range;
            var vMinN = new Vector<float>(min);
            var vInvRange = new Vector<float>(invRange);
            int k = 0;
            for (; k <= span.Length - vecSize; k += vecSize)
            {
                var chunk = new Vector<float>(span.Slice(k));
                ((chunk - vMinN) * vInvRange).CopyTo(span.Slice(k));
            }
            for (; k < span.Length; k++)
                span[k] = (span[k] - min) * invRange;
        }

        public void Dispose()
        {
            DisposeInternal();
            GC.SuppressFinalize(this);
        }

        void DisposeInternal()
        {
            if (Disposed) return;
            while (_sessions.TryTake(out var session))
                session.Dispose();
            Disposed = true;
        }

        ~OnnxDepthEstimator()
        {
            DisposeInternal();
        }
    }
}