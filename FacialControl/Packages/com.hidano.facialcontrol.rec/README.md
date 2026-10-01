# FacialControl REC

`com.hidano.facialcontrol` に流れ込む入力（表情トリガーの on/off・アナログ値・Gaze）を記録し、あとから同じ入力を再現するパッケージ。再生中は live 入力を遮断するので、キャプチャした演技を配信中にそのまま再生したり、`com.hidano.facialcontrol.timeline` 経由で Timeline へ書き出したりできる。

## 依存パッケージ

| パッケージ | バージョン | 用途 |
|---|---|---|
| `com.hidano.facialcontrol` | 1.0.0 | 入力観測バス (`IFacialInputObservationBus`) と入力源レジストリ |

OSC / InputSystem / LipSync / iFacialMocap パッケージには依存しない。どの入力源から来た値でも、core の観測バスを通る限り記録できる。

## 使い方

1. `FacialController` を持つ GameObject に **Add Component → FacialControl → REC Character Binding** を追加する（`RecCharacterBinding`。同 GameObject の `FacialController` を自動で拾う）
2. Play 中に Inspector の **Start Recording** を押す。Recording Name を空にすると Default Recording Name（初期値は空）が使われ、それも空なら `take-yyyyMMdd-HHmmss` 形式で命名される
3. **Stop Recording** で `.fcrec` ファイルが確定する。保存先は `StreamingAssets/FacialControl/{キャラクター名}/recordings/{名前}.fcrec`。同名の録画がすでにある場合は上書きせず、`{名前}-2`, `{名前}-3`… と連番を付けて保存する（実際に保存した名前とパスは Inspector の Path 表示と `LastRecordingName` / `LastRecordingPath` で確認できる）。明示的に上書きする手段は API にも Inspector にも用意していない
4. **Load Target** ドロップダウンで再生するテイクを選び、**Load Recording → Start Playback** で再生する。ドロップダウンには保存済み録画が更新日時の新しい順に並び、録画を止めると保存したテイクが選択された状態になる（フォルダへ直接ファイルを置いた場合は **Refresh** で読み直す）。録画が 1 件も無く Load Target が空のまま Load すると直近に録画したテイク（連番付与後の名前）を読み込む。再生中は live の表情トリガーとアナログ入力が遮断され、記録された値だけが反映される

スクリプトからは同じ操作を `RecCharacterBinding` の API で行える。

```csharp
var rec = GetComponent<RecCharacterBinding>();
rec.StartRecording("take01");   // take01.fcrec が既にあれば take01-2.fcrec に保存される
rec.StopRecording();
rec.LoadRecording();            // 名前を省略すると直近に録画したテイク（rec.LastRecordingName）を読み込む
rec.LoadRecording("take01");    // 名前を指定すればそのテイクを読み込む
IReadOnlyList<string> takes = rec.GetRecordingNames(); // 保存済みテイク名（新しい順）。uGUI の Dropdown の選択肢などに使う
if (takes.Count > 0)
{
    rec.LoadRecording(takes[0]);
}
rec.StartPlayback();            // 完了時は rec.Completed イベント
rec.StartPlayback(12.5);        // 録画の 12.5 秒の位置から再生する
rec.StopPlayback();
```

`GetRecordingNames()` / `GetRecordings()`（テイク名・パス・更新日時）は録画フォルダを読むファイル I/O なので、毎フレームではなく画面を開いたときや録画を止めたときなど、一覧の更新が必要なときだけ呼ぶ。Play モード外でも呼べ、録画中のテイクと、テイク名としてそのまま読み込めない名前（`..` や前後の空白を含む等）のファイルは含めない。

録画と再生は排他で、片方を開始するともう片方は自動停止する。`OnDisable` / `OnDestroy` でも録画・再生は停止され、録画中のファイルは末尾まで書き切られる。

### uGUI / UnityEvent から操作する

`StartRecording` / `LoadRecording` / `StartPlayback` は結果を `bool` で返すため、Button の OnClick など Inspector の UnityEvent には列挙されない。UnityEvent からはコード無しで次の void 版を選ぶ（`bool` 版はスクリプト向けにそのまま使える）。

| UnityEvent で選ぶもの | 動作 |
|---|---|
| `RecordingName`（string） | テイク名を設定する（Inspector の Default Recording Name と同じ値）。InputField の On End Edit などの Dynamic string に繋ぐ |
| `Record ()` | `RecordingName` で録画を始める。空なら `take-yyyyMMdd-HHmmss` |
| `Record (string)` | 指定した名前で録画を始める |
| `StopRecording ()` | 録画を止めて確定する |
| `Load ()` | `RecordingName` で直近に録画したテイクを読み込む（同名衝突で `{名前}-2` などに保存していればそちら）。空なら直近に録画したテイク |
| `Load (string)` | 指定した名前のテイクをそのまま読み込む。空なら `Load ()` と同じ |
| `Play ()` / `StopPlayback ()` | 再生を開始 / 停止する |

