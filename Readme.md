疑似3Dディスプレイスメントマップ  
製作者：Panko200  
配布場所：https://github.com/panko200/Pseudo3DDisplacementMap

## 概要

YukkuriMovieMaker4 にて動作する映像エフェクトプラグインです。  
白黒の深度マップによって、アイテム自体をYMM4の3D空間でZ軸方向にメッシュに分割して曲げるプラグインです。

## 使用方法

「描画」グループの中に、映像エフェクト「疑似3Dディスプレイスメントマップ」が追加されます。  
該当エフェクトを適用して、深度マップ(白黒)に、白が高い、黒がそのままという方式の画像を入れると、  
それに合わせて、画像がZ軸側に飛び出します。

深度マップがない画像を3Dっぽく表示させたい場合は、Depth Estimate(もしくは、疑似被写界深度)とアイテム出力エフェクトという二つのプラグインをダウンロード・インストールして来て、Depth Estimate(もしくは、疑似被写界深度にて、デバッグ表示内の、深度マップ(推論悔過)を選択した状態)をアイテム出力エフェクトを使い、画像出力すると、深度マップの画像が得られるので、あとは、その画像を本プラグインにて選択すると、画像から、3D的な表示ができます。

Depth Estimate  
https://github.com/mes51/YMM_DepthEstimate

疑似被写界深度  
https://github.com/panko200/PseudoDepthofField

アイテム出力エフェクト  
https://github.com/tenkonta/YMM4-ImageOutputEffect

## アンインストール方法

1. YMM4 を起動して`ヘルプ(H)`>`その他`>`プラグインフォルダを開く`をクリックする。
2. YMM4 を終了する。
3. `Pseudo3DDisplacementMap`という名前のフォルダを削除する。

## 注意点

OS : Windows11 (64bit)  
ゆっくりMovieMaker4 : v4.51.0.1  
にて動作確認をしています。

他のSkiaSharpを使うプラグインと、使用するバージョンが違う場合、プラグインが競合する可能性があります。

作者は、本プラグインの使用または使用不能に起因するいかなる損害についても、一切の責任を負いません。

## アップデート内容

v0.1.0  
公開

## ライセンス

このプラグインは、以下のライブラリを使用しています。

SkiaSharp

- License: MIT License  
  [MIT License](./THIRD-PARTY-NOTICES.txt)

本プロジェクトは、MIT Licenseのもと公開しています。  
[MIT License](./LICENSE)
