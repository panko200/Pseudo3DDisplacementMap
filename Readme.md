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

## アンインストール方法

1. YMM4 を起動して`ヘルプ(H)`>`その他`>`プラグインフォルダを開く`をクリックする。
2. YMM4 を終了する。
3. `Pseudo3DDisplacementMap`という名前のフォルダを削除する。

## 注意点

OS : Windows11 (64bit)  
ゆっくりMovieMaker4 : v4.53.0.9  
にて動作確認をしています。

cam値を手前で渡してください。

他のSkiaSharpを使うプラグインと、使用するバージョンが違う場合、プラグインが競合する可能性があります。

作者は、本プラグインの使用または使用不能に起因するいかなる損害についても、一切の責任を負いません。

## アップデート内容

v0.1.0  
公開

v0.2.0  
深度推定を内蔵

## ライセンス

このプラグインは、以下のライブラリを使用しています。

Depth Anything V2のLicenseは、`./License`の中に入っています。

SkiaSharp

- License: MIT License  
  [MIT License](./THIRD-PARTY-NOTICES.txt)

本プロジェクトは、MIT Licenseのもと公開しています。  
[MIT License](./LICENSE)

## ビルド時の注意

Depth Anything V2 の ONNX モデル depth_anything_v2_vits_dynamic.onnx を下記リポジトリからダウンロード、プロジェクトのディレクトリ内に配置してください。  
https://github.com/fabio-sim/Depth-Anything-ONNX
