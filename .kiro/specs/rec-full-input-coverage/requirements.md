# Requirements Document

## Project Description (Input)
Linear HID-35: REC の記録・遮断対象を FacialControl で動く全入力に拡張する。現状 RecordingUseCase の記録入口は AnalogObservationSampler（IAnalogInputSource のみ）と ExpressionTriggerInputSourceBase のトリガーフックの 2 種だけで、OSC 受信 BlendShape（OscInputSource）・iFacialMocap 受信 BlendShape・uLipSync 音素オーバーレイ（LipSyncPhonemeOverlayInputSource）・Timeline ベイク値（TimelineBakedValueSink）の ValueProviderInputSourceBase 系と、FacialController.Activate/Deactivate 直呼びが未記録・再生時に未遮断。core の観測面と REC の RecAnalogInjector/RecTriggerInjector 相当をこれら全てに拡張し、全パッケージの IInputSource 実装を reflection で列挙して観測対象か明示的除外のどちらかに分類されていなければ失敗する網羅性ゲートテストを追加する。OverlayInputSource は派生値として除外要否を設計で判断。.fcrec formatVersion 変更の要否も設計で判断（AnalogSample は 255 軸まで）。rec-recording-playback/design.md の記述も実装に合わせて修正する。方針は『FacialControl で動く入力は例外なく全て REC 対象』で部分対応は不可。1.0.0 は未リリースなので後方互換の制約は無い。

## Introduction

本機能は、REC（`com.hidano.facialcontrol.rec`）の記録・再生・ライブ遮断の対象を、FacialControl の表情パイプラインを動かす**全ての入力経路**へ拡張する。

現状の REC は、core の観測面（`IFacialInputObservationBus`）へ流れ込む 2 種類の入口 — `ExpressionTriggerInputSourceBase` のトリガーフック（TriggerOn/Off）と `AnalogObservationSampler` による `IAnalogInputSource` の pull サンプリング — だけを記録し、再生時は `RecTriggerInjector` / `RecAnalogInjector` がこれら 2 種類だけを遮断・注入する。その結果、次の入力は記録されず、再生中も遮断されない。

- `ValueProviderInputSourceBase` 派生の BlendShape 値提供型入力源: `OscInputSource`（OSC 受信 BlendShape。`OscReceiverAdapterBinding` と `IFacialMocapReceiverAdapterBinding` が使用）、`LipSyncPhonemeOverlayInputSource`（uLipSync 音素オーバーレイ）、`TimelineBakedValueSink`（Timeline ベイク値）
- スクリプト / uGUI からの `FacialController.Activate(Expression)` / `Deactivate(Expression)` 直呼び（`ExpressionUseCase` へ直行する系1 経路。観測フックが存在しない）

本 spec の方針は「**FacialControl システムで動くものは例外なく全て REC 対象にする。半端な未対応は許容しない**」である（Linear HID-35 で決定）。この方針を、(1) 全 `IInputSource` 実装および `IInputSource` を実装しない `IAnalogInputSource` 単独実装（Runtime / Editor アセンブリの両方）を「観測対象」または「理由付きの明示的除外」のいずれかに分類し、分類漏れを reflection ベースの網羅性ゲートテストで機械的に検出すること、(2) 観測対象に分類された全入力について記録・基準状態捕捉・再生時遮断・注入・`.fcrec` ラウンドトリップの全てを成立させること、の 2 点で担保する。1.0.0 は未リリースのため、`.fcrec` フォーマットの後方互換は制約としない（`formatVersion` は 1 のまま据え置き、記録構造の変更は版分岐なしで行う）。

ランタイムのレイヤー weight / 入力源 weight 変更（inputsystem の overlay binding が駆動する `FacialController.SetLayerWeight`、`LayerUseCase.SetInputSourceWeight`）は、初期には本 spec の対象外として Linear **HID-80**（REC: record and block runtime layer / input-source weight changes）へ分離していた。`rec-weight-coverage` により上書きされ、現在はこの経路も REC の記録・遮断・注入対象であり、既知制限ではない。

既存 spec との関係: rec-recording-playback（観測面・注入面・`.fcrec` の契約オーナー）と rec-playback-input-exclusivity（ライブ遮断面・排他ライフサイクルのオーナー）が確立した語彙と設計判断（観測面 / 注入 / live 遮断 / 基準状態 / 占有規則 / 開始時スナップショット方式 / StopPlayback を唯一の解放点とする）を継承し、その**対象範囲**を全入力へ拡張する。両 spec の文書のうち、本 spec の方針と矛盾する記述（例: rec-recording-playback design.md の「リップシンク由来の操作イベントは他入力と同様に観測面経由で記録される」「系1 経路の記録は Out of Boundary」）は本 spec で上書き・修正する。

