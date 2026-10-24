# このプロジェクトでの作業

- 作品の構想と実装状況はREADMEを参照。依頼なしにえさやりやKinect連携の本実装へ広げない。
- Unityバージョンは `ProjectSettings/ProjectVersion.txt` に従う。依頼なしにEditorやURPを更新しない。
- 作業開始時にGit状態を確認し、ユーザーの未コミット変更を上書き・巻き戻し・無断コミットしない。作業ブランチを使う。
- Unity公式の `unity-cli` スキルを読んで操作し、パッケージ変更では `unity-package-management` も読む。セットアップは `docs/unity-agent-setup.md` に従う。
- シェルを使えるエージェントはUnity CLI + Unity Pipelineを使う。旧AI Assistant内蔵MCPや追加のMCPサーバー、Unity AI契約を前提にしない。
- Editor操作は必ず `--project-path` でこのチェックアウトの絶対パスを指定する。まず `unity status` で接続状態を確認する。
- `unity command` には `--caller plugin --skill unity-cli`（別スキルに従う場合はそのスキル名）を渡す。実際のコマンド・引数は接続したEditorから調べる。
- 接続中はシーン・Prefab・アセットの変更にEditor APIを使い、YAMLを直接編集しない。未保存のシーン変更があれば保護し、検証のために勝手に保存・切り替えしない。
- アセットの移動・改名には `AssetDatabase.MoveAsset` 等を使い、`.meta` とGUIDを保持する。シーン、Prefab、ビルド設定、文字列のパス参照を確認する。
- MonoBehaviourのクラス名とファイル名を一致させる。シリアライズ済みフィールドを改名するなら `FormerlySerializedAs` 等で移行する。命名目的だけの大規模リファクタリングはしない。
- `Assets/script` と既存シーン名は参照保護のため今回維持している。新規C#の型・ファイル名にはPascalCaseを使う。
- スクリプト／パッケージ変更後は実際のUnityコンパイルを行う。接続不能・Safe Mode・タイムアウトはコンパイル成功扱いにしない。必要に応じて関連するテストやシーン参照の検証を加える。
- `Library`、`Temp`、ログ、IDE生成物、認証情報、マシン固有のエージェント設定をコミットしない。成果報告では実行済みの検証と未確認事項を分ける。
