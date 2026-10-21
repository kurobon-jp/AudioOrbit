# AudioPlaybackの実装と最小比較

2026-10-19（JST）。再利用可能なAudioPlaybackをCoreへ実装した。Playerビルドや追加の広範な性能調査は行っていない。

## API

```csharp
using var playback = engine.CreatePlayback(soundReference);
playback.StateChanged += (player, state) => Debug.Log(state);

var firstRun = playback.Play();
await playback.StopAsync(0.2f);
var secondRun = playback.Play();
await playback.StopAsync();
```

登録済みKey用のCreatePlayback(string)と、Direct／Resources／Addressables参照用のCreatePlayback(ISoundReference)を提供する。既存engine.Playは維持。作成時Idle、PlayでLoadingから通知し、再生前に購読できる。同じインスタンスの再生中のPlayは明示的に例外とし、同時に鳴らす場合は別インスタンスを作る。

各Playは独立したPlaybackHandleを返す。Currentは直近のHandle。WaitForCompletionAsyncとStopAsyncは呼び出し時の再生回を対象とし、次のPlayで以前の完了結果や待機を上書きしない。Disposeは自分の再生を停止して通知を破棄する。Play前の完了待機は例外。古い再生回の未配信通知は次のPlayで破棄し、イベントには通知生成時の状態を渡す。

内部Playbackを1個保持し、再生開始時に全ての再生設定・フェード・停止・Pause状態を再設定して使う。終了時にはSource／Lease／Generator／Owner／Targetなどの参照を外す。Loader・制限・LoopRegionClip・フェード・一括停止は既存の経路を共有する。各回のHandle・CTS・リソース保持オブジェクトは独立させている。

## 検証

Play Modeで100ケース成功（新規AudioPlayback13、Engine33、Direct3、一括停止23、状態通知8、完了待機5、Region15）。再生前購読、古いHandle／待機の独立性、Pause／Fade／Loopのリセット、複数インスタンス、キャンセル、Engine破棄、古いキュー通知の抑制、Dispose、失敗からの再試行、Ended通知からの次のPlay、遅着ロード、ループ区間の再利用を確認した。Unity Pipelineの既存簡易ランナーで実行した。

Unity 2022.3.62f3／6000.2.9f1の参照アセンブリでRuntimeの条件付きコンパイルも成功。既存の未代入シリアライズフィールド等の警告は残る。これは旧Editorでの実行試験ではない。

## 小規模な性能比較

Unity 6000.3.10f1 / Editor Play Mode / Mono / 12th Gen Intel(R) Core(TM) i5-12400F。同じ登録済みループClip、Sourceプール1、購読なし。ウォームアップ128回、CPU512回×7、GC.Allocのメインスレッドメタデータ128回×3。AudioPlaybackの作成費用は含めない。4096B配列4128B／空処理0Bの校正を確認した。

| 操作 | B/回 | 確保/回 | CPU中央値 μs |
|---|---:|---:|---:|
| engine.Play→Stop | 632 | 5 | 13.775 |
| AudioPlayback.Play→Stop | 424 | 4 | 14.889 |

208B／1確保を削減（32.9%減）。内部Playbackの再利用分に一致する。残り424Bは主にHandle本体312B・完了同期用16B・CTS64B・PlaybackResource32B。古いHandleを再利用しないことで、その操作や待機結果が新しい再生へ混入しない。

CPU中央値はわずかに増えており、高速化の結果とは扱わない。要求とAudioPlaybackの対応付けなどの管理が加わる。単一Editor条件の参考値で、Player／IL2CPP／I/O／DSPは測定していない。通知付き連続再生のAllocも今回の比較対象外。今回は機能の実装と最小限の比較で区切った。

- [生データ](2026-10-19-audio-playback.json)
- [最小比較スクリプト](../../Tools/Benchmarks/AudioPlaybackComparison.cs)
- [ソースハッシュ](2026-10-19-audio-playback-source-hashes.json)