## Boundary Context

- **対象 Unity プロジェクト**: `FacialControl/`（リポジトリ直下。単一 Unity プロジェクト）。変更対象パッケージは `FacialControl/Packages/com.hidano.facialcontrol`（core: 観測面・遮断面・注入面）と `FacialControl/Packages/com.hidano.facialcontrol.rec`（記録・再生・永続化・網羅性ゲートテスト）。網羅性ゲートテストの列挙対象は `FacialControl/Packages/com.hidano.facialcontrol*` の全パッケージ（core / osc / inputsystem / lipsync / ifacialmocap / rec / timeline / expression-creator / routing-editor）の **Runtime と Editor の両アセンブリ**（テストアセンブリは除外）。列挙する型は具象 `IInputSource` 実装に加え、`IInputSource` を実装しない具象 `IAnalogInputSource` 実装（`InputActionAnalogSource` / `ArKitOscAnalogSource` / `OscFloatAnalogSource` 等）も含む。Editor アセンブリの実装（timeline Editor の `OfflineExpressionSource` 等）も列挙対象であり、観測対象または明示的除外（例: Editor のベイクシミュレーション用ソースとして除外）に分類する。Requirement 3.9 / 8.4 により osc パッケージ（`FacialControl/Packages/com.hidano.facialcontrol.osc`）の受信 binding も改修対象に含む
- **In scope**: `ValueProviderInputSourceBase` 派生入力源の記録・基準状態捕捉・再生時遮断・注入、`FacialController.Activate/Deactivate`（系1 経路）の記録・基準状態捕捉・再生時遮断・注入、全 `IInputSource` 実装および `IAnalogInputSource` 単独実装（Runtime / Editor）の観測対象 / 明示的除外への分類とその文書化、分類漏れを検出する網羅性ゲートテスト、新たに記録対象となる値の `.fcrec` ラウンドトリップ（`formatVersion` 1 据え置きでの記録構造の変更、新 kind の途中再生ベースライン畳み込み、timeline パッケージ REC Export の新 kind を含むファイルの継続読取）、osc パッケージの受信 binding におけるマッピング集合変化時の registry エントリ入れ替え（Replace）の廃止（既存 `OscInputSource` の in-place 更新化）、既存 spec 文書（rec-recording-playback design.md 等）と rec / timeline パッケージドキュメントの記述修正
- **Out of scope（初期定義。`rec-weight-coverage` により上書き）**: ランタイムのレイヤー weight / 入力源 weight 変更の記録・遮断（inputsystem の overlay binding が `InputActionAnalogSource` 直参照で駆動する `FacialController.SetLayerWeight`、および `LayerUseCase.SetInputSourceWeight`。Linear **HID-80** へ分離していた初期定義）。`rec-weight-coverage` によりこの経路も記録・遮断・注入の対象となり、現在の既知制限ではない。その他の除外は、音声解析・音声波形の記録（リップシンクは `LipSyncPhonemeOverlayInputSource` が合成パイプラインへ供給する BlendShape 値を記録対象とし、音声そのものは扱わない）、Timeline 独自 Track のベイク機能・スクラブ（rec-timeline-baking 系の責務）、ランタイム UI、記録セッション中の `SetProfile` / `LoadCharacter` 再初期化を跨ぐ完全な記録保証（既存 spec の既知制限を継承）、再生中に新規登録された入力源の遮断（開始時スナップショット方式の既知制限を継承）、排他の on/off オプション（常時有効を継承）である。
- **Adjacent expectations**: rec-recording-playback Req 3.3（同一構成でのブレンド完全再現）・Req 3.5（停止時のシームレスなライブ引き継ぎ）・Req 3.8（再生開始時の基準状態確立）・Req 6 系（core は rec を知らない / 未使用時の挙動・性能不変 / core 改修は観測面・注入面・遮断面の追加に限定）・Req 8 系（毎フレーム GC ゼロ）、rec-playback-input-exclusivity Req 1.6（排他は常時有効）・Req 3.4（開始時スナップショット方式）・Req 4.3（StopPlayback を唯一の解放点とする）・Req 4.4（部分的排他状態を定常状態として残さない）を、新たに対象となる入力についても同じ強度で成立させること。注入面の占有規則（`IInjectedInputSource`）は rec-recording-playback の既存契約に従うこと

## Requirements

### Requirement 1: 全入力源の網羅分類（観測対象 / 明示的除外）

