using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using SkiaSharp;
using YukkuriMovieMaker.Brush;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Brush;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Settings;
using Brush = YukkuriMovieMaker.Plugin.Brush.Brush;

namespace Pseudo3DDisplacementMap
{
    [VideoEffect("疑似3Dディスプレイスメントマップ", ["描画"], ["3d", "displacement", "立体", "メッシュ"])]
    public class DisplacementMapEffect : VideoEffectBase, IFileItem, IResourceItem
    {
        static DisplacementMapEffect()
        {
            RegisterSkiaNativeResolver();
        }

        private static bool _isResolverRegistered = false;

        private static void RegisterSkiaNativeResolver()
        {
            if (_isResolverRegistered) return;
            _isResolverRegistered = true;

            try
            {
                NativeLibrary.SetDllImportResolver(typeof(SKObject).Assembly, (libraryName, assembly, searchPath) =>
                {
                    if (libraryName == "libSkiaSharp")
                    {
                        var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                        if (!string.IsNullOrEmpty(pluginDir))
                        {
                            string arch = RuntimeInformation.ProcessArchitecture switch
                            {
                                Architecture.X64 => "win-x64",
                                Architecture.X86 => "win-x86",
                                Architecture.Arm64 => "win-arm64",
                                _ => "win-x64"
                            };

                            string dllPath = Path.Combine(pluginDir, "runtimes", arch, "native", "libSkiaSharp.dll");
                            if (File.Exists(dllPath) && NativeLibrary.TryLoad(dllPath, out IntPtr handle))
                            {
                                return handle;
                            }

                            string directPath = Path.Combine(pluginDir, "libSkiaSharp.dll");
                            if (File.Exists(directPath) && NativeLibrary.TryLoad(directPath, out IntPtr handleDirect))
                            {
                                return handleDirect;
                            }
                        }
                    }
                    return IntPtr.Zero;
                });
            }
            catch
            {
                // 無視
            }
        }

        public override string Label => "疑似3Dディスプレイスメントマップ";

        // =====================================================================
        // 深度ソース設定
        // =====================================================================
        public enum DepthSourceType
        {
            [Display(Name = "外部画像 (静止画)")]
            External = 1,
            [Display(Name = "自動深度推定")]
            Estimate = 2,
            [Display(Name = "別の画像・シーン (ブラシ)")]
            Brush = 3
        }

        [Display(GroupName = "3D設定", Name = "深度ソース", Description = "深度（高さ）情報の取得元を選択します。")]
        [EnumComboBox]
        public DepthSourceType DepthSource
        {
            get => depthSource;
            set
            {
                if (Set(ref depthSource, value))
                {
                    OnPropertyChanged(nameof(IsExternalSource));
                    OnPropertyChanged(nameof(IsEstimateSource));
                    OnPropertyChanged(nameof(IsBrushSource));
                    OnPropertyChanged(nameof(ShowRangeStability));
                    OnPropertyChanged(nameof(ShowTemporalBlend));
                    OnPropertyChanged(nameof(Brush));
                }
            }
        }
        private DepthSourceType depthSource = DepthSourceType.External;

        [Browsable(false)]
        public bool IsExternalSource => DepthSource == DepthSourceType.External;

        [Browsable(false)]
        public bool IsEstimateSource => DepthSource == DepthSourceType.Estimate;

        [Browsable(false)]
        public bool IsBrushSource => DepthSource == DepthSourceType.Brush;

        // =====================================================================
        // 3D設定
        // =====================================================================
        [Display(GroupName = "3D設定", Name = "深度マップ (白黒)", Description = "高さを指定する画像を選択してください。\n白が手前に盛り上がります。")]
        [FileSelector(FileGroupType.ImageItem)]
        [ShowPropertyEditorWhen(nameof(IsExternalSource), true)]
        public string HeightMapPath { get => heightMapPath; set => Set(ref heightMapPath, value); }
        private string heightMapPath = string.Empty;

