# FacialControl uLipSync Adapter

`com.hidano.facialcontrol` と `com.hidano.ulipsync-asio`（uLipSync の ASIO 対応フォーク）を橋渡しする Windows 向けアダプタ。uLipSync の音素解析結果（A / I / U / E / O の比率と音量）を、FacialControl の **音素 Overlay slot** への入力源に変換する。キャラクター Prefab に uLipSync 系コンポーネントを事前に付ける必要はなく、再生時に binding が動的に構築・破棄する。

## 対応範囲

- Unity 6000.3 以降
- Windows 64bit Standalone と Editor（Runtime asmdef の `includePlatforms`）。uLipSync の Windows アセンブリと NAudio（ASIO）に依存するため
- `com.hidano.facialcontrol` 1.0.0、`com.hidano.ulipsync-asio` 3.1.5-custom.2

## 提供する binding

**uLipSync**（`ULipSyncAdapterBinding`、既定 slug `ulipsync`）

| 設定 | 内容 |
|---|---|
| **入力デバイス** | マイクまたは ASIO ドライバ名。PlayerPrefs に保存され、アセットには残らない。空なら先頭のマイクにフォールバック |
| **Analyzer Profile** | uLipSync の解析 Profile。未指定なら同梱の既定 Profile（`Resources/FacialControl/LipSync/Default uLipSync Profile`）を使う |
| **音素エントリ** | 音素ごとの口形状。形式は Expression / AnimationClip / BlendShape から選ぶ（新規追加時は A〜O の Expression 形式 5 件がプリセットされる） |
| **Max Weight Scale** | 全エントリの MaxWeight に掛ける倍率 |
| **Voice Gate** | 発話ゲート（下記「iFacialMocap との相互排他」）。On / Off / Hold / Attack / Release / Stale Timeout |
| **Suppress Blend Shape Names** | 発話中に 0 で押さえ込む BlendShape 名（キャプチャの口まわり） |

登録する入力源 id は固定 prefix `lipsync-overlay:{a|i|u|e|o}`。Profile の **Slots** に `a / i / u / e / o` が宣言されている slot だけ登録される（表情ライブラリタブの **Phoneme slots を初期化** ボタンで一括宣言できる）。binding を Add すると `overlay` レイヤー（`blend`、`overlay:{slot}` と `lipsync-overlay:{slot}` の 10 入力源）が自動追加される。

## 使い方

1. `com.hidano.facialcontrol.lipsync` を追加する（`com.hidano.ulipsync-asio` は依存として解決される）
2. `FacialCharacterProfileSO` の表情ライブラリタブで **Phoneme slots を初期化** を押し、slot `a / i / u / e / o` を宣言
3. **Adapter Bindings** で **uLipSync** を Add。入力デバイスを選び、音素エントリの口形状を設定する
   - **Expression 形式**（推奨）: Profile に登録した口形 Expression を参照。id か名前が A〜O と一致すれば自動リンク
   - **AnimationClip 形式**: Clip の **終端**（`length` が 0 なら先頭）の BlendShape 値をサンプリング
   - **BlendShape 形式**: BlendShape 名 1 つと最大 weight
4. Play。binding が `AudioSource` / `uLipSync` / マイクまたは ASIO 入力を Host GameObject に追加し、停止時に取り外す

表情側で音素の口形状を差し替えたい場合は、各 Expression の **Overlays** で slot `a〜o` を Override（専用の口形状）または Suppress（口を動かさない）にする。優先順位は Expression の Override / Suppress → Default Overlays → uLipSync の既定出力。Override 中も駆動 weight（音素比率 × 音量）は同じなので、無音時に口が開いたままになることはない。

## iFacialMocap との相互排他（発話ゲート）

iFacialMocap などのフェイシャルキャプチャと同じキャラクターで併用すると、無言の間はキャプチャの口（jawOpen / mouth\*）をそのまま出し、発話中だけ口の開閉・形状を uLipSync が置き換える。目・眉・頬・笑顔はキャプチャのまま。

推奨構成:

| レイヤー | priority | inputSources |
|---|---|---|
| `capture` | 1 | iFacialMocap Receiver の slug |
| `lipsync` | 2 | `lipsync-overlay:a` 〜 `lipsync-overlay:o` のみ |

`lipsync` レイヤーには他の入力源を入れない（ゲートがレイヤー weight を書き換えるため一緒に消える）。OSC Receiver の対象レイヤーを未指定にすると先頭レイヤーへ補われるので、`lipsync` を先頭にする場合は対象レイヤーを `capture` に指定する。priority は同値にしない。binding 追加時に自動で作られる `overlay` レイヤーのように他の入力源（`overlay:{slot}` 等）と同居するレイヤーは weight を書かず、入力源の有効 / 無効だけで制御する（Attack / Release は効かない）。