**Objective:** As a ライブラリ開発者, I want FacialControl の全 `IInputSource` 実装と `IAnalogInputSource` 単独実装（Runtime / Editor アセンブリ）が REC に対して「観測対象」か「理由付きの明示的除外」のどちらかに必ず分類されていてほしい, so that 「半端な未対応」が設計・実装・将来の追加のいずれの段階でも発生しない

#### Acceptance Criteria

1. The 本機能の設計 shall `FacialControl/Packages/com.hidano.facialcontrol*` 配下の全パッケージの Runtime および Editor アセンブリ（テストアセンブリを除く）に存在する具象 `IInputSource` 実装（抽象型を除く）と、`IInputSource` を実装しない具象 `IAnalogInputSource` 実装（`InputActionAnalogSource` / `ArKitOscAnalogSource` / `OscFloatAnalogSource` 等）を列挙し、それぞれを「観測対象」または「明示的除外」のいずれか一方に分類した一覧を成果物として残す
2. The 本機能の設計 shall 「明示的除外」に分類した各実装について、除外しても rec-recording-playback Req 3.3 のブレンド完全再現が損なわれない根拠（例: 入力元が既に観測対象として記録され再生時に同一値で再導出される派生値である、再生注入用の内部ソースである）を文書化する
3. The 本機能の設計 shall `OverlayInputSource` を上記の分類規則に従って判定し、その結論と根拠を文書化する（本要件は分類結果を固定しない。派生値として除外する場合は「active 表情の記録から同一の overlay 出力が再導出されること」を根拠として示す）。この判定は、overlay のレイヤー weight を駆動する経路（inputsystem の overlay binding による `FacialController.SetLayerWeight`）が本 spec ではライブのまま残る（HID-80 で扱う）ことを明示した前提の上で行い、「派生値」の根拠は表情由来の出力値（active 表情から解決される overlay 値）のみを対象とし、レイヤー weight には及ばないことを明記する
4. The 本機能の設計 shall core 内の派生型入力源（`AnalogBlendShapeInputSource` / `AnalogExpressionInputSource` 等、観測対象の `IAnalogInputSource` を入力として値を導出する実装）、`IAnalogInputSource` 単独実装（`InputActionAnalogSource` のようにラッパ経由で registry 登録されつつ直参照もされるもの、`ArKitOscAnalogSource` / `OscFloatAnalogSource` のように Runtime の消費者を持たないもの）、および Editor アセンブリの実装（timeline Editor の `OfflineExpressionSource` 等）についても同じ規則で分類し、除外する場合は「再生時に入力側の注入から同一値が導出される」「Runtime の合成パイプラインに到達しない」「Editor のベイクシミュレーション専用である」等の根拠を示す
5. The 本機能 shall 「観測対象」に分類された全実装について Requirement 2〜6 の記録・基準状態・遮断・注入・ラウンドトリップを成立させ、一部の観測対象だけが成立した状態を完成とみなさない
6. When 将来 `IInputSource` または `IAnalogInputSource` の新しい具象実装がいずれかのパッケージの Runtime / Editor アセンブリに追加されたとき, the 本機能 shall その実装が分類されるまで Requirement 7 の網羅性ゲートテストが失敗する状態を維持する（分類の追加を伴わない実装追加を機械的に拒否する）

### Requirement 2: 値提供型入力源（ValueProvider 系）の記録

**Objective:** As a Unity エンジニア, I want OSC 受信 BlendShape・iFacialMocap 受信 BlendShape・uLipSync 音素オーバーレイ・Timeline ベイク値など、値提供型入力源が合成パイプラインへ供給した値も REC に記録されてほしい, so that これらの入力を使ったパフォーマンスも記録どおりに完全再現できる

#### Acceptance Criteria

