# 命名規約

## 基本方針

Unityで管理する自作フォルダ、シーン、アセット、C#ファイル・型・公開メンバーは英語のPascalCaseにします。役割や内容が分かる名前を選び、`test`、`honban`、連番、`ver2`、用途不明の略称を完成名として使いません。型とMonoBehaviourのファイル名は一致させます。

C#のprivateフィールド、引数、ローカル変数はcamelCaseです。既存のシリアライズ済みフィールドは表記だけを理由に変更せず、必要な改名では `FormerlySerializedAs` 等で移行します。privateな補助型は通常どおりPascalCaseにします。

| 種類 | 例 |
| --- | --- |
| フォルダ | `Scripts`, `Scenes`, `Models`, `Animations`, `Materials`, `Textures`, `Shaders` |
| シーン | `Main.unity`（作品の起動シーン）、`Sandbox.unity`（描画実験） |
| アセット | `StripedFish.fbx`, `FishAnimator.controller`, `RockAlbedo.jpg` |
| C#型・ファイル | `FishAnimatedQuadBillboard.cs` / `FishAnimatedQuadBillboard` |
| 公開メンバー | `TargetCamera`, `UpdateFishColor` |
| privateフィールド等 | `targetCamera`, `baseOffset`, `deltaTime` |

通常の文書も `Docs/NamingConventions.md`、`Docs/UnityAgentSetup.md` のようにPascalCaseにします。`README.md`、`AGENTS.md`、`.vscode`、パッケージの `manifest.json` 等、ツールが指定する綴りは維持します。リポジトリ名は `under-the-sea`、製品表示名は `Under the Sea`、UWPパッケージ名は `UnderTheSea` です。

## 互換性のための例外

- Unity予約ディレクトリ（`Editor`, `Resources`, `StreamingAssets`, `Gizmos`, `Plugins` 等）はUnityが要求する綴りを使います。
- パッケージID、GUID、fileID、CloudプロジェクトID・組織ID、既存アプリ識別子は命名整理の対象にしません。
- `TutorialInfo` とその配下はUnityテンプレート由来として構成を維持します。第三者パッケージやそのキャッシュも改名しません。
- FBX内部のボーン・階層・埋め込みクリップ／マテリアル、Animatorのステート名、AnimationClipのバインディングは互換性を優先します。ファイル名変更に連動して改名しません。シーンの既存モデルインスタンス名にも元のモデル名が残る場合があります。
- Input Systemのaction map・action名や生成コード設定、シェーダーのプロパティ識別子やShaderメニュー文字列はAPI／バインディングとして扱います。
- ユーザー了承済みの仮 `Cube` は維持します。名前を変えるためにシーンの未保存変更を保存しません。

## 今回の移行

すべてUnityの `AssetDatabase.MoveAsset` で移動し、既存 `.meta` とGUIDを保持しました。

| 旧パス（Assets以下） | 新パス（Assets以下） |
| --- | --- |
| `script` | `Scripts` |
| `animate` | `Animations` |
| `import` | `Models` |
| `texture` | `Textures` |
| `Scenes/honban.unity` | `Scenes/Main.unity` |
| `Scenes/test.unity` | `Scenes/Sandbox.unity` |
| `Scenes/test/Global Volume Profile.asset` | `Scenes/Sandbox/GlobalVolumeProfile.asset` |
| `import/isidai_ver2.fbx` | `Models/StripedFish.fbx` |
| `import/rock1_fbx.FBX` | `Models/Rock.fbx` |
| `import/tikei.fbx` | `Models/Seabed.fbx` |
| `animate/isidai_ver2.controller` | `Animations/FishAnimator.controller` |
| `Materials/rock1.mat` | `Materials/Rock.mat` |
| `M_UnderwaterDistanceAttenuation.mat` | `Materials/UnderwaterDistanceAttenuation.mat` |
| `UnderwaterDistanceAttenuation.shader` | `Shaders/UnderwaterDistanceAttenuation.shader` |
| `Underwater_Caustic_Cookie.png` | `Textures/UnderwaterCausticCookie.png` |
| `rock1.jpg` | `Textures/RockAlbedo.jpg` |
| `aerial_beach_02_diff_1k.jpg` | `Textures/BeachAlbedo.jpg` |
| `InputSystem_Actions.inputactions` | `InputActions.inputactions` |
| `Settings/Mobile_Renderer.asset` | `Settings/MobileRenderer.asset` |
| `Settings/Mobile_RPAsset.asset` | `Settings/MobileRenderPipeline.asset` |
| `Settings/PC_Renderer.asset` | `Settings/DesktopRenderer.asset` |
| `Settings/PC_RPAsset.asset` | `Settings/DesktopRenderPipeline.asset` |
| `Settings/SampleSceneProfile.asset` | `Settings/SampleSceneVolumeProfile.asset` |

テクスチャの旧ファイル名は上表に出典追跡のため残しています。ビルド設定はGUIDから新しいシーンパスへ解決し直しています。新規の `Shaders` フォルダにはUnityが生成した `.meta` を含めます。

## ローカル保存名とCloud表示名

`ProjectSettings/ProjectSettings.asset` に保存する `projectName` は `Under the Sea` です。Unity 6000.3.26f1のPlayerSettingsを `SerializedObject` で取得し、このプロパティだけを更新して保存しました。保存したファイルをUnityの `InternalEditorUtility.LoadSerializedFileAndForget` で読み直し、名前の永続化とCloudプロジェクトID・組織ID・連携設定の同一性を確認しています。これはローカル保存名の変更であり、Cloudサーバー上の改名ではありません。

[公式CloudProjectSettingsのソース](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Editor/Mono/UnityConnect/CloudProjectSettings.cs)では、`projectName` はUnityConnectから現在のCloud名を取得する読み取り専用APIです。この環境では同APIの名前は空、`projectBound` はfalseでした。保存済みIDを削除したり、再バインドしたりしていません。

Cloud側は `unity auth status` と `unity cloud project list` がともに `NOT_SIGNED_IN` となり、実名・権限を照会できませんでした。CLIの `cloud project` には一覧・既定対象の選択機能がありますが、表示名更新コマンドはありません。Cloud側の変更が必要なら、対象組織にアクセスできるアカウントでサインインし、保存済みIDの既存プロジェクトを確認します。[公式の改名手順](https://docs.unity.com/en-us/cloud/projects/edit-project)はUnity DashboardのSettings → Project Details → 名前のEdit → Saveで、OwnerまたはManager権限が必要です。プロジェクトIDは改名対象ではありません。認証できていないCloud実名がローカル名と一致するとは扱いません。

ローカルに無視対象の旧名 `.slnx` やLibraryキャッシュが残る場合があります。これらは公開追跡対象ではありません。
