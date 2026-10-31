using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class StripedFishPaletteBuilder
{
    const string Output = "Assets/Materials/StripedFishColors";
    static readonly string[] Names = { "Orange", "Cyan", "Green", "Purple", "Red", "Yellow", "Blue", "Pink", "Mint", "Ivory" };
    static readonly string[] Hex = { "FFAD66", "77DFFF", "95E875", "BC9AFF", "FF7777", "FFE578", "80A9FF", "FFA0CE", "79E1BD", "FFF1D1" };

    [MenuItem("Tools/Fish/Create Striped Fish Color Materials")]
    public static void Create()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/StripedFish.fbx");
        if (!model) throw new InvalidOperationException("StripedFish model not found.");
        var renderer = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(r => r.sharedMesh).OrderByDescending(r => r.sharedMesh.vertexCount).FirstOrDefault();
        if (!renderer) throw new InvalidOperationException("StripedFish renderer not found.");
        EnsureFolder(Output);
        var report = new StringBuilder("# StripedFish color materials\n\n");
        report.AppendLine("Original textures, shader and transparency are preserved. Only base tint changes.\n");
        int count = 0;
        for (int slot = 0; slot < renderer.sharedMaterials.Length; slot++)
        {
            var source = renderer.sharedMaterials[slot];
            if (!source) continue;
            string property = source.HasProperty("_BaseColor") ? "_BaseColor" : source.HasProperty("_Color") ? "_Color" : null;
            if (property == null) throw new InvalidOperationException("No tint property: " + source.name);
            string folder = Output + "/Slot" + slot;
            EnsureFolder(folder);
            report.AppendLine("## Slot " + slot + ": " + source.name + " (" + source.shader.name + ")\n");
            for (int i = 0; i < Names.Length; i++)
            {
                string path = folder + "/StripedFish" + Names[i] + ".mat";
                if (AssetDatabase.LoadMainAssetAtPath(path))
                    throw new InvalidOperationException("Refusing to overwrite existing asset: " + path);
            }
            for (int i = 0; i < Names.Length; i++)
            {
                ColorUtility.TryParseHtmlString("#" + Hex[i], out Color tint);
                Color original = source.GetColor(property);
                // Multiplication retains authored dark stripes and the source alpha.
                Color color = new Color(original.r * tint.r, original.g * tint.g, original.b * tint.b, original.a);
                var material = new Material(source) { name = "StripedFish" + Names[i] };
                material.SetColor(property, color);
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                string path = folder + "/" + material.name + ".mat";
                AssetDatabase.CreateAsset(material, path);
                report.AppendLine("- " + i + ": " + Names[i] + " (#" + Hex[i] + ") — " + path);
                count++;
            }
            report.AppendLine();
        }
        AssetDatabase.SaveAssets();
        // Reload and verify persisted material properties against the source.
        for (int slot = 0; slot < renderer.sharedMaterials.Length; slot++)
        {
            var source = renderer.sharedMaterials[slot];
            if (!source) continue;
            foreach (string name in Names)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(Output + "/Slot" + slot + "/StripedFish" + name + ".mat");
                if (!material || material.shader != source.shader || material.renderQueue != source.renderQueue)
                    throw new InvalidOperationException("Material validation failed: " + name);
                foreach (string property in source.GetTexturePropertyNames())
                    if (material.GetTexture(property) != source.GetTexture(property) ||
                        material.GetTextureScale(property) != source.GetTextureScale(property) ||
                        material.GetTextureOffset(property) != source.GetTextureOffset(property))
                        throw new InvalidOperationException("Texture validation failed: " + name + "/" + property);
            }
        }
        File.WriteAllText("Docs/StripedFishColorMaterials.md", report.ToString());
        Debug.Log("[StripedFishPalette] Created and verified " + count + " materials across " + renderer.sharedMaterials.Length + " slots.\n" + report);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }
}