1. The core shall `ValueProviderInputSourceBase` 派生の入力源（`OscInputSource` / `LipSyncPhonemeOverlayInputSource` / `TimelineBakedValueSink` を含む、観測対象に分類された全ての値提供型）が各フレームでレイヤー合成へ実際に供給した値（消費値）を観測できる観測面を提供する
2. The core の値提供型観測面 shall 各フレームにおける当該入力源の有効性（`TryWriteValues` が true を返したか false を返したか）の変化も観測可能にする（OSC の staleness・リップシンクの無音判定・Timeline の未評価状態など、「無効 = 合成に寄与しない」状態がブレンド結果に影響するため）
3. Where 値提供型入力源の寄与対象集合（`ContributeMask`）が実行中に変化し得る実装である場合（`LipSyncPhonemeOverlayInputSource` の Override / Suppress 解決、Requirement 3.9 により in-place 更新される `OscInputSource` のマッピング集合変化等）, the core の値提供型観測面 shall 再生時に同一の合成入力を再現するのに必要な寄与対象集合の変化も観測可能にする（ライブの `OscInputSource` に対するマッピング変更由来の `ContributeMask` 変化も、他の mask 変化と同じ経路で観測される）
4. The core の値提供型観測面 shall FacialController の registry に登録された、またはレイヤーへバインドされた全ての `ValueProviderInputSourceBase` 派生に対して、派生クラス側の個別改修なしに適用される（既存実装と将来の実装の両方を、所属パッケージを問わず自動的に対象とする）
5. While 記録セッションが有効な間, when 値提供型入力源の消費値・有効性・寄与対象集合のいずれかが前フレームから変化したとき, the REC 記録サービス shall 入力源 id・記録開始起点の相対秒とともに当該変化を記録する
6. While 記録セッションが有効な間, when 値提供型入力源の消費値・有効性・寄与対象集合のいずれも前フレームから変化していないとき, the REC 記録サービス shall 当該入力源について新たな記録を追加しない（無変化フレームで記録量が増えない）
7. The 値提供型観測面と記録 shall 観測のみを行い、ライブの合成パイプラインや表情出力に一切書き戻さない（rec-recording-playback Req 2.5 の読取専用契約を継承）
8. The REC 記録サービス shall 値提供型入力源の値を正規化・clamp・量子化せずそのまま記録する（記録値と消費値が float ビット単位で一致する）

### Requirement 3: 値提供型入力源の再生時遮断と注入

**Objective:** As a Unity エンジニア, I want REC 再生中は値提供型入力源のライブ値（OSC 受信・マイク音声・Timeline 評価）が遮断され、記録された値が本物の合成パイプラインへ注入されてほしい, so that 再生中にライブ入力が混入せず、収録時と同一のブレンドが得られる

#### Acceptance Criteria

1. When 再生が開始されたとき, the REC 再生サービス shall 再生開始時点で registry に登録済み、またはレイヤーへバインド済みの全ての値提供型入力源（記録ベースラインに含まれないものを含む）を遮断対象とする（rec-playback-input-exclusivity Req 3.1 / 3.4 の開始時スナップショット方式を値提供型にも適用する）
2. While 再生中, when 遮断対象の値提供型入力源に対してライブの値更新（OSC 受信・音素解析結果・Timeline 評価等）が発生したとき, the REC 再生サービス shall 当該ライブ値をレイヤー合成へ反映させない
3. If 値提供型入力源が registry の差し替え（Replace）では到達できない経路（直接参照保持等）で合成パイプラインに消費されている場合, the core shall 当該入力源のライブ値を再生中に遮断できる遮断面を提供する（rec-playback-input-exclusivity がトリガー入力に対して core 側ゲートを設けたのと同じ理由による）
4. While 再生中, when 記録された値提供型入力源の値・有効性・寄与対象集合のイベントがタイムスタンプに到達したとき, the REC 再生サービス shall 当該入力源がライブで消費されるのと同一のレイヤー合成経路へ記録値を供給する（遷移計算・レイヤー合成はライブと同一コードパスで実行される）
5. When 再生が開始されたとき, the REC 再生サービス shall 記録ベースラインに含まれない遮断対象の値提供型入力源について確定的な初期状態（値と有効性）を確立する（方式はアナログ側の既存決定である 0 埋め seed との整合を含めて設計フェーズで決定し、本要件では固定しない）
6. When 再生停止（StopPlayback）が指示されたとき, the REC 再生サービス shall 値提供型入力源の遮断を解放し原本をライブへ引き継ぐ（自然完了 Completed では解放しない。rec-playback-input-exclusivity Req 4.2 / 4.3 を継承）
7. The REC 再生サービス shall トリガー・アナログ / gaze・値提供型・系1 経路（Requirement 4）の排他確立と解放を一貫した順序で行い、部分的な排他状態（一部の入力種別だけが遮断・解放された状態）を定常状態として残さない（rec-playback-input-exclusivity Req 4.4 を全入力種別へ拡張）
8. The 値提供型入力源への注入 shall rec-recording-playback の注入占有規則（`IInjectedInputSource` による他者占有の検出とスキップ、参照同一性による復元ガード）に従う
9. The osc パッケージの受信 binding（`OscReceiverAdapterBinding` の heartbeat 自動マッピング更新経路）shall マッピング集合の変化時に registry のエントリを入れ替え（`Replace`）ず、構築時に登録済みの `OscInputSource` インスタンスを更新する（マッピング index 表と `ContributeMask` を更新可能にし、registry に登録されたエントリの参照は構築時から変化しない）
10. While 再生中, when 受信側のマッピング集合が変化したとき, the osc パッケージの受信 binding shall 注入ソースを registry から追い出さない（ライブ OSC 値が再生中に復活せず、`EndInjection` の参照同一性ガードによる原本復元が成立する）

