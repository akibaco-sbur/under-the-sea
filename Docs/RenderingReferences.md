# 描画アセットの参照管理

Unity 6000.3.26f1 / URP 17.3.0の描画設定は、`Assets/Settings/DefaultVolumeProfile.asset` と `DesktopRenderer.asset`、URPのGraphics Settingsで管理します。パッケージを更新するときは、GUIDだけでなく欠損型サブアセットも検査します。

## Volume Profile

有効なVolumeComponent 20件を使用しています。色調整、Bloom、DepthOfField、ProbeVolumesOptions、既存のVolumetricFogVolumeComponent等のシリアライズ値は維持しています。

以下は読み込めない旧型として除去した参照です。現在のパッケージ・プロジェクト内に対応型はなく、VolumeProfileの `OnEnable` でも無効なコンポーネントがリストから除かれるため、描画に利用されていませんでした。GUIDを似た型へ置き換えて別の効果を作ることはしていません。

| 旧GUID | 保存されていた型名 | 判断 |
| --- | --- | --- |
| `60f3b30c03e6ba64d9a27dc9dba8f28d` | OutlineVolumeComponent | `Enabled=0` の旧カスタム効果。対応型・Renderer Featureなし |
| `0fd9ee276a1023e439cf7a9c393195fa` | TestAnimationCurveVolumeComponent | テスト用カーブ値の旧型。現在のランタイム型なし |
| `5a00a63fdd6bd2a45ab1f2d869305ffd` | OasisFogVolumeComponent | `Density=0` の旧カスタムFog。現在のVolumetricFogとは別型で、現行Fogの設定は保持 |
| `74955a4b0b4243bc87231e8b59ed9140` | TestVolume | `param=123` の旧テスト型。現在のランタイム型なし |

同じProfileには、`m_Script.fileID=0` かつ `Unity.RenderPipelines.Core.Editor.Tests` 由来の `CopyPasteTestComponent1/2/3`、`VolumeComponentSupportedEverywhere`、`VolumeComponentSupportedOnAnySRP` も残っていました。この5件も読み込めないEditorテスト用サブアセットとして整理しています。

除去には [AssetDatabase.RemoveScriptableObjectsWithMissingScript](https://docs.unity.com/ja-jp/engine/6000.3/script-reference/unityeditor/assetdatabase/removescriptableobjectswithmissingscript) を使用し、Profileは再インポート・保存しました。欠損型は9件から0件、有効な20件の値はEditorJsonUtilityで変更前後一致を確認。ProfileのGUIDと正常サブアセットは保持しています。

## Adaptive Probe Volumes

`DesktopRenderer.asset` の旧 `probeVolumeResources` フィールドはURPの `Deprecated.cs` で廃止扱いです。現在はGraphics Settingsの `ProbeVolumeDebugResources` と `ProbeVolumeRuntimeResources` から取得します。旧フィールドを空にし、現行の参照は変更していません。

| 旧GUID | 旧フィールド／期待型 | 現在の供給元 |
| --- | --- | --- |
| `e5c6678ed2aaa91408dd3df699057aae` | probeVolumeDebugShader / Shader | ProbeVolumeDebugResources.probeVolumeDebugShader |
| `03cfc4915c15d504a9ed85ecc404e607` | probeVolumeFragmentationDebugShader / Shader | ProbeVolumeDebugResources.probeVolumeFragmentationDebugShader |
| `53a11f4ebaebf4049b3638ef78dc9664` | probeVolumeOffsetDebugShader / Shader | ProbeVolumeDebugResources.probeVolumeOffsetDebugShader |
| `8f96cd657dc40064aa21efcc7e50a2e7` | probeVolumeSamplingDebugShader / Shader | ProbeVolumeDebugResources.probeVolumeSamplingDebugShader |
| `57d7c4c16e2765b47a4d2069b311bffe` | probeSamplingDebugMesh / Mesh | ProbeVolumeDebugResources.probeSamplingDebugMesh |
| `24ec0e140fb444a44ab96ee80844e18e` | probeSamplingDebugTexture / Texture2D | ProbeVolumeDebugResources.numbersDisplayTex |
| `b9a23f869c4fd45f19c5ada54dd82176` | probeVolumeBlendStatesCS / ComputeShader | ProbeVolumeRuntimeResources.probeVolumeBlendStatesCS |

現行リソースは計9フィールドを取得し、すべてPackages内の実アセットへ解決できました。通常の描画用シェーダー、PostProcessData、Renderer Features 3件（SSAO無効、VolumetricFog有効、FullScreenPass有効）とその設定、RendererのGUIDは保持しています。旧参照の整理にEditor／URPの更新は必要ありません。

実装根拠: [URP Deprecated.cs](https://github.com/Unity-Technologies/Graphics/blob/6000.3/staging/Packages/com.unity.render-pipelines.universal/Runtime/Deprecated.cs)、[現行ProbeVolumeリソース](https://github.com/Unity-Technologies/Graphics/blob/6000.3/staging/Packages/com.unity.render-pipelines.core/Runtime/Lighting/ProbeVolume/ProbeVolumeGlobalSettings.cs)、[VolumeProfileの欠損型処理](https://github.com/Unity-Technologies/Graphics/blob/6000.3/staging/Packages/com.unity.render-pipelines.core/Runtime/Volume/VolumeProfile.cs)。実際の判断にはプロジェクトが解決したURP 17.3.0の同名ソースも照合しています。

## 検証方法

- 非ゼロGUIDをAssetDatabaseで解決し、Built-inの予約GUIDを区別する。解決結果は0件の欠損。
- `GetScriptableObjectsWithMissingScriptCount` でGUIDがゼロの欠損型も検査する。DefaultVolumeProfileは0件。
- シーンはPreview Sceneで開いてMissing Scriptとビルド参照を確認する。Main 21オブジェクト・Sandbox 5オブジェクトで0件、MainのビルドパスとGUIDが一致。
- 正常Volume値・Renderer Feature値の変更前後一致を確認し、Mainでフレーム・時間・魚・水中光の進行、Consoleの新規エラー、Game Viewを確認する。現在の証跡は [Unity連携手順](UnityAgentSetup.md) を参照。
