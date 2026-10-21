# Library選択履歴の弱いキーへの移行

2026-10-18。librarySelectionsをConditionalWeakTable<ISoundLibraryLoader, Dictionary<string, SelectionHistory>>へ変更し、Engineが選択履歴のキーによって不要なLoaderを延命しないようにした。公開APIは維持している。

同じLoaderインスタンスが生存している間はSequence／Shuffle／AvoidImmediateRepeatの履歴を保持する。LibrarySessionは使用中のLoaderを通常どおり強く保持するため、ロード中・再生中の寿命は変わらない。Session解放時はそのLoaderの履歴辞書を取得して内容を更新し、Engine.Dispose時はテーブルをClearする。

## 500回反復の再測定

生成LibraryのPreload→Play→Stop→Preload解放を500回反復し、同一Loader、新規Loader、Reference Factoryの3条件で確認。前回同様、Force GC後のEngine内保持数とEditor全体のメモリを採取した。メモリ測定ハーネスはDictionaryと弱いテーブルの両方を列挙できる形へ変更し、解放後の採取前にはローカル参照を外して呼び出しスタックを抜ける。以前の強いDictionaryはGC後もキーを保持するので、保持数の比較には影響しない。

| 条件 | 履歴数 変更前→変更後 | Dispose済みLoader保持数 変更前→変更後 | Session／リース 解放後 |
|---|---:|---:|---:|
| 同じLoader | 1 → 1 | 0 → 0 | 0／0 |
| 毎回新しいLoader | 501 → 1 | 500 → 0 | 0／0 |
| Reference Factory | 501 → 1 | 500 → 0 | 0／0 |

変更後は10／100／500回のすべてで履歴1件、Dispose済みLoader保持0件。残る1件はハーネスが意図的に保持しているウォームアップ用Loaderで、Engine破棄後は履歴0件。新規Loader／Factoryのいずれでも反復数に比例した増加がなくなった。再生・ロード待ち・SharedLibrary・Definition・Session・リースは解放後0を確認した。

キーの回収時期はGCに依存する。利用者がLoaderや解放済みのリースを参照し続けていれば、そのLoaderと履歴が残る場合がある。Disposeを呼ぶことと、すべての強参照を外すことは同じではない。今回の変更はEngineの履歴が不要な強参照元にならないためのもの。

## 回帰確認

関連72ケースがPlay Modeで成功（AudioLibraryLoadingTests31、AudioEngineTests33、PlaybackStateChangedTests8、スキップなし）。新規4ケースでは、同じLoaderに対してGCと再ロードを繰り返してもSequenceの順序・Shuffleの一巡・AvoidImmediateRepeatの直前回避が継続することと、Engineを生存させたまま破棄したLoaderのWeakReferenceが回収されることを確認した。共有LibraryのSession移譲、遅着ロード回収、失敗後の再試行、Engine破棄、通知順序も既存ケースで確認した。

GC回収テストは生成用メソッドをNoInliningにし、Monoの保守的GCが生成スタックの一時参照を拾わないよう、非同期でスタックを抜けてからGCする。回収前にWeakReference.Target／IsAliveを読まない形で確認している。

Unity 2022.3.62f3／6000.2.9f1の参照アセンブリによるRuntime条件付きコンパイルも成功。既存の未代入シリアライズフィールド・未使用フィールド警告は残る。これは旧Editorでの実行試験ではない。

## 制約と残る候補

測定はUnity 6000.3.10f1 / Windows Editor Play Mode / Mono、生成Clipと同期テストLoader。Editor総メモリの変動をLoader単体の削減バイト数として扱わない。実際のAddressables／ResourcesのNativeアンロード、Player、IL2CPPでのGC時期、長期のテーブル内部容量は未測定。今回は強参照による保持件数と履歴の挙動を検証したもので、CPUの改善率は測定していない。

次の候補は多数のAsyncフェード停止で生成する待機Taskの削減。Library参照からSessionを探す走査の索引化も引き続き別タスクとして残る。

- [変更前データ](2026-10-18-library-memory-cycles.json)
- [変更後データ](2026-10-18-library-weak-history-cycles.json)
- [測定スクリプト](../../Tools/Benchmarks/AudioOrbitScaling.cs)
- [ソースハッシュ](2026-10-18-library-weak-history-source-hashes.json)
