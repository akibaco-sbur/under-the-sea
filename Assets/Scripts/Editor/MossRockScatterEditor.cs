using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MossRockScatter))]
public sealed class MossRockScatterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox(
            "配置範囲はこのオブジェクトを中心としたローカルXZ平面です。SeaBedにColliderがあれば表面に合わせて配置し、設定した割合までランダムに埋めます（上限50%）。生成物だけを再生成・削除します。",
            MessageType.Info);

        MossRockScatter scatter = (MossRockScatter)target;
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Generate Rocks"))
        {
            Undo.RecordObject(scatter, "Generate Moss Rocks");
            scatter.Generate();
            EditorUtility.SetDirty(scatter);
        }
        if (GUILayout.Button("Clear Generated"))
            scatter.ClearGenerated();
        EditorGUILayout.EndHorizontal();
    }
}
