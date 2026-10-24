# Unity公式エージェント連携

## 構成

シェル操作が可能なCodexでは **Unity CLI → Unity Pipeline → ローカルEditor** を使います。`unity command` / `unity eval` が公式の直接操作経路です。旧 `com.unity.ai.assistant` 内蔵MCPは不要で、Unity AIの契約もこの連携には不要です。Pipelineは実験的パッケージのため、更新時には再検証してください。

2026-10-24に公式配布先・レジストリで確認したバージョン:

| 対象 | バージョン |
| --- | --- |
| Unity Editor | 6000.3.26f1（プロジェクト指定） |
| Unity CLI | 1.0.0-beta.14（確認時の最新beta） |
| com.unity.pipeline | 0.9.1-exp.1（確認時の最新） |
| 公式Codexプラグイン | 0.1.8-beta |

Pipelineとその依存関係は `Packages/manifest.json` / `packages-lock.json` で管理します。CLIとCodexプラグインはマシン側へのインストールです。

## Windowsでのセットアップ

1. Unity Hubで指定Editorを用意し、Git LFSの実体を取得してこのプロジェクトを開きます。
2. PowerShellで [公式インストーラ](https://unity.com/install.ps1) を確認して実行します。

   ```powershell
   irm https://unity.com/install.ps1 | iex
   ```

   ターミナルを開き直し、次を確認します。既存のCodexプロセスでPATHが更新されていない場合、`& "$env:LOCALAPPDATA/Unity/bin/unity.exe" --version` で確認できます。

   ```powershell
   unity --version
   unity self-update --check --format json
   ```

3. 公式Codexプラグインを導入します。

   ```powershell
   codex plugin marketplace add Unity-Technologies/unity-agent-plugin
   codex plugin add unity@unity-agent-plugin
   codex plugin list
   ```

   `unity@unity-agent-plugin` が `installed, enabled` であることを確認し、新しいCodexセッションでスキルを読み込みます。プラグインが対応していない環境では、CLIの `unity skill install codex --dry-run` で配置先を確認してから `unity skill install codex` を使う公式代替経路があります。プラグインとCLI同梱スキルの重複導入は避けます。Windowsのこの環境ではプラグインの登録に成功しました。

4. このプロジェクトのルートでPipelineと接続を確認します。

   ```powershell
   $projectPath = (Get-Location).Path
   unity pipeline list-versions --format json
   unity pipeline install --project-path "$projectPath"
   unity status --project-path "$projectPath" --format json
   unity command editor status --project-path "$projectPath" --caller plugin --skill unity-cli --format json
   unity command script eval 'return UnityEngine.Application.unityVersion;' --project-path "$projectPath" --caller plugin --skill unity-cli --format json
   ```

   クローンにはPipelineの指定が含まれています。既に同じバージョンがあればインストール操作は不要です。意図的な更新時にだけ `unity pipeline upgrade --project-path "$projectPath"` を使い、ロックファイルと検証結果をレビューします。

5. スクリプトやパッケージ変更をコンパイルします。

   ```powershell
   unity recompile --project-path "$projectPath" --format json
   ```

   終了コードと返された診断の両方を確認します。テスト実行は `unity command --query test` で対象Editorのコマンドを発見し、必要なテストだけを実行します。`unity test` はバッチEditorを起動するため、同一プロジェクトを開いたEditorとの競合に注意してください。

## 接続できないとき

- `STATUS_PIPELINE_LOAD_PENDING`: Unityを前面に出し、パッケージ解決・コンパイルを待ちます。
- Safe Mode: Editorログのコンパイルエラーを確認します。Pipelineが起動しないため、接続不能を成功とみなしません。
- 制限付きシェルでは実行中プロセスやlocalhostが見えない場合があります。必要な権限で同じ対象を確認し、別プロジェクトのEditorへ切り替えないでください。
- ポートは自動検出に任せます。確認時はlocalhost:7800でした。外部公開のためのファイアウォール変更は不要です。
- Unityへのバグ報告はログを送信する操作です。ユーザーの依頼なしに送信しません。

## この環境での検証（2026-10-24）

- CLIの自己更新確認とPipelineのレジストリ照会で上記バージョンが最新であることを確認。
- `codex plugin list` で公式プラグインが `installed, enabled`。この実行中セッションでは取得した公式スキルを直接読んで使用。新しいセッションでの自動ロードは別途確認が必要。
- Pipeline導入時に実際のEditorがコンパイルし、その後 `ready`。`pipeline list` でlocalhostへの到達を確認し、`editor status` / `script eval` の実行に成功。
- 以下は[命名整理](NamingConventions.md)後の再検証結果。以前の `honban` での検証とは別に実施。
- `unity recompile` は `completed`、`compilationFailed: false`、エラー0・警告0。
- 移動24件のGUIDを再照合して不一致0。`Main.unity` の21オブジェクトと `Sandbox.unity` の5オブジェクトにMissing Scriptは0。Preview Sceneで確認し、ユーザーのアクティブシーンを切り替えていない。ビルド設定のMainのパス・GUIDは一致。
- 全シリアライズGUIDを広く照合すると未解決11件あり。`DefaultVolumeProfile.asset` の旧Volume型4件と `DesktopRenderer.asset` の描画デバッグ用7件で、いずれも改名前の同一アセットに同じGUIDが存在した。今回の移動による参照断裂は見つかっていないが、既存の未解決参照は別途整理が必要。
- `Main.unity` でPlay Modeを実行。`job wait` のフレーム変化条件が成立（2401 → 2417）。別時点でも2575フレーム／18.062秒 → 9369フレーム／65.465秒へ進行。
- 魚のAnimator状態時間が0.461→0.946へ変化し、SkinnedMeshRenderer `body`（252頂点）の比較では554座標成分が変化。水中光のcookie offsetも `(12.248, -12.248)` → `(14.902, -14.902)` と変化。
- 開始前のConsoleを `Logs/main-pre-play-console.json` に退避してクリア。Play中の新規エラー0・警告0を、バッファとEditor Consoleの両方で確認（`Logs/main-play-errors.json`）。Playを終了し、Run In Backgroundの一時上書きを解除。アクティブシーンはMainで未保存変更なし。
- Game ViewのPNGを2枚目視し、魚・岩・海底が描画され、時間による光の変化を確認。上部のCubeはユーザー確認済みの仮オブジェクトとして維持。
- gh 2.95.0は添付非対応のため、公式gh 2.103.0をSHA-256検証して一時利用。元リポジトリは読み取り権限のため直接添付できず、fork側の[証跡コメント](https://github.com/shogo0x2e/under-the-sea/issues/1#issuecomment-6383198818)へ `gh issue comment --attach` でアップロードし、同じ画像をDraft PR本文に表示。
- 改名後Mainの[Game View 約18秒](https://github.com/user-attachments/assets/fd7cb8ff-7eb3-4ed2-ad0c-7e139f6dd0e5) / [約65秒](https://github.com/user-attachments/assets/a152d06c-b163-46f3-b87a-614f8b4d437a)。デスクトップやEditor周辺UIは含まない。
- えさやり、Kinect実機、FPV130投影、プレイヤービルドは今回未検証。
- ローカルのPlayerSettings保存名を `Under the Sea` に統一し、保存・UnityデシリアライズAPIによる再読み込み後も同じ値であることを確認。Cloud ID・組織ID・連携設定は変更なし。Cloud側の照会はCLI未サインイン（`NOT_SIGNED_IN`）のため未確認。詳細は[ローカル保存名とCloud表示名](NamingConventions.md#ローカル保存名とcloud表示名)を参照。

## 公式資料

- [Unity CLIの導入](https://docs.unity.com/en-us/unity-cli/use-unity-cli)
- [旧MCPと直接コマンドの位置付け](https://docs.unity.com/en-us/unity-cli/replace-mcp-server-unity-cli)
- [Unity Pipeline](https://docs.unity.com/en-us/unity-cli/unity-pipeline/unity-pipeline-package)
- [Codex公式プラグインの導入](https://docs.unity.com/en-us/ai/unity-plugin/codex)
- [公式スキルのソース](https://github.com/Unity-Technologies/unity-agent-plugin)
