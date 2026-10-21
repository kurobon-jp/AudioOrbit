# 再生開始時Allocの削減（2026-10-20）

公開APIを変えず、同期開始で不要なキャンセル資源と保持用ラッパーを削除した。LibraryのEngine内キャッシュ、再生中の保持、再生回ごとの完了・待機の独立性は維持している。

## 測定条件

- Unity 6000.3.10f1、Editor Play Mode / Mono、既存AudioSourceプール1個、通知購読なし。
- 1操作はPlayから即時Stopまで。登録済みLibrary、Direct参照、事前ロード済みLibrary参照は、同じ1秒のループClipを使用する。未ロードのI/OとDSP負荷は対象外。
- Handle再利用ケースはCreatePlaybackの確保を含めない。新規作成ケースは含める。
- 128回ウォームアップ。CPUは記録を無効化して512回×7試行、Allocは固有ProfilerMarker配下のメインスレッドGC.Allocを128回×3試行。表のバイト数は中央値で、各試行の確保回数も一致した。
- 4096バイト配列の確保は4128B、空操作は0Bで前後とも校正した。Editor側で動く検証コードを記録するため、Alloc測定中のみprofileEditorとCPU記録を有効化し、終了時に元へ戻した。マーカー未取得だった最初の試行は結果に採用していない。
- Player／IL2CPPの計測は実施していない。過去の測定値との比較ではなく、今回の現行APIに対する変更直前・直後を比較する。

## Allocの比較

| 条件 | 変更前／回 | 変更後／回 | 削減率 | 確保回数／回 |
|---|---:|---:|---:|---:|
| Handle新規作成＋通常再生 | 672 B | 576 B | 14.3% | 6 → 4 |
| Handle再利用＋通常再生 | 384 B | 288 B | 25.0% | 4 → 2 |
| Handle再利用＋Direct参照 | 628 B | 532 B | 15.3% | 8 → 6 |
| Handle再利用＋ロード済みLibrary参照 | 448 B | 320 B | 28.6% | 6 → 3 |
| Handle再利用＋終了待機Task取得 | 1248 B | 1152 B | 7.7% | 11 → 9 |

## 変更内容

1. CancellationTokenSourceをLibrary／PCMのロード待ちが必要になったときだけ生成する。通常の同期開始・停止では生成しない。開始前の停止で作成済みTokenをキャンセルし、遅れて完了したロードからの再開を防ぐ。
2. PlaybackResourceを削除。Clipのスナップショットは開始処理へ直接渡し、Library由来の再生だけSessionReferenceを保持する。既存の利用権をラッパーで二重に包まない。資源を借用する登録済みLibrary／Direct参照には保持用オブジェクトを生成しない。
3. キャッシュ済みLibrary参照の同期経路では、一時的なSessionReferenceの取得・返却を省く。Engineキャッシュの保持下で解決し、PCM準備などで待機に入る前に再生自身の保持権を取得する。

## CPU参考値

| 条件 | 変更前の中央値 | 変更後の中央値 |
|---|---:|---:|
| Handle新規作成＋通常再生 | 15.95 μs | 12.34 μs |
| Handle再利用＋通常再生 | 13.34 μs | 11.54 μs |
| Handle再利用＋Direct参照 | 15.30 μs | 11.74 μs |
| Handle再利用＋ロード済みLibrary参照 | 16.09 μs | 12.71 μs |
| Handle再利用＋終了待機Task取得 | 16.76 μs | 14.71 μs |

CPUは同じEditorセッション内の比較であり、一般的な短縮率やPlayerの性能を保証する値ではない。

## 検証・残る候補

- Play Mode 137ケース：Handle再利用、Direct、ロード待ち停止、Libraryキャッシュ・解放、完了待機、状態通知、一括停止、AudioPlayer、区間再生。
- Edit Mode 21ケース：Resources参照とEditorプレビュー。合計158ケース成功。コンパイルはエラー・警告0件。
- 再生回ごとのPlaybackRequest、完了同期用のゲートは残る。Direct参照は変更可能な設定を毎回スナップショットするDefinition／ClipDefinition／配列／制限設定を引き続き生成する。終了待機を使う場合のTask類も別途必要。
- 公開Handleや再生回の安易なプール化は行っていない。過去の待機と未配信通知が次の再生に作用しない仕様を優先する。追加削減は実際に必要になった時点で判断する。

## 再実行

[計測コード](../../Tools/Benchmarks/PlaybackHandleComparison.cs)をDeep ProfilingなしのPlay Modeで実行する。Profiler設定はコード内で復元する。

[変更前JSON](2026-10-20-start-allocation-before.json) / [変更後JSON](2026-10-20-start-allocation-after.json)
