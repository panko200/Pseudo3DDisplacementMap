using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.InteropServices;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Settings;

namespace Pseudo3DDisplacementMap
{
    [VideoEffect("疑似3Dディスプレイスメントマップ", ["描画"], ["3d", "displacement", "立体", "メッシュ"])]
    public class DisplacementMapEffect : VideoEffectBase
    {
        // ▼▼▼ ここから追加 ▼▼▼
        static DisplacementMapEffect()
        {
            LoadNativeLibrary();
        }

        private static void LoadNativeLibrary()
        {
            try
            {
                var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(pluginDir)) return;

                // 実行環境のアーキテクチャを取得 (x64 / x86 / ARM64)
                string arch = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "win-x64",
                    Architecture.X86 => "win-x86",
                    Architecture.Arm64 => "win-arm64",
                    _ => "win-x64"
                };

                // runtimesフォルダから正しいアーキテクチャのDLLパスを指定
                string dllPath = Path.Combine(pluginDir, "runtimes", arch, "native", "libSkiaSharp.dll");

                if (File.Exists(dllPath))
                {
                    NativeLibrary.Load(dllPath);
                }
                else
                {
                    // 以前のバージョンのように直下にある場合のフォールバック
                    string directPath = Path.Combine(pluginDir, "libSkiaSharp.dll");
                    if (File.Exists(directPath)) NativeLibrary.Load(directPath);
                }
            }
            catch
            {
                // ロードに失敗した場合は無視してSkiaSharp標準の機構に任せる
            }
        }
        public override string Label => "疑似3Dディスプレイスメントマップ";

        [Display(GroupName = "3D設定", Name = "深度マップ (白黒)", Description = "高さを指定する画像を選択してください。\n白が手前に盛り上がります。")]
        [FileSelector(FileGroupType.ImageItem)]
        public string HeightMapPath { get => heightMapPath; set => Set(ref heightMapPath, value); }
        private string heightMapPath = string.Empty;

        [Display(GroupName = "3D設定", Name = "押し出し量", Description = "立体の奥行き（高さ）の強さです。")]
        [AnimationSlider("F1", "px", -500, 500)]
        public Animation Depth { get; } = new Animation(100.0f, -10000, 10000);

        [Display(GroupName = "3D設定", Name = "分割数 (横)", Description = "メッシュの横の分割数です。")]
        [AnimationSlider("F0", "分割", 1, 128)]
        public Animation SubdivisionX { get; } = new Animation(32, 1, 512);

        [Display(GroupName = "3D設定", Name = "分割数 (縦)", Description = "メッシュの縦の分割数です。")]
        [AnimationSlider("F0", "分割", 1, 128)]
        public Animation SubdivisionY { get; } = new Animation(32, 1, 512);

        [Display(GroupName = "描画設定", Name = "簡易ライティング", Description = "光を当てて凹凸の立体感を強調します。")]
        [ToggleSlider]
        public bool EnableLighting { get => enableLighting; set => Set(ref enableLighting, value); }
        private bool enableLighting = false;

        [Display(GroupName = "描画設定", Name = "ワイヤーフレーム", Description = "画像を貼らずにメッシュの線だけを描画します。")]
        [ToggleSlider]
        public bool EnableWireframe { get => enableWireframe; set => Set(ref enableWireframe, value); }
        private bool enableWireframe = false;

        // ★追加：背面カリング
        [Display(GroupName = "描画設定", Name = "背面カリング", Description = "裏側を向いている面を非表示にします。\nカメラ接近時にメッシュが抜けた際、裏側が見えて不自然になるのを防げます。")]
        [ToggleSlider]
        public bool EnableCulling { get => enableCulling; set => Set(ref enableCulling, value); }
        private bool enableCulling = false;

        [Display(GroupName = "品質設定", Name = "最大解像度", Description = "内部で生成する画像の最大サイズ(2^n)です。\n値を上げると綺麗になりますが激重になります。")]
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
        protected override IEnumerable<IAnimatable> GetAnimatables() => [Depth, SubdivisionX, SubdivisionY];
    }
}