using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(StripedFishAnimatedQuadBillboard)), CanEditMultipleObjects]
public sealed class StripedFishMaterialEditor : Editor
{
    bool showLegacy;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "materialVariants", "feedingVariantByFoodColor", "previewVariantIndex");
        EditorGUILayout.HelpBox("元マテリアルを指定し、差し替え先をまとめた色セットを割り当てます。食べた餌に近い色がセットから選ばれます。Nameやスロット番号の入力は不要です。", MessageType.Info);
        var bindings = serializedObject.FindProperty("materialColorBindings");
        if (bindings.arraySize > 0 && !serializedObject.isEditingMultipleObjects)
        {
            var palette = bindings.GetArrayElementAtIndex(0).FindPropertyRelative("ColorSet").objectReferenceValue as FishMaterialColorSet;
            if (palette)
            {
                EditorGUILayout.LabelField("色変更のプレビュー（Play中のみ）");
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                    for (int i = 0; i < palette.Count; i++)
                    {
                        Material material = palette.GetMaterial(i);
                        if (material && GUILayout.Button(material.name))
                        {
                            serializedObject.ApplyModifiedProperties();
                            ((StripedFishAnimatedQuadBillboard)target).ApplyMaterialVariant(i);
                        }
                    }
            }
        }
        showLegacy = EditorGUILayout.Foldout(showLegacy, "以前の設定（互換用）", true);
        if (showLegacy)
        {
            EditorGUILayout.HelpBox("差し替え設定が空の場合のみ使用します。旧Nameは候補の表示名で、色の判定には使いません。", MessageType.None);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("materialVariants"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("feedingVariantByFoodColor"), true);
        }
        serializedObject.ApplyModifiedProperties();
    }
}
