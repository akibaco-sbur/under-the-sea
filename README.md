# Under the Sea

球面ディスプレイ **FPV130** にプロジェクターで海中を投影する、体験型デジタルアートのUnityプロジェクトです。

## 作品コンセプト

中心となる体験は「えさやり」です。数種類の色の餌を用意し、食べた魚がその餌の色になることを構想しています。自分の行為の結果が魚の色として海の中に残り、主体感や、「別の色をあげたらどうなるか」「別の魚にも食べさせたら」という探索につなげます。

身体入力にはAzure Kinectを使う方針です。座った状態／立った状態での身体インタラクションを検討します。餌をどう選び、どう海へ差し出すかは、今後詰める重要な設計事項です。

泡、自分の影、声で魚が逃げる反応なども追加候補です。これらと上記のえさやり体験は構想であり、実装済み機能を示すものではありません。

現在の起動シーンでは、海底・岩・魚の描画、魚のアニメーション、水中光の動きを確認できます。

`Sandbox.unity` は、仮の魚18匹が餌の位置へ集まり、周囲を泳ぐデモです。クリックで餌を移動し、Fキーで表示を切り替えられます。個体ごとの速度・旋回・尾の動きと、餌がないときの遊泳を確認できます。[魚デモの操作と調整](Docs/FishFeedingSandbox.md)を参照してください。

同じシーンの `AzureKinectDemo` を有効にすると、Azure KinectのDepth入力から骨格を表示できます。RGB映像は使用しません。[SDKのセットアップと切替手順](Docs/AzureKinectSandbox.md)を参照してください。既定の魚デモはKinectを起動しません。魚の色変化・餌の消費・身体入力との接続は未実装です。

手の開閉を単体で調べるには `Assets/Scenes/Sandbox/KinectTest.unity` を開きます。Depth画像、手の計測範囲、開閉判定としきい値を確認できます。判定は実験段階であり、誤判定があります。[Kinect手の開閉テスト](Docs/KinectHandTest.md)を参照してください。

## 開発

- Unity **6000.3.26f1**（Unity Hubで同じバージョンを用意）
- Universal Render Pipeline **17.3.0**
- Git LFSが必要です。クローン後に `git lfs install` と `git lfs pull` を実行してください。
- ビルド対象シーン: `Assets/Scenes/Main.unity`。`Sandbox.unity` は魚の遊泳とAzure Kinect骨格の検証用です。既定では `FishFeedingDemo` のみ有効で、骨格デモと従来の `RenderingSamples` は非アクティブで保持しています。
- リポジトリ名は `under-the-sea`、製品表示名は `Under the Sea` です。

Unity Hubからこのフォルダーを開き、パッケージ解決とコンパイルの完了を待ちます。`Main.unity` を開いてPlayすると、現在の海中シーンを実行できます。IDE用の `.slnx` / `.csproj` は各環境で生成します。

製品名とローカルのプロジェクト表示名は `Under the Sea`、UWPパッケージ名は `UnderTheSea` です。Unity Cloud上の表示名とローカル保存名は別管理です。

エージェント操作のセットアップと確認方法は [Unity連携手順](Docs/UnityAgentSetup.md)、[命名規約](Docs/NamingConventions.md)、作業規約は [AGENTS.md](AGENTS.md) を参照してください。
