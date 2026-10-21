# Shuffle・Library検索・Engine取得の効率化（2026-10-20）

公開APIを変えず、繰り返しの配列生成と全走査を削減した。再生の選択方式、Library共有・キャッシュ解放、Contextの既存Engineの寿命を維持する。

## 測定条件

- Unity 6000.3.10f1、Editor Play Mode / Mono。Deep Profilingなし。
- 128回のウォームアップ後、CPUはProfiler記録を無効化して2048回×5試行。表は1操作あたりの中央値。
- Allocは固有ProfilerMarker配下のGC.Allocメタデータを128回×3試行で合算。Editor側コードの記録を有効化し、計測後にProfiler設定を復元した。記録フレームの確定を待ってから読み取る。
- 校正は前後とも4096バイト配列が4128B、空操作が0B。初回のマーカー未取得試行は結果に採用していない。
- 選択方式の測定は事前生成したデリゲートを呼ぶ。各操作にReflectionやデリゲート生成の確保は含めない。旧実装のChoose＋Array.IndexOfと、新実装のChooseClipIndexを比較する。AudioSourceの再生・停止全体の時間ではない。
- Libraryの測定はロード済み参照にPreloadAsyncを再度呼ぶ経路。最後に登録したSessionを取得し、I/Oは発生しない。
- Engineの測定は最後に定義したKeyに対するOrbitContext.Get。Engine作成・索引初期化は測定外。
- 初回のShuffle配列・Context索引・共有Libraryの所有者集合はロード時等に確保する。定常操作の確保と区別する。
- Player／IL2CPP、実I/O、DSP負荷は対象外。前後とも同じベンチマークと同じEditor環境で測定した。

## 結果

| 操作 | Alloc／回（前 → 後） | CPU中央値／回（前 → 後） |
|---|---:|---:|
| Shuffle・8 Clip | 27 → 0 B | 0.323 → 0.164 μs |
| Shuffle・64 Clip | 15.375 → 0 B | 0.799 → 0.200 μs |
| Sequence・256 Clip | 0 → 0 B | 1.143 → 0.140 μs |
| ロード済みLibrary再取得・1 Session | 304 → 304 B | 2.100 → 1.991 μs |
| ロード済みLibrary再取得・128 Session | 304 → 304 B | 12.665 → 1.933 μs |
| 既存Engine取得・1定義 | 0 → 0 B | 0.409 → 0.257 μs |
| 既存Engine取得・128定義 | 0 → 0 B | 20.662 → 0.281 μs |

Shuffleの割り当ては一巡で発生する確保を1操作へ平均した値。初期化後は再抽選でも配列を再利用し、追加Allocは0Bとなった。Library再取得の304Bは既存の非同期待機用Task・一時保持に由来し、今回の検索索引化では削減していない。

数値は切り出した操作の比較であり、再生全体やアプリ全体が同じ割合で高速化することを意味しない。

## 実装

- Shuffle：候補Indexを既存配列へ詰め直してからFisher–Yatesで並べ替える。一時Listと毎巡のToArrayを削除。Weightが0のClipを除外し、一巡するまでの重複を防ぐ動作を維持する。
- ClipIndex：選択時にIndexを返し、その値をPCM準備の非同期経路にも渡す。再生開始時のArray.IndexOfを削除。同じAudioClipを複数候補が共有しても候補ごとの設定・Indexを維持する。
- Library：参照ごとのReferenceLoadに保持先Sessionを記録し、全SessionとShared一覧の探索を省く。共有Libraryは所有者Sessionの集合を持ち、解放時の引き継ぎ先を一度選んで各定義へ適用する。複数Loaderの同一Library共有と重複Libraryを含む失敗時の巻き戻しを維持する。
- Engine：KeyからEngineDefinitionを引く索引を初回に構築し、通常のGetでは全定義の走査・重複検証を繰り返さない。OnValidate、定義配列の交換、ContextのEngine解放時に索引を無効化する。検証は再構築時に実施し、失敗した索引を有効扱いにしない。InspectorでKeyを変更しても取得済みEngineは破棄しない。

## 検証

- Play Mode：149ケース成功。Shuffleの複数巡・Weight除外・明示Clip選択、各選択方式のClipIndex、共有Library所有者の解放、重複ロードの巻き戻し、再ロード時の選択履歴、既存Handle・区間再生・一括停止・AudioPlayer・Context。
- Edit Mode：40ケース成功。Inspectorと同じSerializedObjectによるKey編集、Contextのプレビュー・寿命、Resources参照、Editorプレビュー。
- 延べ189ケース。コンパイルはエラー・警告0件。

## 再実行

[ベンチマーク](../../Tools/Benchmarks/SelectionLookupBenchmark.cs)をPlay Mode、Deep Profilingなしで実行する。KeyやSessionの最終登録位置、初期化済み状態などの条件もコードに保存している。

[変更前JSON](2026-10-20-selection-lookup-before.json) / [変更後JSON](2026-10-20-selection-lookup-after.json)
