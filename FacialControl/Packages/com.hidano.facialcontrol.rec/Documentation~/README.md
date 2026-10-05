# com.hidano.facialcontrol.rec ドキュメント

基本操作は [パッケージ README](../README.md) を参照してください。ここでは `.fcrec` のバイナリ形式、入力源の分類、再生時の遮断契約を記載します。

## `.fcrec` バイナリ形式

すべて little-endian です。定義は `Runtime/Domain/Services/RecBinaryFormat.cs` にあります。

| 区画 | 内容 |
|---|---|
| ヘッダ (16 byte) | magic `FREC` / `formatVersion` (`ushort`, 1) / `flags` (`ushort`) / `startedAtUnixMilliseconds` (`int64`) |
| レコード列 | `IdDefine` → baseline → 時刻付きイベント |
| Footer (13 byte) | `duration` (`double`) / `recordCount` (`uint32`) |

`flags` の bit0（`RecHeaderFlags.FullInputBaseline = 0x0001`）と bit1（`RecHeaderFlags.WeightBaseline = 0x0002`）は必須です。writer は常に設定し、reader は欠落したヘッダをエラーにします。`formatVersion` は 1 据え置きです。本変更以前の構造で書かれたファイルは互換・移行の対象外であり、必須 flags 欠落として拒否されます。再収録してください。

### レコード kind

| kind | 名称 | 説明 |
|---:|---|---|
| 1 | `IdDefine` | source / Expression id の定義 |
| 2 / 3 | `TriggerOn` / `TriggerOff` | 入力トリガーの on/off |
| 4 | `AnalogSample` | アナログ・gaze の軸値 |
| 5 / 6 | `BaselineTrigger` / `BaselineAnalog` | 開始時の trigger / analog 基準 |
| 7 | `ValueProviderSample` | 値提供型の時刻付き疎値 |
| 8 | `BaselineValueProvider` | 値提供型の開始時基準 |
| 9 / 10 | `ExpressionActivate` / `ExpressionDeactivate` | 系1 の Expression 操作 |
| 11 | `BaselineExpression` | 系1 の開始時基準 |
| 12 | `LayerWeightSample` | レイヤー weight の時刻付き値 |
| 13 | `InputSourceWeightSample` | 入力源 weight の時刻付き値 |
| 14 | `BaselineLayerWeight` | レイヤー weight の開始時基準 |
| 15 | `BaselineInputSourceWeight` | 入力源 weight の開始時基準 |
| 255 | `Footer` | duration と record count |

値提供型は `ValueCount` と mask byte 列を持ちます。mask は byte 列の LSB-first 表現で、値は mask の立っている index 順に疎に格納されます。mask 外の非ゼロ値はファイルに保存されず、再生でも再現されません。

## 記録対象と明示的除外

観測対象はトリガー、アナログ・gaze、値提供型、系1 の 4 系統です。入力源分類カタログと 1:1 に対応する明示的除外は次の 7 型です。

| 型 | 区分 | 理由 |
|---|---|---|
| `TimelineGazeInputSource` | InjectionSource | 他注入者の注入ソースで、REC と排他 |
| `RecPlaybackAnalogSource` | InjectionSource | REC の再生注入用内部ソース |
| `RecPlaybackValueProviderSource` | InjectionSource | REC の値提供型再生注入用内部ソース |
| `InputActionAnalogSource` | WrappedByObservedSource | 観測対象 wrapper 経由で registry に登録される |
| `ArKitOscAnalogSource` | NotRegisteredAtRuntime | runtime registry に登録されない |
| `OscFloatAnalogSource` | NotRegisteredAtRuntime | runtime の合成パイプラインに到達しない |
| `OfflineExpressionSource` | EditorOnly | Editor ベイク専用で runtime に登録されない |

## 再生中の入力遮断

遮断対象は再生開始時の registry スナップショットで決まります。

### トリガー

対象 source を suspend し、baseline の stack を確立してから REC の on/off を注入します。停止時に suspend を解除します。

### アナログ・gaze

baseline 値を seed にした再生用 source に置換し、その他の source は 0 seed にします。停止時は元の source を復元します。

### 値提供型

`BaselineValueProvider` の有効状態、mask、疎な値を再生用 source に設定します。無効な値提供型も無効 seed として置換するため、再生中に live の値提供型が混入しません。

### 系1

`ExpressionUseCase` を Suspend し、`BaselineExpression` から基準を確立してから REC の `ExpressionActivate` / `ExpressionDeactivate` を注入します。

### weight

ライブの weight 書込を遮断し、宣言値へリセットしてから基準を確立し、REC の layer weight / input-source weight を注入します。停止時は停止時点の値を維持し、その後のライブ書込を受け付けます。

## 保存先

既定の保存先は `StreamingAssets/FacialControl/{キャラクター名}/recordings/{名前}.fcrec` です。同名の場合は `{名前}-2` 以降になります。

## 既知制限

- 開始時スナップショット方式は値提供型・系1・weight のスロットにも適用されます。再生開始後に登録された入力源は遮断対象外です。
- プロファイル内のレイヤー名は一意であることが前提です。
- 値提供型の mask 外非ゼロ値は記録・再現されません。
- 値提供型の基準捕捉は `StartRecording` の Update 時点の読取です。同一フレームの LateUpdate までに届いた値は t≈0 のイベントとして記録されます。
- 再生中の記録には REC の注入イベント（系1 を含む）が残ります。

Timeline REC Export は weight kind 12〜15 に未対応です。timeline トラック側の合流後に follow-up として対応します。
