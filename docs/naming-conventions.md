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

`README.md`、`docs`、パッケージの `manifest.json` 等、一般的な文書・ツール規約は機械的に改名しません。リポジトリ名は `under-the-sea`、製品表示名は `Under the Sea`、UWPパッケージ名は `UnderTheSea` です。

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

## 残存する旧Cloud名

`ProjectSettings/ProjectSettings.asset` の `projectName: Test_2026_shiba` は既存Cloud連携由来のメタデータです。既存のCloudプロジェクトID・組織IDが記録されていますが、現在のEditor APIが返すCloud名は空で、サーバー上の実名をこの検証では確認できていません。ローカルだけで別のCloud名を捏造せず、ID・連携状態とともに保持しています。Cloud側の改名を行う場合は、対象アカウントとプロジェクトを確認して別途実施します。

ローカルに無視対象の旧名 `.slnx` やLibraryキャッシュが残る場合があります。これらは公開追跡対象ではありません。
