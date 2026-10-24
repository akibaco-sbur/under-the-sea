# Azure Kinect Sandbox

`Assets/Scenes/Sandbox.unity` でAzure Kinect DKの骨格を表示します。Mainとビルドシーンの指定は変更しません。従来のSandboxの描画オブジェクトは `RenderingSamples` 配下で非アクティブにしています。

## セットアップ

- Windows x64、USB 3接続とKinect外部電源、Visual C++ 2015–2022 x64ランタイムが必要です。デバイスマネージャーでAzure Kinect Depth Cameraが認識されることを確認します。ドライバーがない環境ではMicrosoft公式Sensor SDKのセットアップを行ってください。
- Unityは6000.3.26f1、URPは17.3.0です。GPU推論の既定はDirectMLで、CUDA/cuDNNの導入は不要です。InspectorのProcessing BackendでCPUも選べます（低速になる場合があります）。
- プロジェクトルートでPowerShellから実行します。

  ```powershell
  ./Tools/SetupAzureKinect.ps1
  ```

  Microsoft公式NuGetからSensor 1.4.1 / Body Tracking 1.1.2 / ONNX Runtime 1.10.0を取得し、SHA-256を検証して `.Local/AzureKinect/Runtime` にDLLとLiteモデルを配置します。Microsoft公式Unityサンプルと同じ互換バージョンの組み合わせです。グローバルSDKインストールやUnityパッケージ追加は必要ありません。ダウンロードには数百MBの空き容量が必要です。

  `.Local` はGit無視対象です。SDK、ONNXモデル、ログ、計測した関節データはコミットしません。別のチェックアウトでもセットアップを実行してください。

## 起動と表示

1. 他のKinect Viewerを終了し、Sandboxを開いてPlayします。
2. 初回のBody Tracker作成には数秒かかります。HUDに現在の状態・理由、Depth取得数、骨格フレーム数、人数、センサー時刻、破棄数を表示します。
3. センサー前方1〜4mに立ち、腕や身体を動かします。同じBody IDを同じ色・同じ表示オブジェクトに対応付けます。
4. Stopはカメラ／Trackerを停止し、骨格を消します。Start / Retryで再開します。デバイスが開けない場合は理由を表示し、5秒間隔で再試行します。SDK未配置・デバイス未接続でもEditorのフレームは継続します。

入力はNFOV Unbinned、15 FPS、Color Offです。RGB、音声、録画ファイルは取得しません。表示はSDKの32関節で、既定ではconfidenceがMedium/Highの関節と、両端が有効なボーンだけを表示します。身体未検出の結果では以前の身体を消し、結果が1秒以上更新されないと全骨格を隠します。Body IDが変わると旧表示を破棄します。

座標はDepthカメラ原点です。SDKのミリメートル値 `(x, y, z)` をUnityのメートル値 `(x, -y, z) * 0.001` に変換します。右が+X、上が+Y、センサーから遠ざかる向きが+Zです。ミラー表示・床検出・部屋座標へのキャリブレーション・アバターリターゲットは行いません。センサーは正立させて使用してください。

## リソースと停止

`AzureKinectNative` はMicrosoft C APIの最小バインディングです。Sensor 1.4.1の校正構造体を変更せず渡し、DeviceConfiguration=36、Calibration=1032、TrackerConfiguration=24、Joint=32、Skeleton=1024バイトを起動時に照合します。SDKの管理DLLをUnityへ追加する必要はありません。

Depth取得と推論は専用ワーカーで動作し、Unityオブジェクトにはメインスレッドからだけ触れます。capture待ちは50ms、enqueue/popは0ms、推論中入力は最大2件です。結果は最新1スナップショットだけを保持します。capture/frameは各取得のfinallyでreleaseし、停止はshutdownでSDK待ちを解除 → worker終了 → tracker destroy → camera停止／device closeの順です。

Tracker作成にはキャンセルAPIがありません。初期化中の手動Stopは2秒後も終了していなければ待機理由を表示し、二重起動を防ぎます。Play終了／assembly reloadは、初期化と後始末の完了を待ってから進みます。その間、Editorの終了操作が数秒待つ場合があります。ネイティブDLLはプロセス終了までロードを維持し、繰り返しPlayでは既存モジュールを再利用します。

## 実機検証（2026-10-24）

- 接続Azure Kinect DK、RTX 4070 Laptop環境でDirectMLを使用。Depthフレーム、骨格フレーム、センサー時刻が更新し、人物1人を検出しました。
- 同じBody IDの右手首座標が変化し、両時点でMedium以上として表示。腕の動きと骨格表示を確認しました。個別の関節座標・画像はローカルに保持し、公開許可待ちです。
- 手動停止で人数・表示が0になり、初期化中Stop、初期化中OnDisable→再有効化、停止後再Playでも入力が再開しました。再有効化後にはDepth 472、骨格465フレーム・人物1人を取得。
- SDK未配置パスでは理由を表示してワーカー終了。物理USBの抜き差し、複数人、CPUバックエンド、Standaloneビルドは未検証です。
- 新規ワークツリーの初回PlayでUnity Searchの初期化例外が発生しました。公式ソースで、Play中は未作成の既定インデックスを作れないことを確認。Edit Modeで既定インデックスを準備してからConsoleをクリアし、再Playでは新規Error 0・Warning 0でした。ローカルのUserSettingsキャッシュだけの準備で、検索無効化や回避コードは加えていません。
- UnityコンパイルはError 0・Warning 0。Mainおよび既存の作業チェックアウトのユーザー変更は保持。公開画像は骨格Game ViewだけでRGB画像を含みません。

## 公式資料

- [Microsoft Unity Body Tracking Sample](https://github.com/microsoft/Azure-Kinect-Samples/tree/master/body-tracking-samples/sample_unity_bodytracking)
- [Sensor SDK C API](https://microsoft.github.io/Azure-Kinect-Sensor-SDK/master/index.html)
- [Body Tracking SDK 1.1](https://microsoft.github.io/Azure-Kinect-Body-Tracking/release/1.1.x/index.html)
- [Tracker shutdown](https://microsoft.github.io/Azure-Kinect-Body-Tracking/release/1.1.x/group__btfunctions_ga94036969ef94cbc414c78b3f6d04bfa5.html)
- [Unity Searchの既定インデックス処理](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Modules/QuickSearch/Editor/Indexing/SearchDatabase.cs)
