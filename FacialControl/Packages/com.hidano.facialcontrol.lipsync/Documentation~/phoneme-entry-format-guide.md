# Phoneme Entry 形式ガイド

`ULipSyncAdapterBinding` の phoneme entry は、A / I / U / E / O などの phoneme を FacialControl の BlendShape snapshot に変換するための設定です。現在選べる形式は `ExpressionPhonemeEntry`、`AnimationClipPhonemeEntry`、`BlendShapePhonemeEntry` の 3 種類です。

新しく設定する場合は、まず `ExpressionPhonemeEntry` を選んでください。`FacialProfile` に登録済みの Expression を参照するため、AnimationClip の renderer path や BlendShape curve の構造に依存せず、最も確実に動作します。

## 形式比較

| 形式 | 向いている用途 | 強み | 注意点 |
|---|---|---|---|
| `ExpressionPhonemeEntry` | Profile に A / I / U / E / O などの口形 Expression がある場合 | 既存の Expression snapshot を参照するため、renderer path 不一致の影響を受けません。Inspector の既定追加形式です。 | 参照先 Expression の割り当てが必要です。未割り当ての場合は HelpBox と warning が表示され、該当 phoneme はスキップされます。 |
| `AnimationClipPhonemeEntry` | 1 phoneme に複数 BlendShape をまとめて設定したい場合、または既存の口形 AnimationClip を流用したい場合 | Clip の終端フレームに置いた複数 BlendShape 値をまとめて snapshot 化できます。 | `AnimationClip.SampleAnimation` は Host GameObject 配下の renderer path と curve binding が一致する必要があります。Clip の終端時刻（`length` が 0 なら先頭）の値だけを読むため、Clip の時間再生、途中キー、ループ、イージングは使われません。 |
| `BlendShapePhonemeEntry` | 1 phoneme が単一 BlendShape 名だけで表せる場合 | 最も単純で、BlendShape 名と最大 weight だけで設定できます。 | 単一 BlendShape しか出力できません。複数 BlendShape を組み合わせる口形には不向きです。BlendShape 名が対象 mesh と一致しない場合は動作しません。 |

選択の目安は次のとおりです。

1. Profile に口形 Expression を作れる場合は `ExpressionPhonemeEntry` を使います。
2. 複数 BlendShape の組み合わせを Clip として管理したい場合だけ `AnimationClipPhonemeEntry` を使います。
3. 口形が単一 BlendShape で十分な場合は `BlendShapePhonemeEntry` を使います。

`AnimationClipPhonemeEntry` の sample 結果がすべて 0 で、同じ `PhonemeId` の Expression が Profile に存在する場合は、Expression 参照の snapshot が fallback として使われます。この fallback は既存データの救済用です。新規設定では、より確実な代替として `ExpressionPhonemeEntry` を直接使ってください。

## Migration Notes

この変更以降、Inspector で uLipSync binding を新規追加したときの既定 phoneme entry は `ExpressionPhonemeEntry` になります。以前の既定である `AnimationClipPhonemeEntry` は、renderer path の不一致や BlendShape curve 未設定によって snapshot が空になりやすいため、新規作成時の第一候補から外れました。

既存の `[SerializeReference]` データはそのままロードできます。保存済みの `BlendShapePhonemeEntry` と `AnimationClipPhonemeEntry` は削除も変換もされません。すでに動作している Profile は、急いで移行する必要はありません。

既存の `AnimationClipPhonemeEntry` が「動かない」場合は、次の順に確認してください。

1. Clip の終端フレームに必要な BlendShape weight が入っているか確認します。
2. Clip の curve binding の renderer path が Host GameObject 配下の実際の階層と一致しているか確認します。
3. Profile に同じ phoneme の Expression を作成し、`ExpressionPhonemeEntry` へ置き換えます。

移行時に迷う場合は、Profile の Expression 一覧に `A` / `I` / `U` / `E` / `O` 相当の口形を登録し、各 phoneme entry からそれらを参照する構成にしてください。これにより、LipSync 以外の overlay や表情制御と同じ口形定義を共有できます。

## サンプル方針

`MicLipSyncDemo` は、モデル側に `A / I / U / E / O` という名前の BlendShape がある前提で `BlendShapePhonemeEntry` を使う最小構成です。モデルの BlendShape 名が異なる場合は、各 entry の BlendShape 名を合わせるか、Profile に口形 Expression を登録して `ExpressionPhonemeEntry` に切り替えてください。

`AnimationClipLipSyncDemo` は、`BlendShapePhonemeEntry` と `AnimationClipPhonemeEntry` の混在例です。Clip の終端フレームの BlendShape 値だけが使われること、renderer path が一致しないと snapshot が空になることを確認できます。

`Multi Source Blend Demo` は、LipSync 専用の phoneme entry 形式ではなく、FacialControl の Expression / overlay / input source の合成を示すサンプルです。LipSync と組み合わせる場合は、同じ口形 Expression を `ExpressionPhonemeEntry` から参照し、必要に応じて overlay 側で Override / Suppress を設定してください。
