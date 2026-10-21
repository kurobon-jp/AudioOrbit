# 再生→停止のAlloc内訳

2026-10-18。状態通知の最適化後に残った2,912B/回を、Unity ProfilerのGC.Allocコールスタックで分類した。

1秒の生成AudioClip、登録済みKey、AudioSourceプール1、ループ再生、終了待機なし。128回ウォームアップ後に16回の再生→停止を記録。合計46,592B／352確保で、前回のベンチマークの2,912B／22確保/回と一致した。コールスタック収集時のCPU時間は比較に使わない。

| 最初に現れるCore側の呼び出し元 | B/再生 | 確保回数/再生 |
|---|---:|---:|
| PlaybackHandleのコンストラクター（終了Task一式） | 864 | 7 |
| AudioEngine.Start（Playback・制限判定用一時領域等） | 824 | 6 |
| AudioEngine.Play（操作ラムダ・クロージャ） | 312 | 2 |
| AudioEngine.CreateRequest（Handle本体） | 272 | 1 |
| AudioEngine.LoadPlayback（非同期状態） | 264 | 1 |
| LoadPlaybackのMoveNext（PlaybackResource・結果Task） | 120 | 2 |
| AudioEngine.RunRequest（非同期状態） | 112 | 1 |
| AudioEngine.PrepareRegion（通常Clipでも生成される非同期状態） | 80 | 1 |
| PlaybackHandle.InitializeRequest（CTS） | 64 | 1 |
| 合計 | **2,912** | **22** |

分類は各GC.Allocのスタック内で最初に現れるCoreのメソッド単位。Task内部の確保もPlaybackHandleのコンストラクターへ集約しており、各行が特定の単一オブジェクト型のサイズという意味ではない。表中のオブジェクト名はソースとの対応による説明である。

## 今回の変更対象

- 終了結果をHandle内へ保持し、TaskCompletionSourceは未完了の再生を実際に待機された場合のみ生成する。完了後に初めて待機された場合は結果Taskを生成・キャッシュする。複数待機者と完了の競合をロックで保護する。
- AsyncWaitは完了済みまたはキャンセル不能なTaskをそのまま返す。キャンセル可能な未完了待機だけ非同期ラッパーを使う。
- 通常AudioClipやPCM取得済みのループClipでは、PrepareRegionの非同期状態を生成せず完了Taskを返す。

変更後の削減量は再測定結果を参照する。Handleへの結果保持や同期用オブジェクトにも費用があるため、上記864Bと80Bの全額が削減されるとは仮定しない。

- [元の全確保スタック](2026-10-18-playback-allocations-before.json)
- [メソッド単位の集計](2026-10-18-playback-allocation-summary.json)
- [採取スクリプト](../../Tools/Benchmarks/AudioOrbitAllocationBreakdown.cs)

## 変更後の再測定

同じEditor Play Mode / Mono、Deep Profiling無効でクリーン再コンパイル後に計測。GC.Allocは128回×3、CPUは512回×7の中央値（マイクロベンチは4096回）。校正用4096B配列は4128B、空処理は0Bを再確認した。

| 操作 | 変更前 B/回 | 変更後 B/回 | 変更前 μs | 変更後 μs |
|---|---:|---:|---:|---:|
| Registered.PlayStop.NoSubscriber | 2912 | 2024 | 20.784 | 17.401 |
| Direct.PlayStop.NoSubscriber | 3356 | 2468 | 20.684 | 17.086 |
| PreloadedReference.PlayStop.NoSubscriber | 3424 | 2536 | 24.508 | 20.827 |
| Registered.PlayPauseResumeStop.NoSubscriber | 2912 | 2024 | 21.405 | 18.539 |
| Registered.PlayPauseResumeStop.OneSubscriber.EnqueueOnly | 2992 | 2104 | 25.926 | 21.849 |
| Registered.PlayStopAll.NoSubscriber | 4417.125 | 3408 | 31.19 | 27.935 |
| Registered.PlayStop.WaitAfterCompletion | 3272 | 2120 | 24.705 | 18.699 |
| OrbitContext.Get.ExistingEngine | 0 | 0 | 0.399 | 0.385 |
| HeldPlayback.PauseResume.Dispatch.Subscribers0 | 0 | 0 | 0.272 | 0.272 |
| HeldPlayback.PauseResume.Dispatch.Subscribers1 | 0 | 0 | 0.943 | 1.083 |
| HeldPlayback.PauseResume.Dispatch.Subscribers8 | 0 | 0 | 1.122 | 1.102 |

登録済みKeyの再生→停止は888B（30.5%）、確保回数は22→15回/再生を削減。別途16回のコールスタック収集でも合計32,384B、240確保と一致した。終了待機しない経路で終了Task一式を生成しない効果が中心。完了後待機も2,120Bへ削減した。

CPUは登録済み再生→停止で20.781→17.402μs。Editor・OSの変動を含む単一環境の参考値であり、Playerや実発音の遅延、DSP負荷の改善を示すものではない。保持済みHandleの通知と既存Engine取得の0Bは維持した。

検証は待機・状態通知・直接参照の16件、Play Modeの区間再生13件とSoundPlayer8件が成功。既存のInvalidRegionFailsBeforePlaybackAndDoesNotPinPcmはログ期待値未指定のため今回の簡易実行から除外している。Unity 2022.3と6.2向けの条件付きRuntimeコンパイルも成功。

Play APIは維持した。残り2,024BにはPlayback、制限判定用一時領域、操作クロージャ、ロードの非同期状態、CTSなどが残る。これらの優先度を検討してから再利用可能な再生操作との比較を行う。公開Handleのプール化や新APIは今回導入していない。

- [再測定の生データ](2026-10-18-playback-lazy-completion.json)
- [変更後の全確保スタック](2026-10-18-playback-allocations-after.json)