- **発話の判定**: uLipSync の正規化済み音量（activity、0〜1）が `On` 以上で発話開始。発話中は `Off` 以上の間続き、下回った状態が `Hold` 秒続くと発話終了。`Off` が `On` より大きい設定は `On` と同じ扱い。しきい値 0 は `activity > 0` で判定する
- **gate weight**: 発話中は 1、無言は 0 へ、`Attack` / `Release` 秒で線形に近づく（0 以下なら即時）。uLipSync の入力源だけを宣言した全レイヤーの weight に毎フレーム書く。weight 0 の間は入力源も止めて下位レイヤーをそのまま通す
- **途絶**: uLipSync のコールバックが `Stale Timeout` 秒届かなければ activity 0 として閉じる（マイク停止・デバイス抜けで口が開いたまま残らない。0 以下で無効）
- **口まわりの押さえ込み**: `Suppress Blend Shape Names` の BlendShape には発話中（gate weight > 0）に 0 を書く（母音側が同じ BlendShape を動かす場合はその値）。**フェイシャルキャプチャの口まわりを追加** ボタンで ARKit 52 の口の開閉・形状系 21 個（`jawOpen` `jawForward` `jawLeft` `jawRight` `mouthClose` `mouthFunnel` `mouthPucker` `mouthLeft` `mouthRight` `mouthRoll*` `mouthShrug*` `mouthPress*` `mouthLowerDown*` `mouthUpperUp*` `mouthStretch*`）を追加する。`mouthSmile*` / `mouthFrown*` / `mouthDimple*` は感情表現としてキャプチャに残す。iFacialMocap Receiver の Mappings を上書きしている場合は、口まわりのキャプチャ名に割り当てた反映先も追加する。参照モデルがあればメッシュにある名前だけ追加する。既存の項目は残し、重複は追加しない。メッシュに無い名前は起動時に警告する
- **OFF にした場合**: 入力源は常に有効（従来挙動）。実行中に OFF にするとレイヤー weight を一度 1 に戻す。Suppress Blend Shape Names は OFF でも常に 0 を書く（UDP LipSync と同じ。従来挙動にするなら空にする）
- **Inspector の警告**: uLipSync の入力源を宣言したレイヤーが無い、他の入力源と同居している、iFacialMocap Receiver / OSC Receiver を宣言したレイヤーの priority が uLipSync のレイヤー（複数あれば最大値）以上、のときに binding の下に表示する
- **Live Monitor**: binding Inspector 下部の Foldout に、Play 中の activity / Speaking / weight / 対象レイヤーを表示する。スクリプトからは `VoiceGate` / `GateLayerNames` で参照できる

| 設定 | 既定値 |
|---|---|
| Voice Gate Enabled | ON |
| Voice On Threshold | 0.05 |
| Voice Off Threshold | 0.02 |
| Voice Hold Time | 0.2 秒 |
| Voice Attack Time | 0.05 秒 |
| Voice Release Time | 0.2 秒 |
| Voice Stale Timeout | 0.5 秒 |

## 実行時の挙動

- 音量の正規化・スムージングは uLipSync 本体に委ね、本パッケージでは再加工しない。声が小さい場合は uLipSync 側（マイク gain / Profile）で調整する
- `SwapDevice(deviceName, disambiguatorIndex)` で再生中にデバイスを切り替えられる。切替時は 1 フレームゼロ出力を挟んで入力コンポーネントを作り直す
- 同名デバイスが複数ある場合は Disambiguator（0 始まりの序数）で区別する。デバイス名が見つからない場合はエラーを出して起動を中止する（フォールバックは空文字のときだけ）
- 音素比率の受信・スナップショット合成・値書き込みのホットパスは GC アロケーション 0

## サンプル

| Sample | 内容 |
|---|---|
| `MicLipSyncDemo` | マイク入力の最小構成。BlendShape 形式のエントリで A〜O を直接指定 |
| `AnimationClipLipSyncDemo` | BlendShape 形式と AnimationClip 形式の混在例 |

## ドキュメント

- [使い方の詳細](Documentation~/usage.md) — slot 宣言、Override / Suppress、ASIO の手順
- [音素エントリ形式ガイド](Documentation~/phoneme-entry-format-guide.md)
- [手結線 uLipSync からの移行](Documentation~/migration-guide.md)

## ライセンス

[MIT License](LICENSE.md)
