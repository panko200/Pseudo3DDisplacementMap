using SkiaSharp;
using System; 
using System.IO; 
using System.Reflection; 
using System.Runtime.InteropServices; 
using System;
using System.Collections.Generic;
using System.Numerics;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace Pseudo3DDisplacementMap
{
    internal class DisplacementMapEffectProcessor : IVideoEffectProcessor, IDisposable
    {
        private readonly IGraphicsDevicesAndContext _devices;
        private readonly DisplacementMapEffect _item;
        private ID2D1Image? _input;

        private ID2D1Bitmap1? _cpuReadBitmap;
        private ID2D1Bitmap1? _gpuBitmap;
        private ID2D1DeviceContext? _localContext;
        private SKBitmap? _cachedInputTexture;
        private SKBitmap? _cachedHeightMap;
        private string _loadedHeightPath = string.Empty;

        private ID2D1Bitmap? _outD2DBitmap;
        private AffineTransform2D? _mapTransformEffect;

        // ★メモリリーク対策：出力画像をキャッシュして使い回す
        private ID2D1Image? _transformOutput;

        public DisplacementMapEffectProcessor(IGraphicsDevicesAndContext devices, DisplacementMapEffect item)
        {
            _devices = devices;
            _item = item;
            _mapTransformEffect = new AffineTransform2D(_devices.DeviceContext);

            // ★メモリリーク対策：コンストラクタで1回だけOutputの参照を取得しておく
            _transformOutput = _mapTransformEffect.Output;
        }

        // ★メモリリーク対策：毎回 GetOutput() を呼ばずにキャッシュを返す
        public ID2D1Image Output => _transformOutput ?? _input!;

        public void SetInput(ID2D1Image? input) { _input = input; }
        public void ClearInput() { _input = null; }

        private SKBitmap? GetInputTexture(int width, int height, Vortice.RawRectF rawBounds)
        {
            if (_input == null) return null;
            var dc = _devices.DeviceContext;

            int maxTexSize = (int)_item.MaxResolution;

            float scale = 1.0f;
            if (width > maxTexSize || height > maxTexSize)
                scale = Math.Min((float)maxTexSize / width, (float)maxTexSize / height);

            int texW = Math.Max(1, (int)(width * scale));
            int texH = Math.Max(1, (int)(height * scale));

            if (_cpuReadBitmap == null || _cpuReadBitmap.PixelSize.Width != texW || _cpuReadBitmap.PixelSize.Height != texH)
            {
                _cpuReadBitmap?.Dispose(); _gpuBitmap?.Dispose(); _localContext?.Dispose(); _cachedInputTexture?.Dispose();
                _cpuReadBitmap = dc.CreateBitmap(new SizeI(texW, texH), IntPtr.Zero, 0, new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
                _localContext = dc.Device.CreateDeviceContext(DeviceContextOptions.None);
                _gpuBitmap = _localContext.CreateBitmap(new SizeI(texW, texH), new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96, BitmapOptions.Target));
                _localContext.Target = _gpuBitmap;
                _cachedInputTexture = new SKBitmap(texW, texH, SKColorType.Bgra8888, SKAlphaType.Premul);
            }

            _localContext!.BeginDraw();
            _localContext.Clear(new Vortice.Mathematics.Color4(0, 0, 0, 0));
            _localContext.Transform = Matrix3x2.CreateTranslation(-rawBounds.Left, -rawBounds.Top) * Matrix3x2.CreateScale(scale);
            _localContext.DrawImage(_input);
            _localContext.EndDraw();

            _cpuReadBitmap!.CopyFromBitmap(_gpuBitmap);
            var map = _cpuReadBitmap.Map(MapOptions.Read);
            try
            {
                unsafe
                {
                    byte* srcPtr = (byte*)map.Bits;
                    byte* dstPtr = (byte*)_cachedInputTexture!.GetPixels();
                    long srcPitch = map.Pitch;
                    long dstPitch = _cachedInputTexture.RowBytes;

                    for (int y = 0; y < texH; y++)
                    {
                        Buffer.MemoryCopy(srcPtr + y * srcPitch, dstPtr + y * dstPitch, dstPitch, Math.Min(srcPitch, dstPitch));
                    }
                }
            }
            finally { _cpuReadBitmap.Unmap(); }

            return _cachedInputTexture;
        }

        private unsafe float GetHeightValue(float u, float v)
        {
            if (_cachedHeightMap == null) return 0f;
            int x = Math.Clamp((int)(u * _cachedHeightMap.Width), 0, _cachedHeightMap.Width - 1);
            int y = Math.Clamp((int)(v * _cachedHeightMap.Height), 0, _cachedHeightMap.Height - 1);

            int bpp = _cachedHeightMap.BytesPerPixel;
            byte* p = (byte*)_cachedHeightMap.GetPixels() + y * _cachedHeightMap.RowBytes + x * bpp;

            if (bpp == 4)
                return (p[2] * 0.299f + p[1] * 0.587f + p[0] * 0.114f) / 255f;
            else if (bpp == 1)
                return p[0] / 255f;
            else
                return 0f;
        }

        public DrawDescription Update(EffectDescription desc)
        {
            if (_input == null) return desc.DrawDescription;

            var frame = desc.ItemPosition.Frame;
            var len = desc.ItemDuration.Frame;
            var fps = desc.FPS;
            var drawDesc = desc.DrawDescription;
            var dc = _devices.DeviceContext;

            if (_item.HeightMapPath != _loadedHeightPath)
            {
                _loadedHeightPath = _item.HeightMapPath;
                _cachedHeightMap?.Dispose();
                if (System.IO.File.Exists(_loadedHeightPath))
                    _cachedHeightMap = SKBitmap.Decode(_loadedHeightPath);
                else
                    _cachedHeightMap = null;
            }

            Vortice.RawRectF rawBounds;
            try { rawBounds = dc.GetImageLocalBounds(_input); } catch { return drawDesc; }
            int imgW = (int)Math.Ceiling(rawBounds.Right) - (int)Math.Floor(rawBounds.Left);
            int imgH = (int)Math.Ceiling(rawBounds.Bottom) - (int)Math.Floor(rawBounds.Top);
            if (imgW <= 0 || imgH <= 0) return drawDesc;

            float d2r = (float)Math.PI / 180.0f;
            Matrix4x4 localRotation = Matrix4x4.CreateRotationZ(drawDesc.Rotation.Z * d2r) *
                                      Matrix4x4.CreateRotationY(-drawDesc.Rotation.Y * d2r) *
                                      Matrix4x4.CreateRotationX(-drawDesc.Rotation.X * d2r);

            Matrix4x4 localTransform = localRotation;
            Matrix4x4 worldTranslation = Matrix4x4.CreateTranslation(drawDesc.Draw.X, drawDesc.Draw.Y, drawDesc.Draw.Z);
            Matrix4x4 perspective = new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -0.001f, 0, 0, 0, 1);
            Matrix4x4 m_fullProjection = localTransform * worldTranslation * drawDesc.Camera * perspective;

            Vector4 projectedCenter = Vector4.Transform(Vector3.Zero, m_fullProjection);
            float tx = 0, ty = 0, tz = 0;
            if (Math.Abs(projectedCenter.W) > 1e-6f)
            {
                tx = projectedCenter.X / projectedCenter.W;
                ty = projectedCenter.Y / projectedCenter.W;
                tz = projectedCenter.W;
            }
            Matrix4x4 m_adjustment = Matrix4x4.CreateTranslation(-tx, -ty, 0);
            Matrix4x4 m_internalDraw = m_fullProjection * m_adjustment;

            if (!Matrix4x4.Invert(drawDesc.Camera, out Matrix4x4 invView)) invView = Matrix4x4.Identity;
            Vector3 worldEye = Vector3.Transform(new Vector3(0, 0, 1000), invView);

            int subX = Math.Max(1, (int)_item.SubdivisionX.GetValue(frame, len, fps));
            int subY = Math.Max(1, (int)_item.SubdivisionY.GetValue(frame, len, fps));
            float depthScale = (float)_item.Depth.GetValue(frame, len, fps);

            int vertsX = subX + 1;
            int vertsY = subY + 1;
            Vector3[,] vertices = new Vector3[vertsX, vertsY];
            Vector2[,] uvs = new Vector2[vertsX, vertsY];

            for (int y = 0; y < vertsY; y++)
            {
                float v = (float)y / subY;
                for (int x = 0; x < vertsX; x++)
                {
                    float u = (float)x / subX;
                    float hVal = GetHeightValue(u, v);

                    float posX = (u - 0.5f) * imgW;
                    float posY = (v - 0.5f) * imgH;
                    float posZ = hVal * depthScale;

                    vertices[x, y] = new Vector3(posX, posY, posZ);
                    uvs[x, y] = new Vector2(u, v);
                }
            }

            var faces = new List<GridFace>();
            for (int y = 0; y < subY; y++)
            {
                for (int x = 0; x < subX; x++)
                {
                    var v0 = vertices[x, y];
                    var v1 = vertices[x + 1, y];
                    var v2 = vertices[x + 1, y + 1];
                    var v3 = vertices[x, y + 1];

                    bool isBehind = false;
                    foreach (var v in new[] { v0, v1, v2, v3 })
                    {
                        if (Vector4.Transform(v, m_internalDraw).W <= 0.00001f) { isBehind = true; break; }
                    }
                    if (isBehind) continue;

                    Vector3 center = (v0 + v1 + v2 + v3) / 4f;
                    Vector3 worldCenter = Vector3.Transform(center, localTransform * worldTranslation);
                    float distSq = (worldEye - worldCenter).LengthSquared();

                    Vector3 normal = Vector3.Normalize(Vector3.Cross(v1 - v0, v3 - v0));
                    Vector3 rotatedNormal = Vector3.Normalize(Vector3.TransformNormal(normal, localTransform));

                    if (_item.EnableCulling)
                    {
                        Vector3 viewDir = worldEye - worldCenter;
                        if (Vector3.Dot(rotatedNormal, viewDir) <= 0) continue;
                    }

                    float brightness = 1.0f;
                    if (_item.EnableLighting)
                    {
                        float intensity = Vector3.Dot(rotatedNormal, Vector3.Normalize(new Vector3(-1, -1, 1)));
                        brightness = 0.4f + 0.6f * Math.Clamp((intensity + 1f) / 2f, 0f, 1f);
                    }

                    faces.Add(new GridFace
                    {
                        V0 = v0,
                        V1 = v1,
                        V2 = v2,
                        V3 = v3,
                        UV0 = uvs[x, y],
                        UV1 = uvs[x + 1, y],
                        UV2 = uvs[x + 1, y + 1],
                        UV3 = uvs[x, y + 1],
                        DistanceSq = distSq,
                        Brightness = brightness
                    });
                }
            }

            if (faces.Count == 0) return drawDesc;

            faces.Sort((a, b) => b.DistanceSq.CompareTo(a.DistanceSq));

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var face in faces)
            {
                foreach (var v in new[] { face.V0, face.V1, face.V2, face.V3 })
                {
                    Vector4 t = Vector4.Transform(v, m_internalDraw);
                    float px = t.X / t.W;
                    float py = t.Y / t.W;
                    if (px < minX) minX = px; if (px > maxX) maxX = px;
                    if (py < minY) minY = py; if (py > maxY) maxY = py;
                }
            }

            float limit = 8192f;
            minX = Math.Max(minX, -limit); maxX = Math.Min(maxX, limit);
            minY = Math.Max(minY, -limit); maxY = Math.Min(maxY, limit);

            int pad = 5;
            float rawOutW = (maxX - minX) + pad * 2;
            float rawOutH = (maxY - minY) + pad * 2;
            if (rawOutW <= 0 || rawOutH <= 0) return drawDesc;

            float renderScale = 1.0f;
            float maxCanvasSize = (float)_item.MaxResolution;

            if (rawOutW > maxCanvasSize || rawOutH > maxCanvasSize)
                renderScale = Math.Min(maxCanvasSize / rawOutW, maxCanvasSize / rawOutH);

            int outW = (int)Math.Ceiling(rawOutW * renderScale);
            int outH = (int)Math.Ceiling(rawOutH * renderScale);
            float drawOffsetX = -minX + pad;
            float drawOffsetY = -minY + pad;

            using var outBitmap = new SKBitmap(outW, outH, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(outBitmap);
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(renderScale, renderScale);
            canvas.Translate(drawOffsetX, drawOffsetY);

            using var paint = new SKPaint { IsAntialias = true };
            paint.FilterQuality = _item.MaxResolution <= DisplacementMapEffect.ResolutionType.Res1024 ? SKFilterQuality.Low : SKFilterQuality.High;

            SKBitmap? texture = GetInputTexture(imgW, imgH, rawBounds);
            SKShader? texShader = null;

            if (!_item.EnableWireframe && texture != null)
                texShader = SKShader.CreateBitmap(texture, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);

            SKPoint[] pts = new SKPoint[4];
            SKPoint[] texs = new SKPoint[4];
            SKColor[] cols = new SKColor[4];
            ushort[] indices = new ushort[] { 0, 1, 2, 0, 2, 3 };

            foreach (var face in faces)
            {
                Vector4 t0 = Vector4.Transform(face.V0, m_internalDraw); pts[0] = new SKPoint(t0.X / t0.W, t0.Y / t0.W);
                Vector4 t1 = Vector4.Transform(face.V1, m_internalDraw); pts[1] = new SKPoint(t1.X / t1.W, t1.Y / t1.W);
                Vector4 t2 = Vector4.Transform(face.V2, m_internalDraw); pts[2] = new SKPoint(t2.X / t2.W, t2.Y / t2.W);
                Vector4 t3 = Vector4.Transform(face.V3, m_internalDraw); pts[3] = new SKPoint(t3.X / t3.W, t3.Y / t3.W);

                if (_item.EnableWireframe)
                {
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 1.0f;
                    paint.Color = new SKColor(0, 255, 0, 255);
                    using var path = new SKPath();
                    path.MoveTo(pts[0]); path.LineTo(pts[1]); path.LineTo(pts[2]); path.LineTo(pts[3]); path.Close();
                    canvas.DrawPath(path, paint);
                }
                else
                {
                    float tw = texture!.Width;
                    float th = texture!.Height;
                    texs[0] = new SKPoint(face.UV0.X * tw, face.UV0.Y * th);
                    texs[1] = new SKPoint(face.UV1.X * tw, face.UV1.Y * th);
                    texs[2] = new SKPoint(face.UV2.X * tw, face.UV2.Y * th);
                    texs[3] = new SKPoint(face.UV3.X * tw, face.UV3.Y * th);

                    byte b = (byte)(255 * face.Brightness);
                    SKColor col = new SKColor(b, b, b, 255);
                    cols[0] = col; cols[1] = col; cols[2] = col; cols[3] = col;

                    paint.Shader = texShader;
                    paint.Style = SKPaintStyle.Fill;
                    paint.IsAntialias = false;
                    canvas.DrawVertices(SKVertexMode.Triangles, pts, texs, cols, indices, paint);
                    paint.IsAntialias = true;
                    canvas.DrawVertices(SKVertexMode.Triangles, pts, texs, cols, indices, paint);
                }
            }

            texShader?.Dispose();

            _outD2DBitmap?.Dispose();
            _outD2DBitmap = dc.CreateBitmap(new SizeI(outW, outH), outBitmap.GetPixels(), outBitmap.RowBytes, new BitmapProperties(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied)));

            if (_mapTransformEffect != null && _outD2DBitmap != null)
            {
                _mapTransformEffect.SetInput(0, _outD2DBitmap, true);
                _mapTransformEffect.TransformMatrix = Matrix3x2.CreateScale(1.0f / renderScale) * Matrix3x2.CreateTranslation(-drawOffsetX, -drawOffsetY);
            }

            return drawDesc with
            {
                Draw = drawDesc.Draw with { X = tx, Y = ty, Z = -tz },
                Rotation = Vector3.Zero,
                Camera = Matrix4x4.Identity
            };
        }

        public void Dispose()
        {
            _cpuReadBitmap?.Dispose();
            _gpuBitmap?.Dispose();
            _localContext?.Dispose();
            _cachedInputTexture?.Dispose();
            _cachedHeightMap?.Dispose();
            _outD2DBitmap?.Dispose();

            // ★メモリリーク対策：キャッシュしたOutputのDisposeも忘れずに
            _transformOutput?.Dispose();
            _mapTransformEffect?.Dispose();
        }

        private class GridFace
        {
            public Vector3 V0, V1, V2, V3;
            public Vector2 UV0, UV1, UV2, UV3;
            public float DistanceSq;
            public float Brightness;
        }
    }
}