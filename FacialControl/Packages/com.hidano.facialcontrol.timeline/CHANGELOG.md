# Changelog

[Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) の形式に準拠し、[セマンティックバージョニング](https://semver.org/lang/ja/) に従う。

## [Unreleased]

Timeline 再生の手順を「REC → Export → Director にセット → Receiver を追加」の 4 手順に統合した（HID-144）。preview 段階の破壊的変更を含む。移行手順は README の「旧設定からの移行」を参照。

### Changed（破壊的変更）

- `RecToTimelineExporter.TryExportTimelineAsset` の署名から `director` / `receiver` 引数を撤去した。新しい署名は `(recordingPath, profileAsset, outputAssetPath, out result, existingTimeline = null, sourceKindOverrides = null)`。Export はシーン上の PlayableDirector / FacialTimelineReceiver に触れず、TimelineAsset 1 つ（Bake サブアセットと全 Facial トラックの Bake 参照を含む）で完結する
- `FacialTimelineReceiver.Configure(...)` と `BeginPlaybackSession(profile, timeline)` を撤去した。Receiver は binding から接続コンテキストを受け取り、`BeginPlaybackSession(timeline, director)` でトラック構成からレイヤーとチャネルを導出する
- Layer.inputSources の `timeline:{layer}:state` 宣言を非互換にした。宣言が残っていると Play は `LegacyStateDeclaration`（Error）で止まる。Receiver Inspector の「旧 timeline 宣言を削除」で除去する（Undo 可）。`timeline:{layer}` の値 sink 宣言は引き続き有効（削除は任意）
- `TimelineAdapterBinding` の Inspector 項目を Slug と Enabled だけにした。Target Layer Names / Channel Definitions は legacy フィールドとして読み込むが再生には使わず、残っていれば Play で 1 回警告する。`IGazeSourceProvider` の実装を外した
- `FacialTimelineBakeAsset` に Profile 内容ハッシュ（`ProfileContentHashHex`）を追加し、Source ハッシュを Timeline 構造 + Profile 内容ハッシュ + sampleRate で計算するようにした（seed を `facial-timeline-hash/v2` に更新）。既存の Bake は一度 `ProfileMismatch` / `BakeStale`（Warning）になり、Edit で自動再ベイクされる
- `TimelineBakeService.IsStale` の戻り値を `bool` から `BakeStaleReason`（`None` / `ProfileChanged` / `TimelineChanged`）に変えた
- `TimelineEditChangeWatcher` を静的クラスからインスタンスにし、`TimelineEditorServices.ChangeWatcher` 経由で使うようにした。Editor のイベント購読は `TimelineEditorServices` が一元管理し、`TimelineBakeDirtyWatcher` の `[InitializeOnLoad]` と自己購読を撤去した
- Analog 消費者（core の `AnalogExpressionInputSource` / `AnalogBlendShapeInputSource`）が registry の差し替えに追従するようになった（`IRegistryAttachableAnalogConsumer`）。InputSystem の analog expression 経路は registry へ接続済みになり、Timeline / REC の `Replace` が届く
- core の AutoExport（`FacialCharacterProfileAutoExporter`）は、内容が同じなら profile.json を書き直さない。Play のたびに `LastWriteTimeUtc` が更新されることはなくなった（タイムスタンプで変更検知している外部ツールのみ影響）
- Value トラックの `ChannelSubId` を REC の入力源 id（`slug:sub`）そのものとして扱い、その registry エントリを再生中だけ乗っ取るようにした。Timeline 独自の `timeline:{sub}` / `timeline:gaze-{n}` 入力源は登録しない

### Added

- REC Export がレイヤー weight（基準 kind 14 と時刻付き kind 12。UDP LipSync の発話ゲートなどが `FacialController.SetLayerWeight` で書く inter-layer weight）を、レイヤーごとの `FacialLayerWeightTrack`（トラック名 `{layer} (weight)`、階段カーブの `FacialLayerWeightClip`）として書き出すようにした（HID-182）。従来は読み捨てていたため、発話ゲートが開いていた区間（リップシンクだけで口を動かした区間）が Export した Timeline では再現されなかった。宣言値 1 のまま変わらないレイヤーはトラックにしない。入力源 weight（kind 13）は従来どおり読み捨て、Warning の件数は入力源 weight だけを数える
- レイヤー weight トラックの再生: Timeline にレイヤー weight トラックがあると、Receiver は再生中だけ live のレイヤー weight 書き込みを止め（REC 再生と同じ `IWeightInjectionGate`）、トラックの値を毎フレーム注入する。Clip の外とトラックの無いレイヤーは宣言値 1。停止時は再生前の weight に戻して live の書き込みを再開する。レイヤー weight トラックの無い Timeline の挙動は変わらない。Edit プレビューも同じ規則で合成に掛ける。レイヤー weight は Bake 済みのレイヤー値に再生時に掛かるため、Bake と Source ハッシュには含めない
- REC Export が値提供型（kind 7 / 基準 kind 8。iFacialMocap の BlendShape / UDP LipSync など）を入力源 id ごとの Value トラック（`FacialValueChannelKind.ValueProvider`）として書き出すようにした（HID-178）。基準を t=0 の状態とし、記録の差分を REC 再生と同じ規則で積み上げて、BlendShape ごとの値・寄与 mask・有効状態の階段カーブにする。BlendShape は REC に記録された録画時の BlendShape 名で、名前の記録が無ければ記録時の index で保存する（index 保存時は Export で 1 回 Warning）。Detected Channels に `ValueProvider（N 個の BlendShape）` と保存方法を表示する
- 値提供型トラックの再生: `ChannelSubId` の registry エントリを再生中だけ `TimelineValueProviderInputSource` で乗っ取り（Analog / Gaze と同じ占有規則）、停止時に戻す。Edit プレビューは Profile のレイヤー宣言どおりに同じ型の sink を合成する。Receiver の診断に値提供型領域（`ValueProviderTakeoverAttached` / `ValueProviderSourceNotFound` / `ValueProviderOccupied` / `ValueProviderBlendShapeMismatch` / `ValueProviderNotDeclared`）を追加した。乗っ取りはレイヤーの宣言スロットにだけ届く（後付け接続のスロットは置き換わらない。REC 再生と同じ）ため、宣言の無い値提供型チャネルは `ValueProviderNotDeclared` で知らせる
- REC Export が系1（kind 9 / 10）を読み捨てたとき、Export 1 回につき 1 回、件数付きの Warning を出すようにした（従来は無言で捨てていた）
- `FacialValueClip` に値提供型用の `BlendShapeNames` / `BlendShapeIndices` / `Contributes` / `Validity` を追加した。どれも持たない Clip（Analog / Gaze）の Source ハッシュは変わらない。値提供型トラックは Bake の `ValueBakes` に再サンプルしない（Clip の階段カーブを再生・プレビューが直接評価する）
- `FacialTimelineReceiver` の Director 自動解決（上書き → 同 GameObject → 親階層 → シーン走査）と、空の Facial トラック binding の自動設定
- 全 Facial トラックの Bake 参照の自動解決（`FacialTimelineBakeLocator`）。Receiver の BakeAsset 欄は上書き用になった
- 診断モデル（`FacialTimelineDiagnostics` / `TimelineDiagnosticCode`）と UI Toolkit の Receiver Inspector。Play を待たずに Director / Track binding / Bake / Profile / binding / レイヤー一致 / 配置を検査し、直し方を表示する。Console への出力は同じ原因につき 1 回
- Clip 編集・Undo・Profile 変更を 0.3 秒デバウンスで合流させる自動再ベイク
- Edit プレビューを Play と同じレイヤー合成規則で描く `TimelinePreviewCompositor`。Gaze チャネルはトラック順ではなくチャネル id で解決する
- Edit プレビューに Analog チャネル経由の出力（analog expression）を反映した。Profile の binding が `IAnalogExpressionBindingDeclaration` で宣言する構成から Play と同じ消費者をオフラインに組み、Analog Value トラックの値で駆動する（HID-148）
- REC Export ウィンドウに、入力源 id ごとのチャネル種別の判定結果と理由、Export 後の残り手順を表示した
- 4 手順の end-to-end PlayMode テスト。GC ゼロ gate の計測を「GC Allocated In Frame」カウンタに変え、計測器の自己検証テストを追加した（従来の計測は同期テストで確保を検出できていなかった）

### Fixed

- REC Export で値提供型（iFacialMocap 等）の BlendShape が 1 つ前にずれて動くことがあった。Profile の参照モデルから BlendShape 名を集め、名前数と記録の mask バイト数（8 個単位）が合えば一致とみなしていたため、録画時と BlendShape が 1 つ違うモデルでも名前が 1 つずつずれて割り当てられていた。Export は REC に記録された録画時の BlendShape 名だけを使い、参照モデルから推測しないようにした（HID-180）
- Edit プレビューを解除しても BlendShape が変形したまま戻らなかった。プレビュー開始時に登録する復元対象を `FacialController.SkinnedMeshRenderers`（手動オーバーライド欄）からしか集めておらず、Edit 中で空のときは何も登録していなかったため、プレビューが書き込むのと同じ renderer（手動オーバーライド → 子の SkinnedMeshRenderer）を登録するようにした（HID-181）
- Edit プレビューが非アクティブな子の SkinnedMeshRenderer も集めていたため、Play（アクティブな子だけを集める）と BlendShape の並びがずれ、値提供型を index で再生するトラックが別の BlendShape を動かすことがあった。プレビューもアクティブな子だけを集めるようにした（HID-181）

### Removed

- REC Export ウィンドウの Director / Receiver 指定欄と Source Overrides（Auto / Analog / Gaze）
- Play 終了時の再ベイク確認ダイアログ。Play 中に検出した不一致は Edit 復帰時に無言で修復し、Console に Info を 1 行出す

### Known Issues

- Edit プレビューの Analog は、`IAnalogExpressionBindingDeclaration` を実装した binding（InputSystem）の analog expression だけを反映する
- InputSystem 以外で registry を購読しない独自の analog 消費者には、Timeline の Analog が届かない場合がある

### Fixed

- Adapter Bindings のヘッダーのトグルで Timeline binding ごと無効にした（`AdapterBindingBase.Disabled`）場合、静的診断・Receiver のセッション開始とも Enabled オフと同じ `BindingDisabled` を出す（`BindingMissing` にしない）。無効の binding の後ろに有効な Timeline binding があれば有効な方で診断する。Edit プレビューは無効の binding の Analog Expression を合成しない（Play と揃える）
- State sink の ContributeMask 長が 0 で、レイヤー入力源に接続すると Aggregator が `ArgumentException` を投げていた
- weight のレコード kind（12〜15。`com.hidano.facialcontrol.rec` の HID-80 で追加）を含む `.fcrec` を REC Export に通すと `InvalidOperationException`（Unsupported REC event kind）で失敗していた。weight は Export 対象外として読み捨て、時刻付き weight イベントがあれば Export 1 回につき 1 回だけ件数付きの Warning を出す（HID-80）

- 目ボーン path が空の Gaze チャネルで、Timeline のスクラブプレビュー中に目が動かなかった。path が空の側は Humanoid Avatar の `LeftEye` / `RightEye` を使い、rest 回転と yaw / pitch 軸はプレビュー開始時の姿勢から導出する。fallback を使うのは目ごとに先頭の（ベイク値のある）channel だけで、path 指定の channel が同じボーンを指せば path 側を優先する（HID-41 のランタイム fallback と同じ規則）
- スクラブプレビューの目ボーン path の解決をランタイムと同じ `BoneTransformResolver` にした（ボーン名だけの指定・末尾一致も解決する。従来は root からの相対 path のみ）
- 目ボーン path が空のとき、スクラブプレビューがキャラクター root の回転を復元対象として登録していた。空・空白の path では `Transform.Find` を呼ばない

## [1.0.0] - 2026-09-25

初回リリース。

### Added

- `TimelineAdapterBinding`（Adapter Bindings の "Timeline"、既定 slug `timeline`）— レイヤーごとの状態 / ベイク値 sink と、アナログ / Gaze チャネルの入力源を登録する
- `FacialExpressionTrack` / `FacialExpressionClip` — トラック名をレイヤー名として Expression の on/off を区間で駆動。子トラックは同一レイヤーの追加レーン
- `FacialValueTrack` / `FacialValueClip` — `AnimationCurve` でアナログ / Gaze の各軸を駆動。Gaze は live 入力源を再生中だけ乗っ取る
- `FacialTimelineReceiver` — Mixer と sink を仲介する MonoBehaviour。ベイク欠落 / ハッシュ不一致時の劣化動作と Editor への通知
- `FacialTimelineBakeAsset` と `TimelineBakeService` — TimelineAsset のサブアセットへ BlendShape 値と状態イベントをベイク。`TimelineBakeDirtyWatcher` が保存時 / Play 突入時に自動再ベイク
- Edit モードのスクラブでベイク結果を SkinnedMeshRenderer と目ボーンへ反映するプレビュー
- `RecToTimelineExporter` と **Tools → FacialControl → Timeline → REC Export** ウィンドウ — `.fcrec` を Expression クリップと Value トラックへ変換
- `FacialTimelineValidator` と Track / Clip の Custom Editor — Expression id 欠落・Gaze 範囲外・空クリップ・空親トラックを Timeline 上で警告
- 定常再生 / スクラブの GC アロケーション 0 gate と、Timeline 再生と live 入力の post-blend 等価性を検証する PlayMode テスト
