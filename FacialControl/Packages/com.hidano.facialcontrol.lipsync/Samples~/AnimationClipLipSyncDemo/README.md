# AnimationClipLipSyncDemo

`ULipSyncAdapterBinding` の音素エントリに、BlendShape 形式と AnimationClip 形式を混在させるサンプル。

## 使い方

1. Package Manager から `AnimationClipLipSyncDemo` を Import する
2. `Scenes/AnimationClipLipSyncDemo.unity` を開く
3. `Character` GameObject の下へ自分のモデルを配置する
4. モデルの口メッシュ GameObject 名が `Body` でない場合は、`Animations/` 内の I / U / O 用 AnimationClip の curve binding（renderer path）をモデルに合わせる。AnimationClip 形式は Host GameObject 配下の renderer path と curve が一致しないと snapshot が空になる
5. BlendShape 名が異なる場合は、A / E の `BlendShapeName` と、AnimationClip の BlendShape カーブ名を合わせる
6. **Adapter Bindings → uLipSync** の入力デバイスを選び、Play

## 構成

- slot `a / i / u / e / o` を宣言し、`overlay` レイヤーに `overlay:{slot}` と `lipsync-overlay:{slot}` を並べた Profile
- A / E は BlendShape 形式、I / U / O は AnimationClip 形式。AnimationClip 形式は Clip の終端時刻（`length` が 0 なら先頭）の BlendShape 値だけを初期化時にサンプリングし、時間軸は再生しない
- Analyzer Profile は同梱しない。未指定のまま再生すると、パッケージ同梱の既定 Profile が `Resources.Load` で使われる
