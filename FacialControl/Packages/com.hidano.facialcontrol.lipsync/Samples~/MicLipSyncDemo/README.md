# MicLipSyncDemo

`ULipSyncAdapterBinding` をマイク入力で使う最小サンプル。

## 使い方

1. Package Manager から `MicLipSyncDemo` を Import する
2. `Scenes/MicLipSyncDemo.unity` を開く
3. `Character` GameObject の下へ自分のモデルを配置する
4. `Profiles/MicLipSyncDemoProfile.asset` の **Adapter Bindings → uLipSync** で、入力デバイスのポップアップから使用するマイクを選ぶ（選択は PlayerPrefs に保存される。空のままなら先頭のマイクが使われる）
5. モデルの口 BlendShape 名が `A / I / U / E / O` でない場合は、各音素エントリの `BlendShapeName` を合わせる。口形を複数 BlendShape で作りたい場合は Profile に口形 Expression を登録し、エントリ形式を Expression に切り替える
6. Play

## 構成

- slot `a / i / u / e / o` を宣言し、`overlay` レイヤーに `overlay:{slot}` と `lipsync-overlay:{slot}` を並べた Profile
- 音素エントリは BlendShape 形式（`A`〜`O`）
- Expression `smile` は slot `a` を Override し、笑顔中の「あ」の口形状を `Face` の BlendShape `A_smile` に差し替える例。モデルにその BlendShape が無ければ Override を Default に戻すか、名前を合わせる
- Scene 上の `Character` には `FacialController` と `Animator` だけが付いており、`uLipSync` / `uLipSyncMicrophone` / `AudioSource` は再生時に binding が動的に追加し、停止時に取り外す
- Analyzer Profile は同梱しない。未指定のまま再生すると、パッケージ同梱の既定 Profile が `Resources.Load` で使われる