配線例（InputField 1 つと Button 4 つ）:

1. InputField の **On End Edit (String)** → `RecCharacterBinding` の Dynamic string から `RecordingName`
2. Record ボタンの **On Click ()** → `RecCharacterBinding.Record ()`
3. Stop ボタン → `RecCharacterBinding.StopRecording ()`
4. Load ボタン → `RecCharacterBinding.Load ()`
5. Play ボタン → `RecCharacterBinding.Play ()`

同じ `RecordingName` のまま Record → Stop → Load → Play を繰り返しても、毎回いま録ったテイクが再生される。録画中に Load を押した場合は録画を止めて確定してから読み込む。失敗時（未初期化・該当ファイルなし等）は `bool` 版と同じく Console にログが出る。読み込みに失敗すると前に読み込んだテイクも破棄されるので、続く Play が古いテイクを再生することはない。

## 記録される内容

| 種別 | 内容 |
|---|---|
| Trigger on/off | 入力源 id と Expression id の組 |
| Analog sample | 入力源 id と 1〜255 軸の float 値。Gaze の Vector2 もこの形式で記録される |
| Baseline | 録画開始時点でアクティブだった Expression と、有効だったアナログ入力源の現在値 |

Expression id や入力源 id は文字列として先頭で 1 度だけ定義され、以降のレコードは番号で参照する。Profile に存在しない Expression id は再生開始時に 1 回だけ警告され、その id のイベントは無視される。

## ファイル形式

`.fcrec` は独自バイナリ形式（little-endian）。マジック `FREC`、`formatVersion = 1`、開始時刻（Unix ms）のヘッダに続けてレコード列、末尾に duration と件数の Footer を持つ。書き込みは専用スレッド（`RecStreamWriter`）でストリーム出力するため、録画中のメインスレッドに GC アロケーションは発生しない。Footer が欠けたファイルは末尾切れとして警告付きで復元される。

## 再生中の入力遮断

再生開始時点でレジストリに存在する入力源をスナップショットし、そのうえで記録を注入する。

- **Trigger**: 各トリガー入力源を suspend し、スタックを baseline に置き換えてから記録イベントを注入する。停止時は suspend を解除するが、スタックは元に戻さない
- **Analog / Gaze**: baseline にある入力源は記録値を seed にした再生用 source で置き換え、その他のアナログ入力源も 0 seed で置き換える。停止時は自分が置き換えたものだけを元に戻す
- 再生開始後に新しく登録された入力源は遮断の対象外
- `com.hidano.facialcontrol.timeline` の状態 sink もトリガー入力源の一種なので、REC 再生中は Timeline からの表情 on/off も抑止される
- 再生が最後まで進んでも遮断は解除されない。`StopPlayback` を呼ぶまで最後の状態が保たれる

## 途中からの再生

`StartPlayback(startOffsetSeconds)` は録画の途中から再生する。開始位置より前のイベントを時刻順に瞬時に畳み込み、その結果を baseline として注入してから、開始位置以降のイベントを通常どおり発火させる。

- **Trigger**: 各入力源のスタックを最終的な on/off の状態にする（on は同じ id を末尾へ移動、off は取り除く）
- **Analog / Gaze**: 各入力源の最後のサンプル値にする
- 開始位置ちょうどのイベントは畳み込まず、最初のフレームで発火する。開始位置 0 は `StartPlayback()` と同じ動作になる
- 録画長以上を指定すると、録画長ちょうどのイベントも含めて最終状態を注入し、即座に完了する（`Completed` が発火する）
- 負値・NaN・無限大は警告を出して false を返す

制限事項:

- 開始位置で遷移途中だった表情は、遷移の進行度までは再現できない。その時点の目標状態（遷移完了後の状態）から始まる
- 畳み込みでは入力源ごとのスタック上限（maxStackDepth）を適用しない。注入時に新しい側から上限数だけが残る。上限超過で押し出された表情が、後の off で再び表に出る稀なケースは再現されない

## 構成

```
Runtime/
├── Domain/        # RecEvent / RecTimeline / RecBinaryFormat / RecPlaybackScheduler（Unity 非依存）
├── Application/   # RecordingUseCase / PlaybackUseCase
└── Adapters/      # RecCharacterBinding (MonoBehaviour) / RecStreamWriter / RecFileReader / 注入用 Injector
Editor/            # RecCharacterBinding の UI Toolkit Inspector
Tests/             # EditMode 単体 + PlayMode E2E / GC ゼロ gate
```

サンプルは同梱しない。Timeline への書き出しは `com.hidano.facialcontrol.timeline` の **Tools → FacialControl → Timeline → REC Export** を使う。

## ライセンス

[MIT License](LICENSE.md)
