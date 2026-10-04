# FacialControl REC

`com.hidano.facialcontrol.rec` は、`FacialController` に届く入力を `.fcrec` に記録し、同じ入力状態を再生するパッケージです。再生中は記録開始時点で存在した入力源を遮断し、記録値を注入します。

## 依存パッケージ

| パッケージ | 用途 |
|---|---|
| `com.hidano.facialcontrol` | 入力観測バスと入力源レジストリ |

## 使い方

`FacialController` と同じ GameObject に `RecCharacterBinding` を追加し、Play 中に Inspector の **Start Recording** / **Stop Recording**、または `Load Recording` / **Start Playback** を使用します。スクリプトからは次の API を呼び出せます。

```csharp
var rec = GetComponent<RecCharacterBinding>();
rec.StartRecording("take01");
rec.StopRecording();
rec.LoadRecording("take01");
rec.StartPlayback();
rec.StopPlayback();
```

## 記録される内容

REC は次の 4 系統を、入力源の消費点で記録します。

| 系統 | 記録内容 |
|---|---|
| トリガー | 入力源 id と Expression id の on/off、および系1の Expression の activate/deactivate |
| アナログ・gaze | 入力源 id と軸ごとの float 値。gaze の Vector2 を含む |
| 値提供型 | `TryWriteValues` が提供した値、`ContributeMask`、有効状態。BlendShape 等の疎な値は mask の立っている index 順で記録 |
| 系1 | `ExpressionUseCase` / `FacialController.Activate` 経由の Expression 操作 |

明示的除外は、入力源分類カタログと同じ次の 7 型です。これらは観測対象のレコードとして扱いません。

| 型 | 除外区分 | 理由 |
|---|---|---|
| `Hidano.FacialControl.Timeline.Adapters.InputSources.TimelineGazeInputSource` | InjectionSource | `FacialTimelineReceiver` の注入ソース。注入者の占有規則により REC と排他 |
| `Hidano.FacialControl.Rec.Adapters.Playback.RecPlaybackAnalogSource` | InjectionSource | REC 自身の再生注入用内部ソース |
| `Hidano.FacialControl.Rec.Adapters.Playback.RecPlaybackValueProviderSource` | InjectionSource | REC 自身の値提供型再生注入用内部ソース |
| `Hidano.FacialControl.Adapters.InputSources.InputActionAnalogSource` | WrappedByObservedSource | registry には観測対象の wrapper 経由で登録され、元型を直接記録しない |
| `Hidano.FacialControl.Adapters.InputSources.ArKitOscAnalogSource` | NotRegisteredAtRuntime | Adapter が公開する値であり、runtime の registry に登録されない |
| `Hidano.FacialControl.Adapters.InputSources.OscFloatAnalogSource` | NotRegisteredAtRuntime | runtime / Editor の合成パイプラインに到達しない |
| `Hidano.FacialControl.Timeline.Editor.BakeSimulationHarness+OfflineExpressionSource` | EditorOnly | Editor のベイク専用で、runtime の registry に登録されない |

## ファイル形式

`.fcrec` は little-endian のバイナリ形式で、`formatVersion` は **1 のまま**です。ヘッダの `flags` bit0（`FullInputBaseline`）は必須で、writer は常に `1` を書き、reader は bit0 がないファイルを読込エラーにします。

本変更以前の構造で書かれたファイルには読込互換・移行を提供しません。`flags` bit0 がないため拒否され、再収録が必要です。kind 7〜11 の追加やヘッダ flags の変更があっても formatVersion は 1 です。

| kind | 名称 | 内容 |
|---:|---|---|
| 1 | `IdDefine` | source / Expression id の定義 |
| 2 / 3 | `TriggerOn` / `TriggerOff` | トリガーイベント |
| 4 | `AnalogSample` | アナログ・gaze の時刻付き値 |
| 5 / 6 | `BaselineTrigger` / `BaselineAnalog` | 開始時スナップショット |
| 7 | `ValueProviderSample` | 値提供型の時刻付き値 |
| 8 | `BaselineValueProvider` | 値提供型の開始時スナップショット |
| 9 / 10 | `ExpressionActivate` / `ExpressionDeactivate` | 系1 の時刻付き操作 |
| 11 | `BaselineExpression` | 系1 の開始時スナップショット |
| 255 | `Footer` | duration と record count |

値提供型の record は、全 BlendShape の dense 配列ではなく、mask の立った index の値だけを保存する。mask は byte 列の LSB-first の疎な表現で、mask 外の非ゼロ値は記録・再現しません。

## 再生中の入力遮断

再生開始時に registry の入力源をスナップショットし、記録対象の各系統を遮断します。

- トリガー: suspend して baseline を復元し、REC の on/off を注入
- アナログ・gaze: baseline 値を seed にした再生用 source に置換（その他は 0 seed）
- 値提供型: baseline の有効状態・mask・値を再生用 source に設定し、無効 seed で live 値を遮断
- 系1: `ExpressionUseCase` を Suspend し、baseline を確立してから REC の activate/deactivate を注入

## 既知制限

1. レイヤー weight / 入力源 weight のランタイム変更は記録も遮断もされません（Linear HID-80）。`FacialController.SetLayerWeight` と `LayerUseCase.SetInputSourceWeight` の変更は再生中もライブです。
2. 開始時スナップショット方式のため、再生開始後に新規登録された入力源（値提供型・系1 を含む）は遮断対象外です。
3. 値提供型の `ContributeMask` 外に書かれた非ゼロ値は記録・再現されません。
4. 値提供型の基準捕捉は `StartRecording` の Update 時点の読取です。同一フレームの LateUpdate までに届いた値は t≈0 のイベントになります。
5. 再生中に記録すると、REC の注入イベント（系1 を含む）も記録されます。

## 構成

`Runtime/` に Domain / Application / Adapters、`Editor/` に Inspector、`Tests/` に EditMode / PlayMode テスト、`Documentation~/` に詳細ドキュメントを配置しています。Timeline への書き出しは `com.hidano.facialcontrol.timeline` の **Tools → FacialControl → Timeline → REC Export** を使用します。

## ライセンス

[MIT License](LICENSE.md)