### Requirement 4: FacialController.Activate / Deactivate 直呼び（系1 経路）の記録・遮断・注入

**Objective:** As a Unity エンジニア, I want スクリプトや uGUI から `FacialController.Activate(Expression)` / `Deactivate(Expression)` を直接呼んだ操作も REC に記録され、再生中はライブ呼び出しが遮断されて記録どおりに再現されてほしい, so that トリガー入力源を経由しない表情操作を使ったアプリでも REC が完全に機能する

#### Acceptance Criteria

1. The core shall `FacialController.Activate` / `Deactivate` から `ExpressionUseCase` へ至る系1 経路の表情アクティブ化 / 非アクティブ化を、表情 id とイベント種別とともに観測できる観測面を提供する
2. The core の系1 観測面 shall 系1 経路のイベントを、トリガー入力源（系2）のイベントと区別できる識別子（系1 経路を表す入力源 id 相当）で観測者へ通知する（記録・再生・基準状態の全てで経路が一意に識別できる）
3. While 記録セッションが有効な間, when 系1 経路で表情がアクティブ化または非アクティブ化されたとき, the REC 記録サービス shall 当該イベントを表情 id・記録開始起点の相対秒とともに記録する
4. The core shall 系1 経路のライブ呼び出しを遮断する面（遮断の開始・解除。それぞれ冪等）を提供する
5. While 系1 経路が遮断されている間, when ライブの `Activate` / `Deactivate` が呼び出されたとき, the core shall 当該呼び出しを無視し、アクティブ表情集合を変更せず、観測者へ通知しない
6. The core shall 系1 経路の遮断を迂回する注入経路を提供し、注入経路経由のアクティブ化 / 非アクティブ化をライブ呼び出しと同一の `ExpressionUseCase` 処理（レイヤーの排他モード LastWins / Blend を含む）で受理し、観測者へ通知する
7. When 再生が開始されたとき, the REC 再生サービス shall 系1 経路を遮断してから、記録された系1 の基準状態（アクティブ表情集合）を遷移を経ない定常状態として確立する（記録に系1 の基準が無い場合は空集合へ = ライブの残存アクティブ表情を解除。rec-recording-playback Req 3.8 を系1 へ拡張）
8. While 再生中, when 記録された系1 イベントがタイムスタンプに到達したとき, the REC 再生サービス shall 注入経路経由で当該イベントを発火する
9. When 再生停止（StopPlayback）が指示されたとき, the core shall 系1 経路の遮断を解除し、アクティブ表情集合を解除時点の状態のまま維持する（自動解除なし。rec-recording-playback Req 3.5 を系1 へ拡張）
10. If 再生中に記録された系1 イベントの表情 id が現在のプロファイルに存在しないとき, the REC 再生サービス shall 当該イベントを発火前にスキップし、表情 id ごとに 1 回だけ警告を出す（rec-recording-playback Req 9.1 を系1 へ拡張）

### Requirement 5: 基準状態の拡張

**Objective:** As a Unity エンジニア, I want 記録開始時の基準状態に値提供型入力源の現在値と系1 のアクティブ表情集合も含まれてほしい, so that 再生開始フレーム 0 から収録時と同一のブレンドが収束窓なしに成立する

#### Acceptance Criteria

1. When 記録が開始されたとき, the REC 記録サービス shall 観測対象に分類された全ての値提供型入力源について、開始時点の消費値・有効性・寄与対象集合を基準状態として記録する
2. When 記録が開始されたとき, the REC 記録サービス shall 系1 経路の開始時点のアクティブ表情集合を基準状態として記録する
3. The 基準状態レコード shall 最初の時刻付きイベントより前に出現する（rec-recording-playback の `.fcrec` 不変条件を新しい基準種別にも適用する）
4. When 再生が開始されたとき, the REC 再生サービス shall 値提供型入力源と系1 経路の基準状態を、時系列イベントの発火を開始する前に、遷移を経ない定常状態として確立する
5. When 再生が開始されたとき and 記録の基準状態に含まれない観測対象入力源がライブに存在するとき, the REC 再生サービス shall 当該入力源を確定的な初期状態へ確立する（Requirement 3.5 / 4.7 と整合）
6. The REC 再生サービス shall 基準状態の確立を観測者へ通知しない（基準確立は操作イベントではない。rec-recording-playback の `ResetToExpressionStack` の規則を継承）

### Requirement 6: `.fcrec` ラウンドトリップとフォーマット決定

