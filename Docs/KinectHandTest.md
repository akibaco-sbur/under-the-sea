# Kinect 手の開閉テスト

`Assets/Scenes/Sandbox/KinectTest.unity` は、Azure Kinect の右手の開閉を単体で調べるためのシーンです。餌や魚の処理には接続していません。右手の位置は Kinect の骨格、開閉は RGB 画像の MediaPipe Gesture Recognizer で推定します。

## 操作

1. [Azure Kinect のセットアップ](AzureKinectSandbox.md)に従い、SDK と Kinect を用意します。
2. Python をインストールした Windows で `Tools/SetupMediaPipe.ps1` を実行します。MediaPipe と公式モデルを `.Local/MediaPipe` に配置します。
3. `KinectTest.unity` を開いて Play し、Kinect の前で右手を見せます。キーボード操作は不要です。
4. 開く・握る・手を動かす操作を行い、手の画像、状態、数値を確認します。

左は depth 画像、中央は右手の depth 拡大図と RGB 画像です。depth の水色は面積スコアの計算に使った画素、RGB の水色の点は照合できた手の関節点です。画像はローカルのメモリ内で処理し、保存・外部送信しません。

右側は RGB の `OPEN / FREE`、`CLOSED / GRAB`、`UNKNOWN` と判定スコアです。灰色が OPEN、緑が CLOSED、暗い灰色が UNKNOWN です。Kinect の右手を RGB 座標へ投影して近くの手と照合し、ジェスチャースコアが 65% 未満、対応する手がない、または結果が古い場合は UNKNOWN にします。手の開閉をキーボードで調整する必要はありません。

比較用に以前の depth 面積推定も表示します。`Weighted area` は生のスコア、`Filtered area` は平滑化した値、`Open baseline` はパーと仮定した基準、`Area ratio` は平滑化値を基準で割った割合です。`Selected pixels` と `Tip-side pixels` はそれぞれ採用した画素数と手首から遠い側の画素数です。depth 推定は開始直後の約 0.4 秒を開いた手と仮定します。

画素数は奥行きによる大きさの変化を補正し、手首から遠い側を重く扱います。基準に対して 88% 未満が約 0.2 秒続くと CLOSED、96% を超える状態が約 0.2 秒続くと OPEN になります。移動速度が 0.35 m/s を超える間は状態を維持し、OPEN 中は基準も更新します。画面には現在の判定理由と手の距離・速度も表示します。

## 制約

開閉は Azure Kinect 標準 API ではなく MediaPipe による推定です。RGB が暗い、手が小さい・隠れる、対応付けが不正確な場合には認識できません。depth の面積推定は肩などと重なると誤判定し、移動中の基準更新にも影響されます。どちらも実機での精度検証が必要です。餌やりとの統合は含めていません。