        private readonly Brush brush = CreateBitmapBrush();

        [Display(GroupName = "3D設定", Name = "深度マップ (ブラシ)", Description = "深度ソースとして使用する別の画像やシーンを選択します。", AutoGenerateField = true)]
        [ShowPropertyEditorWhen(nameof(IsBrushSource), true)]
        public Brush? Brush => IsBrushSource ? brush : null;

        private static Brush CreateBitmapBrush()
        {
            var pluginType = PluginLoader.Plugins
                .OfType<IBrushPlugin>()
                .FirstOrDefault(p => p.GetType().Name == "BitmapBrushPlugin")?.GetType()
                ?? PluginLoader.Plugins.OfType<IBrushPlugin>().First().GetType();

            var createMethod = typeof(Brush).GetMethods().First(m => m.Name == "Create" && m.IsGenericMethod);
            var genericMethod = createMethod.MakeGenericMethod(pluginType);

            return (Brush)genericMethod.Invoke(null, null)!;
        }

        [Display(GroupName = "3D設定", Name = "押し出し量", Description = "立体の奥行き（高さ）の強さです。")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation Depth { get; } = new Animation(100.0f, -10000, 10000);

        [Display(GroupName = "3D設定", Name = "分割数 (横)", Description = "メッシュの横の分割数です。")]
        [AnimationSlider("F0", "分割", 1, 128)]
        public Animation SubdivisionX { get; } = new Animation(32, 1, 512);

        [Display(GroupName = "3D設定", Name = "分割数 (縦)", Description = "メッシュの縦の分割数です。")]
        [AnimationSlider("F0", "分割", 1, 128)]
        public Animation SubdivisionY { get; } = new Animation(32, 1, 512);

        // =====================================================================
        // 自動深度推定設定 (深度推定モード時のみ表示)
        // =====================================================================
        [Display(GroupName = "自動深度推定設定", Name = "入力幅", Description = "推論に使用する際の画像の幅 (14px刻み)")]
        [TextBoxSlider("F0", "* 14px", 1, 50)]
        [Range(1, 2340)]
        [DefaultValue(37)]
        [ShowPropertyEditorWhen(nameof(IsEstimateSource), true)]
        public int InputWidth
        {
            get => inputWidth;
            set => Set(ref inputWidth, value);
        }
        private int inputWidth = 37;

        [Display(GroupName = "自動深度推定設定", Name = "入力高さ", Description = "推論に使用する際の画像の高さ (14px刻み)")]
        [TextBoxSlider("F0", "* 14px", 1, 50)]
        [Range(1, 2340)]
        [DefaultValue(37)]
        [ShowPropertyEditorWhen(nameof(IsEstimateSource), true)]
        public int InputHeight
        {
            get => inputHeight;
            set => Set(ref inputHeight, value);
        }
        private int inputHeight = 37;

        [Display(GroupName = "自動深度推定設定", Name = "範囲安定化を有効化", Description = "ONにすると深度の正規化範囲を安定化し、フレーム間のピントのブレを抑えます。")]
        [ToggleSlider]
        [ShowPropertyEditorWhen(nameof(IsEstimateSource), true)]
        public bool UseFixedRange
        {
            get => useFixedRange;
            set
            {
                if (Set(ref useFixedRange, value))
                {
                    OnPropertyChanged(nameof(ShowRangeStability));
                }
            }
        }
        private bool useFixedRange = false;

        [Display(GroupName = "自動深度推定設定", Name = "安定化強度", Description = "高いほど推論のスケール変化が鈍くなり安定します。\n0% = 毎フレーム再計算\n100% = 初回フレームで固定")]
        [AnimationSlider("F0", "%", 0, 100)]
        [ShowPropertyEditorWhen(nameof(ShowRangeStability), true)]
        public Animation RangeStability { get; } = new Animation(90f, 0, 100);

        [Browsable(false)]
        public bool ShowRangeStability => IsEstimateSource && UseFixedRange;

        [Display(GroupName = "自動深度推定設定", Name = "時間スムージングを有効化", Description = "ONにすると前フレームの深度マップとブレンドし、凹凸の変化を滑らかにします。")]
        [ToggleSlider]
        [ShowPropertyEditorWhen(nameof(IsEstimateSource), true)]
        public bool UseTemporalSmoothing
        {
            get => useTemporalSmoothing;
            set
            {
                if (Set(ref useTemporalSmoothing, value))
                {
                    OnPropertyChanged(nameof(ShowTemporalBlend));
                }
            }
        }
        private bool useTemporalSmoothing = false;

        [Display(GroupName = "自動深度推定設定", Name = "ブレンド率", Description = "前フレームとのブレンド率。高いほど滑らかになりますが残像も出やすくなります。")]
        [AnimationSlider("F0", "%", 0, 99)]
        [ShowPropertyEditorWhen(nameof(ShowTemporalBlend), true)]
        public Animation TemporalBlend { get; } = new Animation(50f, 0, 99);

        [Browsable(false)]
        public bool ShowTemporalBlend => IsEstimateSource && UseTemporalSmoothing;

        // =====================================================================
        // 描画設定
        // =====================================================================
        [Display(GroupName = "描画設定", Name = "簡易ライティング", Description = "光を当てて凹凸の立体感を強調します。")]
        [ToggleSlider]
        public bool EnableLighting { get => enableLighting; set => Set(ref enableLighting, value); }
        private bool enableLighting = false;

        [Display(GroupName = "描画設定", Name = "ワイヤーフレーム", Description = "画像を貼らずにメッシュの線だけを描画します。")]
        [ToggleSlider]
        public bool EnableWireframe { get => enableWireframe; set => Set(ref enableWireframe, value); }
        private bool enableWireframe = false;

        [Display(GroupName = "描画設定", Name = "背面カリング", Description = "裏側を向いている面を非表示にします。")]
        [ToggleSlider]
        public bool EnableCulling { get => enableCulling; set => Set(ref enableCulling, value); }
        private bool enableCulling = false;

        [Display(GroupName = "品質設定", Name = "最大解像度", Description = "内部で生成する画像の最大サイズ(2^n)です。")]
        [EnumComboBox]
        public ResolutionType MaxResolution { get => maxResolution; set => Set(ref maxResolution, value); }
        private ResolutionType maxResolution = ResolutionType.Res1024;

        public enum ResolutionType
        {
            [Display(Name = "256px (最軽量・粗い)")]
            Res256 = 256,
            [Display(Name = "512px (軽量)")]
            Res512 = 512,
            [Display(Name = "1024px (標準)")]
            Res1024 = 1024,
            [Display(Name = "2048px (高画質)")]
            Res2048 = 2048,
            [Display(Name = "4096px (最高画質・重い)")]
            Res4096 = 4096,
            [Display(Name = "8192px (激重)")]
            Res8192 = 8192
        }

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => new DisplacementMapEffectProcessor(devices, this);

        protected override IEnumerable<IAnimatable> GetAnimatables()
        {
            yield return Depth;
            yield return SubdivisionX;
            yield return SubdivisionY;
            yield return RangeStability;
            yield return TemporalBlend;
            yield return brush;
        }

        public override IEnumerable<string> GetFiles()
        {
            foreach (var file in base.GetFiles()) yield return file;
            if (brush != null)
                foreach (var file in brush.GetFiles()) yield return file;
        }

        public override void ReplaceFile(string from, string to)
        {
            base.ReplaceFile(from, to);
            brush?.ReplaceFile(from, to);
        }

        public override IEnumerable<TimelineResource> GetResources()
        {
            foreach (var res in base.GetResources()) yield return res;
            if (brush != null)
                foreach (var res in brush.GetResources()) yield return res;
        }
    }
}