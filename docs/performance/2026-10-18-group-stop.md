# 一括停止のAlloc削減

2026-10-18。公開APIを維持し、StopAll／StopCategory／StopOwner／StopLibraryの非Async版から待機用Taskの生成を除いた。条件比較は値型フィルターへ置換し、Predicateと捕捉ラムダを生成しない。

停止対象は再生中とロード待ちの両方から通知前に確定する。Engine内のListを借り、停止が完了するまで返却しない。再入する一括停止は別のListを使い、停止・通知中に追加された再生を外側の操作へ混入させない。finallyでClearして返却する。

Async版も即時完了する対象にはHandleの終了Taskを作らず、未完了のフェードがある場合だけ待機Taskを取得する。待機対象が1つならWhenAllを作らずそのTaskを使う。CancellationTokenは従来どおり待機だけを取り消し、停止要求は継続する。

## 同条件の測定

Unity 6000.3.10f1 / Windows Editor Play Mode / Mono / i5-12400F。Deep Profiling無効、クリーン再コンパイル後。1秒の生成AudioClip、AudioSourceプール1。ウォームアップ128回、GC.Allocは128回×3、CPUは512回×7の中央値（空処理等は4096回）。空処理0B、4096B配列4128Bの校正を確認した。

| 操作 | 前 B/回 | 後 B/回 | 前 μs | 後 μs |
|---|---:|---:|---:|---:|
| Registered.PlayStop.NoSubscriber | 632 | 632 | 12.657 | 14.524 |
| Direct.PlayStop.NoSubscriber | 876 | 876 | 13.434 | 15.282 |
| PreloadedReference.PlayStop.NoSubscriber | 696 | 696 | 13.992 | 19.229 |
| Registered.PlayPauseResumeStop.NoSubscriber | 632 | 632 | 13.815 | 16.241 |
| Registered.PlayPauseResumeStop.OneSubscriber.EnqueueOnly | 712 | 712 | 15.695 | 16.733 |
| Registered.PlayStopAll.NoSubscriber | 2016 | 632 | 19.589 | 14.125 |
| Registered.PlayStopAllAsync.Immediate | — | 632 | — | 14.631 |
| Registered.PlayStopCategory.NoSubscriber | — | 632 | — | 14.348 |
| Registered.PlayStopLibrary.NoSubscriber | — | 632 | — | 14.618 |
| Registered.PlayStopOwner.NoSubscriber | — | 632 | — | 14.809 |
| GroupStop.Empty.AllFilters | — | 0 | — | 1.013 |
| Registered.PlayStop.WaitAfterCompletion | 728 | 728 | 12.954 | 12.828 |
| OrbitContext.Get.ExistingEngine | 0 | 0 | 0.387 | 0.444 |
| HeldPlayback.PauseResume.Dispatch.Subscribers0 | 0 | 0 | 0.246 | 0.304 |
| HeldPlayback.PauseResume.Dispatch.Subscribers1 | 0 | 0 | 0.819 | 0.829 |
| HeldPlayback.PauseResume.Dispatch.Subscribers8 | 0 | 0 | 1.086 | 1.062 |
| Limits.StopOldest.ReplaceExisting | 632 | 632 | 15.492 | 14.061 |
| Limits.RejectNew.KeepExisting | 424 | 424 | 3.219 | 3.14 |

Play→StopAllは2,016→632B/回（68.7%減）、22→5確保/回。単独Play→Stopと一致し、ウォームアップ後の即時一括停止による追加Allocは消えた。Category／Owner／Libraryと即時StopAllAsyncも632B。新規追加シナリオの変更前データは採取していないため、削減率はStopAllだけを比較する。

GroupStop.Empty.AllFiltersは4種類の同期停止を1操作として計測し、0Bを確認。空対象ではスナップショットListも新規取得しない。

## 検証

新規GroupStopTests17ケース、既存AudioEngineTests33ケース、PlaybackStateChangedTests8ケース、計58ケースがPipelineの簡易ランナーでPlay Mode成功。All／Category／Owner／Libraryのフィルター、同期・非同期のフェード、後から始まった音の除外、即時完了とキャンセル済みToken、複数フェードの待機キャンセル、無効Fadeの非破壊性、停止通知からの再入、ロード中と再生中の同時停止・通知から追加されたロード待ちの除外を確認した。

Unity 2022.3.62f3と6000.2.9f1のEngineアセンブリ・.NET Standard参照でRuntimeの条件付きコンパイルも成功。既存のシリアライズフィールド未代入・未使用フィールド警告は残る。これは旧Editor上の実行試験ではない。

## 制約・残る項目

0Bという追加費用は、ウォームアップ後の即時停止または空対象で確認したもの。初回・対象数増加・再入深度増加ではListと内部配列の確保があり、Engineが最大容量を保持する。ClearによりHandle参照は残さない。Asyncフェード待機では終了Task、待機リスト、複数対象のWhenAll、キャンセル登録は引き続き必要時に生成する。

CPU値は単一Editor環境の参考値。同じ実行でも変更していない通常再生・Library参照のCPUに変動があり、改善率の根拠には使わない。ネットワーク・デコード・DSP・Player・長期のメモリ推移は未測定。個別の再生→停止632Bは今回変更していない。

- [変更前の生データ](2026-10-18-playback-ready-path.json)
- [変更後の生データ](2026-10-18-group-stop.json)
- [ソースハッシュ](2026-10-18-group-stop-source-hashes.json)
- [ベンチマーク](../../Tools/Benchmarks/AudioOrbitBaseline.cs)