**Objective:** As a ライブラリ開発者, I want 新たに記録対象となる全ての値が `.fcrec` に書き出され、読み戻したときに記録時と同一のイベント列として復元されてほしい, so that 記録の正本性が全入力種別で維持される

#### Acceptance Criteria

1. The `.fcrec` 永続化 shall 本 spec で新たに記録対象となる全てのイベント種別と基準状態種別（値提供型の値 / 有効性 / 寄与対象集合、系1 のアクティブ化 / 非アクティブ化 / 基準アクティブ表情集合）を書き出し・読み戻しできる
2. When 記録を書き出して読み戻したとき, the REC 永続化 shall イベントの種別・順序・タイムスタンプ・入力源 id・表情 id・値（float ビット単位）・有効性・寄与対象集合を記録時と同一に復元する
3. The `.fcrec` 永続化 shall 1 サンプルあたりの値個数が 255 を超える値提供型入力源（`BlendShapeCount` がモデルの BlendShape 総数に等しい実装）を記録できる（既存 `AnalogSample` レコードの u8 軸数上限に拘束されない）
4. The `.fcrec` 永続化 shall `formatVersion` を 1 のまま据え置き、本 spec で必要となる記録構造の変更（新しいレコード kind の追加、u16 count を持つ値レコードの追加等）を版分岐なしで行う（v1 は未リリースであり、本 spec 実装後の構造が `formatVersion` 1 の唯一の定義となる）
5. The REC 永続化 shall 本 spec 以前の記録構造で書かれた `.fcrec` に対する読込互換・移行機能を提供しない（単一フォーマットのみを扱う。1.0.0 未リリースのため後方互換は制約としない）
6. The `.fcrec` 永続化 shall 寄与対象集合が BlendShape 総数より小さい値提供型入力源について、寄与しない BlendShape の値まで毎サンプル保存することを必須としない（記録サイズの扱いは設計フェーズで決定し、本要件では固定しない）
7. The REC 記録サービス shall 大きな値ベクトル（数百 BlendShape）を含むイベントの捕捉においても、メインスレッドの毎フレーム定常処理でヒープ確保を発生させない（キュー飽和時のセグメント追加確保は rec-recording-playback Req 8.5 の許容条件を継承）
8. When 途中位置からの再生（`StartPlayback` のオフセット指定、`RecTimelineSeek.BuildBaselineAt`）が指示されたとき, the REC 再生サービス shall 本 spec で追加された全てのレコード kind をベースラインへ畳み込む（値提供型はオフセット時点までの最後の値・有効性・寄与対象集合、系1 経路はオフセット時点の最終アクティブ表情集合）
9. The timeline パッケージの REC Export（Editor）shall 本 spec で追加されたレコード kind を含む `.fcrec` ファイルの読込に失敗しない（Export 対象としない kind は無視してよい）

### Requirement 7: 網羅性ゲートテスト

**Objective:** As a ライブラリ開発者, I want 全パッケージの `IInputSource` 実装と `IAnalogInputSource` 単独実装が観測対象か明示的除外のどちらかに分類されていることを自動テストで保証してほしい, so that 新しい入力源を追加したときの REC 対応漏れが CI で機械的に検出される

#### Acceptance Criteria

1. The 網羅性ゲートテスト shall reflection により、`FacialControl/Packages/com.hidano.facialcontrol*` 配下の全パッケージの Runtime および Editor アセンブリから、`IInputSource` を実装する具象型と、`IInputSource` を実装しない `IAnalogInputSource` の具象型（いずれも抽象型・インターフェース型を除く）を列挙する
2. The 網羅性ゲートテスト shall テスト専用アセンブリ（Tests / Testing / Shared の Fake 等）に定義された実装を列挙対象から除外する
3. If 列挙された実装のうち、「観測対象」にも「明示的除外」にも分類されていない型が 1 つでも存在するとき, the 網羅性ゲートテスト shall 当該型名を列挙した失敗メッセージとともに失敗する
4. If 「観測対象」と「明示的除外」の両方に分類されている型が存在するとき, the 網羅性ゲートテスト shall 失敗する
5. If 分類一覧に記載された型が列挙結果に存在しないとき（削除・改名済みの陳腐化したエントリ）, the 網羅性ゲートテスト shall 失敗する
6. The 網羅性ゲートテスト shall 「明示的除外」の各エントリに除外理由の記述を必須とし、理由が空のエントリを失敗として扱う
7. The 網羅性ゲートテスト shall 「観測対象」の各実装について、その実装が属する観測カテゴリ（トリガー型 / アナログ型 / 値提供型 / 系1 経路）を分類一覧から判別できるようにする
8. The 網羅性ゲートテスト shall Unity のシーン・MonoBehaviour ライフサイクル・ファイル I/O を使わず EditMode で実行でき、`docs/testing.md` のテストサイズ規約に従ったサイズ属性を持つ
9. The 網羅性ゲートテストのアセンブリ shall rec Runtime が依存しない拡張パッケージ（osc / inputsystem / lipsync / ifacialmocap / timeline / expression-creator / routing-editor）の Runtime および Editor アセンブリも列挙対象に含められる（テストアセンブリの参照追加は許容する。rec Runtime の core 以外への依存禁止は rec-recording-playback Req 7.3 のとおり維持する）
10. The 網羅性ゲートテスト shall push / PR ごとの CI で自動実行される

