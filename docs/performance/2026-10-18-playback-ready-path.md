# ロード済み再生の同期経路

2026-10-18。公開APIは維持し、内部の開始結果にValueTaskを使用する。ロード済みの通常Clipは値で結果を返し、完了Task・非同期状態・操作ラムダを生成しない。未ロードLibrary、未完了のPCM準備の場合だけTaskを持つ経路へ進み、従来同様にHandleが開始・停止・失敗を保持する。

ロード済みのLibrary参照は同期的にSessionを取得し、LoadPlaybackが独自の再生用Sessionを保持した後で取得分を返す。PCM待機が必要な場合も再生用リースが保持を引き継ぐ。Direct参照とExternal再生も開始用ラムダを削除した。同期・非同期の失敗処理は共通化し、再生中止を失敗へ変更しない。

## 同条件の比較

Unity 6000.3.10f1 / Windows Editor Play Mode / Mono / i5-12400F。Deep Profiling無効でクリーン再コンパイル後に計測。1秒の生成AudioClip、AudioSourceプール1、ウォームアップ128回。GC.Allocは128回×3、CPUは512回×7の中央値（マイクロベンチは4096回）。校正は空処理0B、4096B配列4128Bで一致した。

| 操作 | 前 B/回 | 後 B/回 | 前 μs | 後 μs |
|---|---:|---:|---:|---:|
| Registered.PlayStop.NoSubscriber | 1408 | 632 | 15.1 | 12.657 |
| Direct.PlayStop.NoSubscriber | 1852 | 876 | 15.86 | 13.434 |
| PreloadedReference.PlayStop.NoSubscriber | 1920 | 696 | 19.844 | 13.992 |
| Registered.PlayPauseResumeStop.NoSubscriber | 1408 | 632 | 16.168 | 13.815 |
| Registered.PlayPauseResumeStop.OneSubscriber.EnqueueOnly | 1488 | 712 | 18.24 | 15.695 |
| Registered.PlayStopAll.NoSubscriber | 2792 | 2016 | 22.756 | 19.589 |
| Registered.PlayStop.WaitAfterCompletion | 1504 | 728 | 17.308 | 12.954 |
| OrbitContext.Get.ExistingEngine | 0 | 0 | 0.382 | 0.387 |
| HeldPlayback.PauseResume.Dispatch.Subscribers0 | 0 | 0 | 0.226 | 0.246 |
| HeldPlayback.PauseResume.Dispatch.Subscribers1 | 0 | 0 | 0.825 | 0.819 |
| HeldPlayback.PauseResume.Dispatch.Subscribers8 | 0 | 0 | 1.039 | 1.086 |
| Limits.StopOldest.ReplaceExisting | 1408 | 632 | 21.069 | 15.492 |
| Limits.RejectNew.KeepExisting | 1200 | 424 | 6.572 | 3.219 |

登録済みKeyの再生→停止は1,408→632B/回（55.1%減）、10→5確保/回。事前ロード済み参照は1,920→696B/回（63.8%減）。16回の別途スタック採取でも10,112B／80確保と一致した。開始結果のTask、RunRequest／LoadPlaybackの非同期状態、操作ラムダは通常経路のスタックから消えている。

## 検証

Pipelineの簡易ランナーで関連111ケースがPlay Modeで成功（スキップなし）。Libraryロード27、直接参照3、完了待機5、状態通知8、Engine33、制限13、区間再生14、SoundPlayer8。新規ケースではロード済み参照の即時開始、Preload解放後の再生用保持、無効Sound・存在しないKeyの失敗時の保持と再ロード不要性を確認した。共有ロード、ロード中Stop、遅着リース回収、失敗後リトライ、通知順序も既存ケースで確認。失敗系テストへ期待する例外ログを追加し、区間不正テストも実行した。

Unity 2022.3.62f3および6000.2.9f1のEngineアセンブリ・.NET Standard参照でRuntimeを条件付きコンパイルできた。これは旧Editorでの実行試験ではなく、6.3未満のコンパイル互換確認。既存の未代入シリアライズフィールド等の警告は残る。

## 残る確保と制約

| 呼び出し元 | B/回 | 確保/回 |
|---|---:|---:|
| CreateRequest（Handle本体） | 312 | 1 |
| PlaybackHandleコンストラクター（同期用オブジェクト） | 16 | 1 |
| InitializeRequest（CTS） | 64 | 1 |
| LoadPlayback（PlaybackResource） | 32 | 1 |
| Start（Playback本体） | 208 | 1 |
| 合計 | 632 | 5 |

分類はスタックで最初に現れるCoreメソッド単位。公開Handleのプール化・再利用APIは導入していない。通常Clipでも要求用CTSは残っており、未ロード時のキャンセル登録やSession参照などの費用は別途発生する。

CPU時間は単一Editor環境でOS変動を含む参考値。ネットワーク・デコード・実発音遅延・DSP・Playerは未測定。生成Clipを使うため、インポート済みClipのPCMロードが実際に複数フレームにまたがる時間・負荷は今回の測定対象外。未完了準備の経路ではリースを非同期処理へ移し、終了・失敗時にfinallyで解放する。

- [変更前データ](2026-10-18-playback-limit-scenarios.json)
- [変更後データ](2026-10-18-playback-ready-path.json)
- [変更後の確保スタック](2026-10-18-playback-ready-allocations.json)
- [ソースハッシュ](2026-10-18-playback-ready-source-hashes.json)
- [ベンチマーク](../../Tools/Benchmarks/AudioOrbitBaseline.cs)
