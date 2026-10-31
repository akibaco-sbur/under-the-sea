# 開発用のキー操作

MainシーンでPlayするとDiverKeyboardInputを自動生成します。Gameビューをクリックしてキー入力を受け取れる状態にしてください。VoskOfflineVoiceCommandsの「Development controls」にある「開発用キー操作」をONにしてください（初期値OFF）。Inspectorの音声イベントへの追加設定は不要です。

| キー | 操作 |
| --- | --- |
| ↑ / ↓ / ← / → | 上 / 下 / 左 / 右を向く。1回押すごとに音声操作と同じ角度だけ変更 |
| B | 体ごと後ろを向く |
| I | 前進開始。見ている方向へ体を向けてから加速し、進み続ける |
| Space | 停止。音声と同じ慣性で減速 |
| R | 撤退。シャットダウン後、俯瞰マップでルートを再生 |
| Space（ルート画面） | ルートをもう一度再生 |

押しっぱなしで視点変更を繰り返す方式ではありません。キー操作のON/OFFはVoskOfflineVoiceCommandsの「開発用キー操作」で切り替えます。餌やりのKeyboard Feeding Test Modeとは独立しています。音声認識の読み込みエラーなどでVoskコンポーネントが停止しても、このチェックがONならキー試験は続けられます。音声入力も併用できます。既存の餌やり試験用F/WASD/EQ/Shift/Backspaceはそのままです。

認識待ち・起動演出中・撤退後は移動や視点変更を受け付けません。Kinectなしで認識画面を通過するには、従来どおりKeyboard Feeding Test Modeを有効にしてEnterを押します。

無効化するにはVoskOfflineVoiceCommandsの「開発用キー操作」をOFFにします。Play中も切り替え可能で、画面内のキー案内も追従します。設定を保存したい場合はPlay前に変更してください。

Unityを起動せず、参照DLLによるC#コンパイルを実施しました。Gameビューでの入力と表示は未確認です。