### Requirement 8: core 制約・性能・パッケージ境界の維持

**Objective:** As a ライブラリ開発者, I want 全入力対応を rec-recording-playback Req 6 / Req 8 の制約群の範囲内で実現したい, so that core の独立性・既存挙動・GC ゼロ目標・パッケージ分離を守ったまま REC の対象を拡張できる

#### Acceptance Criteria

1. The core 改修 shall 観測面・遮断面・注入面の追加に限定し、既存コードパスの挙動を変更しない（rec-recording-playback Req 6.1 の範囲内）
2. If 観測者が未登録で、遮断・注入のいずれも使用されていないとき, the core shall 既存の挙動・性能を一切変更しない（rec-recording-playback Req 6.5 を維持。値提供型の観測は観測者ゼロ時に早期 return し、系1 の遮断判定は bool 分岐 1 個に留める）
3. The core shall rec パッケージへの依存を持たない（rec-recording-playback Req 6.6 を維持）
4. The 本機能 shall 拡張パッケージ（osc / inputsystem / lipsync / ifacialmocap / timeline）の入力源実装を改修せずに、観測対象に分類された全入力源の記録・遮断・注入を成立させる。唯一の明示的な例外は Requirement 3.9 / 3.10 の osc パッケージ受信 binding の改修（マッピング集合変化時の registry `Replace` を廃止し、登録済み `OscInputSource` を in-place 更新する）であり、その理由は「`Replace` が注入占有規則を経ずに注入ソースを追い出し、`EndInjection` の参照同一性ガードによる復元を no-op にするため、registry 側の挙動変更（Requirement 8.1 違反）なしに解決するには osc 側で `Replace` 自体を無くす必要がある」ことである。これ以外の拡張パッケージ改修は行わない
5. While 記録中または再生中, the core および REC shall 毎フレームの定常処理でヒープ確保を発生させない（観測対象の拡大による値ベクトルの大型化・入力源数の増加を含む）
6. The core および REC shall 同時 10 体のキャラクターが値提供型入力源（ARKit 52ch 相当）を使用して同時に記録・再生する構成でも、キャラクターごとに独立した per-FC 観測バスと注入でスケールする（rec-recording-playback の per-FC スコープを維持）
7. The 値提供型の記録と再生 shall 入力源が合成パイプラインに消費される粒度（フレーム単位の消費値）で行い、1 フレーム内の複数回更新は最終消費値へ畳む（rec-recording-playback のアナログ pull 消費点サンプリングと同じ意味論。ライブブレンドも同じ値しか見ないため Req 3.3 と整合する）
8. The 本機能 shall エラーハンドリングを Unity 標準ログ（`Debug.Log/Warning/Error`）のみで行い、カスタム例外型を追加しない

### Requirement 9: 受け入れ検証

**Objective:** As a ライブラリ開発者, I want 新たに対象となる各入力種別について記録→再生の完全再現と再生中の遮断が自動テストで固定されてほしい, so that 全入力対応が「宣言」ではなく「検証済み」の状態で完成する

#### Acceptance Criteria

