# StripedFishのマテリアル切り替え

魚の `StripedFishAnimatedQuadBillboard` コンポーネントで設定します。

## Inspectorの設定

1. コンポーネントの右上メニューから `Material Variants > Print Source Material Slots` を選び、Consoleで元マテリアルの枠番号と名前を確認します。番号は0始まりです。元のSkinnedMeshRendererのMaterialsのElement番号と対応します。
2. `Material Variants` の要素数を、用意する見た目の数にします。魚ごとに数を変えられます。
3. 各要素に `Name` と `Replacements` を設定します。
4. `Replacements` の各要素に、変更する `Slot Index` と差し替え先の `Material` を指定します。変更したい枠だけ登録します。

例：3枠の魚で、候補0は枠0のみ、候補1は枠0と枠2を変更できます。

| 候補 | Name | Replacements |
| --- | --- | --- |
| 0 | Red | Slot Index 0 → 赤いマテリアル |
| 1 | Blue | Slot Index 0 → 青いマテリアル、Slot Index 2 → 別のヒレ用マテリアル |

候補を選ぶたびに元マテリアルを基準に構成します。候補1から候補0へ戻すと、候補0に指定していない枠2は元に戻ります。要素数0のReplacementsは元の見た目になります。

マテリアル未指定・存在しない枠番号・同じ枠の重複指定があれば、その候補全体の変更を中止しConsoleに理由を表示します。

## Play中の確認

1. Play前に差し替え候補を設定します。
2. Play中に `Preview Variant Index` を選びます。
3. コンポーネント右上メニューの `Material Variants > Apply Preview Variant (Play Mode)` を実行します。
4. 元に戻すには `Material Variants > Restore Original (Play Mode)` を実行します。

Play中に変更したInspector設定は終了時に戻ります。差し替え先の登録はPlay前に行ってください。

## スクリプト・イベントから呼ぶ

```csharp
// 登録した候補を選ぶ。UnityEventのint引数でも指定可能。
stripedFish.ApplyMaterialVariant(1);

// 成功したかを呼び出し元で確認する場合。
bool changed = stripedFish.TryApplyMaterialVariant(1);

// 現在の見た目の指定枠だけを差し替える。
stripedFish.SetMaterialAtSlot(0, replacementMaterial);

stripedFish.RestoreOriginalMaterials();
```

変更は魚1匹のRendererに反映します。元のSkinnedMeshRendererと生成された表示用メッシュの両方を更新し、表示用の両面マテリアルも再作成します。マテリアルアセット自体の色やテクスチャは編集しません。ヒレ補正対象の分類には元マテリアル名を使います。

## Mainシーンの「食べた魚が色を変える」動作

`SummonFish` のStripedFish生成元にある `Material Variants` をPlay前に設定します。生成された各個体へ設定が引き継がれます。生成元が持っていないコンポーネントを `Behaviours To Add` から追加する場合も、StripedFishのマテリアル候補設定をコピーします。

餌を掬い、前に差し出して約5秒保つと、対応するマテリアル候補を持つ個体の中で近い1匹が接近します。餌を引っ込める、追跡が途切れる、Backspaceでリセットする場合は接近を中止します。到着して約0.3秒食べると、その魚だけがマテリアルを変更し、餌の拡散表示が始まります。食べ終わった魚は通常の遊泳へ戻ります。

餌色は `DiverFeedingFoodController` の `Food Palette` の番号です。既定では0=オレンジ、1=水色、2=緑、3=紫です。

`Feeding Variant By Food Color` に「餌色番号に対して使うマテリアル候補番号」を登録できます。例えば候補がRed=0、Blue=1、Green=2、Purple=3なら、配列の値を `0, 1, 2, 3` にします。候補の並びが違う魚はその魚用の番号へ変更してください。

配列を空にした場合、`餌色番号 % 候補数` を使います。候補数が2なら餌色0/2は候補0、餌色1/3は候補1です。配列の値を `-1` にした餌色には反応しません。

接近対象となるには、対応する候補のReplacementsに1つ以上の有効な差し替えが必要です。候補が未登録・差し替え未指定・枠番号不正ならその個体は対象外になります。該当する魚がいない場合、餌は消えずConsoleに設定確認の警告が出ます。

口元と餌の接触位置を調整する場合は `SummonFish > Fish Types > 対象種 > Feeding Mouth Local Offset` を使います。Rootボーンから口までの位置差をPrefabのローカル座標で指定します。ゼロの場合はRoot位置で到着を判定します。到着距離と食べる時間は `Feeding Bite Distance`（ワールド単位）と `Feeding Bite Seconds` です。

Consoleに `[FishFeeding] ... is approaching ...`、続いて `... ate the food and changed material.` が出れば、接近対象の選択と食事完了をそれぞれ確認できます。キーボード試験でも同じ流れになります。
