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
4. **Load Recording → Start Playback** で再生する。Recording Name を空にしたまま Load すると直近に録画したテイク（連番付与後の名前）を読み込む。再生中は live の表情トリガーとアナログ入力が遮断され、記録された値だけが反映される

スクリプトからは同じ操作を `RecCharacterBinding` の API で行える。

```csharp
var rec = GetComponent<RecCharacterBinding>();
rec.StartRecording("take01");   // take01.fcrec が既にあれば take01-2.fcrec に保存される
rec.StopRecording();
rec.LoadRecording();            // 名前を省略すると直近に録画したテイク（rec.LastRecordingName）を読み込む
rec.LoadRecording("take01");    // 名前を指定すればそのテイクを読み込む
rec.StartPlayback();            // 完了時は rec.Completed イベント
rec.StopPlayback();
```

録画と再生は排他で、片方を開始するともう片方は自動停止する。`OnDisable` / `OnDestroy` でも録画・再生は停止され、録画中のファイルは末尾まで書き切られる。

### 記録クロックと開始オフセット

記録タイムスタンプは既定では録画開始からの経過秒（`RecStopwatchClock`）。外部の時計に揃えたい場合は、次の 2 つの差し替え点を使う（どちらも次の `StartRecording` から反映される）。

- `RecordingClock`（`IRecClock`）: 記録に使うクロック。null なら既定の `RecStopwatchClock`。録画開始時に `Reset()` が 1 回呼ばれ、以後はメインスレッドから `ElapsedSeconds` が読まれる。値は有限・非負・単調非減少にすること（逆行は直前の値にクランプされ、例外・NaN・負の値は直前の値で置き換えて 1 回だけ警告される）。録画ごとに `Reset()` されるので、同時に録画する複数の binding で 1 つのインスタンスを共有しないこと
- `RecordingStartOffsetSeconds`（Inspector の **Recording Start Offset Seconds**）: すべてのタイムスタンプに加算する開始オフセット（秒、0 以上）。Footer の duration にも加算されるため、再生・REC Export ではタイムライン全体がこの秒数だけ後ろにずれる（再生時は先頭にこの秒数の待ちが入る）。録画中の `ElapsedSeconds`（Inspector の Elapsed Seconds）もオフセット込みの値になる

```csharp
rec.RecordingClock = myTimecodeClock;       // IRecClock の独自実装
rec.RecordingStartOffsetSeconds = 3600d;    // 例: 01:00:00:00 始まりに揃える
rec.StartRecording("take01");
```

タイムコード（LTC / MTC 等）の受信自体は提供しない。上記の差し替え点に独自のクロックを渡す想定。

## 記録される内容

| 種別 | 内容 |
|---|---|
| Trigger on/off | 入力源 id と Expression id の組 |
| Analog sample | 入力源 id と 1〜255 軸の float 値。Gaze の Vector2 もこの形式で記録される |
| Baseline | 録画開始時点でアクティブだった Expression と、有効だったアナログ入力源の現在値 |

Expression id や入力源 id は文字列として先頭で 1 度だけ定義され、以降のレコードは番号で参照する。Profile に存在しない Expression id は再生開始時に 1 回だけ警告され、その id のイベントは無視される。

## ファイル形式

`.fcrec` は独自バイナリ形式（little-endian）。マジック `FREC`、`formatVersion = 1`、開始時刻（Unix ms）のヘッダに続けてレコード列、末尾に duration と件数の Footer を持つ。ヘッダの開始時刻は、クロックを差し替えても常に録画開始時の壁時計（UTC）で、ヘッダの `flags`（u16）は予約（常に 0）。読み込み側は `flags` を検証しないので、将来「開始時刻の出自（壁時計 / 外部タイムコード）」などを `flags` のビットで表すときも `formatVersion` は上げずに済む。書き込みは専用スレッド（`RecStreamWriter`）でストリーム出力するため、録画中のメインスレッドに GC アロケーションは発生しない。Footer が欠けたファイルは末尾切れとして警告付きで復元される。

## 再生中の入力遮断

再生開始時点でレジストリに存在する入力源をスナップショットし、そのうえで記録を注入する。

- **Trigger**: 各トリガー入力源を suspend し、スタックを baseline に置き換えてから記録イベントを注入する。停止時は suspend を解除するが、スタックは元に戻さない
- **Analog / Gaze**: baseline にある入力源は記録値を seed にした再生用 source で置き換え、その他のアナログ入力源も 0 seed で置き換える。停止時は自分が置き換えたものだけを元に戻す
- 再生開始後に新しく登録された入力源は遮断の対象外
- `com.hidano.facialcontrol.timeline` の状態 sink もトリガー入力源の一種なので、REC 再生中は Timeline からの表情 on/off も抑止される

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