1. The 本機能の受け入れテスト shall 値提供型入力源を含む操作列を記録→停止→読込→再生したとき、ブレンド出力（`BlendedOutputSpan`）が再生フレーム 0 から収録時と一致することを検証する（同一プロファイル・同一レイヤー設定）
2. The 本機能の受け入れテスト shall 系1 経路の `Activate` / `Deactivate` を含む操作列について同様の完全再現を検証する
3. The 本機能の受け入れテスト shall 再生中の値提供型入力源へのライブ値更新と系1 経路のライブ呼び出しがブレンド出力に反映されないことを検証する
4. The 本機能の受け入れテスト shall 再生停止後に値提供型入力源の原本と系1 経路がライブへ引き継がれ、停止時点の状態が自動解除されないことを検証する
5. The 本機能の受け入れテスト shall 値提供型の有効性遷移（有効→無効→有効）と寄与対象集合の変化が記録・再生で再現されることを検証する
6. The 本機能の受け入れテスト shall 255 を超える値個数を持つ値提供型サンプルの `.fcrec` ラウンドトリップを検証する
7. The 本機能の受け入れテスト shall core の観測・遮断・注入ロジックと REC の記録・再生・永続化ロジックを Fake のみで EditMode 検証できる構造とし、MonoBehaviour ライフサイクル・フレーム進行・実 I/O を要する統合検証のみ PlayMode に配置する
8. The 本機能 shall 既存の EditMode / PlayMode テストスイート（rec-recording-playback・rec-playback-input-exclusivity の受け入れテストと `FacialControllerGcZeroGateTests` 等の GC ゲートを含む）を緑のまま維持する
9. The 本機能の受け入れテスト shall 再生中に osc 受信側のマッピング集合が変化しても注入ソースが registry に残り、再生停止後に原本の `OscInputSource` が復元されることを検証する（Requirement 3.9 / 3.10）
10. The 本機能の受け入れテスト shall 本 spec で追加されたレコード kind を含む記録を途中位置から再生したとき、オフセット時点の値提供型の値・有効性・寄与対象集合と系1 のアクティブ表情集合がベースラインとして確立されることを検証する（Requirement 6.8）

### Requirement 10: 既存 spec 文書と rec ドキュメントの整合

> **`rec-weight-coverage` による上書き注記:** 本文書の初期定義にある HID-80（レイヤー weight / 入力源 weight をライブのまま残す既知制限）は上書きされた。現在は weight 系統も REC の記録・遮断・注入対象であり、「ライブのまま残る」とする記述は履歴上の初期定義として扱う。kind 12〜15 の timeline REC Export 対応は timeline トラック合流後の follow-up であり、本タスクでは timeline パッケージを変更しない。

**Objective:** As a ライブラリ利用者・開発者, I want REC の対象範囲に関する文書が実装と一致していてほしい, so that 「記録されるはず」「記録されないはず」の誤解に基づく運用・設計ミスが起きない

#### Acceptance Criteria

1. The 本機能 shall `.kiro/specs/rec-recording-playback/design.md` の Non-Goals にある「リップシンク由来の操作イベントは他入力と同様に観測面経由で記録される」という記述を、本 spec 実装後の実態（`LipSyncPhonemeOverlayInputSource` の消費値が値提供型観測面経由で記録される）に合わせて修正する
2. The 本機能 shall `.kiro/specs/rec-recording-playback/design.md` の Out of Boundary にある「系1（`ExpressionUseCase` / `FacialController.Activate` 直接呼び出し）経路の記録」を対象外とする記述を、本 spec により上書きされた旨が分かる形で修正する
3. The 本機能 shall `.kiro/specs/rec-recording-playback/design.md` および `.kiro/specs/rec-playback-input-exclusivity/design.md` のうち、本 spec の方針（全入力を記録・遮断対象とする）と矛盾する他の記述（「拡張パッケージ内部の直接参照消費者への注入到達は対象外」等）を精査し、上書きされるものには上書き元 spec 名を付記する
4. The rec パッケージのドキュメント（`README.md` / `Documentation~/`）shall REC の記録・遮断対象となる入力種別の一覧（トリガー型 / アナログ・gaze / 値提供型 / 系1 経路）と、明示的除外に分類した実装とその理由を記載する
5. The rec パッケージのドキュメント shall `.fcrec` の `formatVersion` が 1 のまま据え置かれたこと、および本 spec 以前の記録構造で書かれたファイルの読込互換・移行は提供しない旨を記載する
6. The rec パッケージのドキュメント shall 開始時スナップショット方式（再生中に新規登録された入力源は遮断対象外）が値提供型・系1 経路にも適用される既知制限として記載する
7. The 本機能の設計文書（`.kiro/specs/rec-full-input-coverage/design.md`）および rec パッケージの `README.md` に、初期仕様ではランタイムのレイヤー weight / 入力源 weight 変更（inputsystem overlay binding による `FacialController.SetLayerWeight`、`LayerUseCase.SetInputSourceWeight`）を既知制限（Linear HID-80）としていたが、`rec-weight-coverage` により記録・遮断・注入の対象へ上書きされたことを注記する。現在の既知制限として「ライブのまま残る」と記載してはならない
8. The timeline パッケージのドキュメント（REC Export に関する節）shall 本 spec で追加されたレコード kind のうち Export 対象としない kind を無視する旨を記載する。ただし `rec-weight-coverage` の実装指示により timeline パッケージは本タスクでは変更せず、kind 12〜15 の timeline REC Export 対応は timeline トラック合流後の follow-up とする
