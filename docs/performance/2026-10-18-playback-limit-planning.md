# 再生制限判定のAlloc削減

2026-10-18。公開APIを維持し、制限判定のクロージャとPredicate生成を直接比較へ置き換えた。停止対象のHashSetは候補が初めて決まるまで生成せず、Engine内で再利用する。各Startは独立した集合を借り、成功・拒否・例外のすべてでfinallyからClearして返却する。End通知中の再入呼び出しにも、使用中の集合を渡さない。

## 同条件の比較

Unity 6000.3.10f1 / Windows Editor Play Mode / Mono / i5-12400F。Deep Profiling無効、クリーン再コンパイル後、1秒の生成AudioClip、AudioSourceプール1、ネットワーク・デコードなし。GC.Allocは128回×3、CPUは512回×7の中央値。空処理0B、4096B配列4128Bの校正を確認した。

| 操作 | 前 B/回 | 後 B/回 | 前 μs | 後 μs |
|---|---:|---:|---:|---:|
| Registered.PlayStop.NoSubscriber | 2024 | 1408 | 17.401 | 14.991 |
| Direct.PlayStop.NoSubscriber | 2468 | 1852 | 17.086 | 15.847 |
| PreloadedReference.PlayStop.NoSubscriber | 2536 | 1920 | 20.827 | 20.014 |
| Registered.PlayPauseResumeStop.NoSubscriber | 2024 | 1408 | 18.539 | 15.641 |
| Registered.PlayPauseResumeStop.OneSubscriber.EnqueueOnly | 2104 | 1488 | 21.849 | 17.015 |
| Registered.PlayStopAll.NoSubscriber | 3408 | 2792 | 27.935 | 20.585 |
| Registered.PlayStop.WaitAfterCompletion | 2120 | 1504 | 18.699 | 15.842 |
| OrbitContext.Get.ExistingEngine | 0 | 0 | 0.385 | 0.394 |
| HeldPlayback.PauseResume.Dispatch.Subscribers0 | 0 | 0 | 0.272 | 0.24 |
| HeldPlayback.PauseResume.Dispatch.Subscribers1 | 0 | 0 | 1.083 | 0.825 |
| HeldPlayback.PauseResume.Dispatch.Subscribers8 | 0 | 0 | 1.102 | 1.122 |

通常の再生→停止は616B（30.4%）、確保回数は15→10回/再生を削減。全確保スタックの別採取16回でも22,528B／160確保と一致した。Start由来の割り当ては824→208Bとなり、残りはPlayback本体。通知の保持済みHandleと既存Engine取得の0Bは維持した。

## 上限超過の補足測定

ベンチマークへ実際のStopOldest置換とRejectNew拒否を追加した。既存シナリオを含む補足実行でもAlloc値は一致した。ウォームアップ後のStopOldest置換は1,408B／10確保/回、RejectNewは1,200B／9確保/回。拒否時には既存音が継続し、置換時には直前のHandleが完了することも各操作で検査している。これら2シナリオの変更前データはなく、削減率やCPU改善率の比較には使わない。

初回の停止対象選択、HashSet容量拡張、再入の深さが増した場合は追加確保が発生する。空に戻したHashSetの内部容量はEngineが保持するため、最大の停止対象数・再入深さに応じたメモリと引き換えになる。長時間のメモリ推移や多数同時再生の走査コストは別途計測する。CPU値はEditorとOSの変動を含む参考値で、Player・DSP・実発音の遅延を表さない。

## 回帰確認と残る候補

新規13ケースと既存AudioEngineTests33ケースがPlay Modeで成功。Engine／Category／Sound／Owner制限、StopOldest／StopLowestPriority、同優先度・同時刻の順序、他Ownerの除外、制限の重複、複数の停止対象、拒否時の非破壊性、再入中の集合分離、External再起動、クールダウン、プール再利用を確認した。実行は既存テストと新規テストを呼び出すPipelineの簡易ランナーで行った。

1,408Bの内訳はHandle本体312B、同期用オブジェクト16B、操作クロージャ312B、ロード非同期状態264B、RunRequest112B、ロード結果等120B、CTS64B、Playback208B。スタック中で最初に現れるCoreメソッド単位の分類であり、各行が単一のオブジェクト型という意味ではない。残る候補は一括停止の待機用確保や、ロード済み再生でも通る非同期経路。再利用APIはまだ導入していない。

- [変更前データ](2026-10-18-playback-lazy-completion.json)
- [変更後データ](2026-10-18-playback-limit-planning.json)
- [上限超過を含む補足データ](2026-10-18-playback-limit-scenarios.json)
- [全確保スタック](2026-10-18-playback-limit-allocations.json)
- [ソースハッシュ](2026-10-18-playback-limit-source-hashes.json)
- [ベンチマーク](../../Tools/Benchmarks/AudioOrbitBaseline.cs)
