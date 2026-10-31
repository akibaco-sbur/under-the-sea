using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "FishColorSet", menuName = "Fish/Material Color Set")]
public sealed class FishMaterialColorSet : ScriptableObject
{
    [SerializeField, InspectorName("差し替え先のマテリアル")]
    Material[] materials = new Material[0];

    public int Count => materials != null ? materials.Length : 0;
    public Material GetMaterial(int index) => index >= 0 && index < Count ? materials[index] : null;

    public static bool TryReadColor(Material material, out Color color)
    {
        color = Color.white;
        if (!material) return false;
        if (material.HasProperty("_BaseColor")) color = material.GetColor("_BaseColor");
        else if (material.HasProperty("_Color")) color = material.GetColor("_Color");
        else return false;
        return true;
    }

    public bool TryFindClosest(Color foodColor, out int index)
    {
        index = -1;
        float best = float.PositiveInfinity;
        for (int i = 0; i < Count; i++)
        {
            if (!TryReadColor(materials[i], out Color color)) continue;
            float r = foodColor.r - color.r, g = foodColor.g - color.g, b = foodColor.b - color.b;
            float distance = r * r + g * g + b * b;
            if (distance >= best) continue;
            best = distance;
            index = i;
        }
        return index >= 0;
    }
}

[Serializable]
public sealed class FishMaterialColorBinding
{
    [InspectorName("変更する元マテリアル")]
    public Material OriginalMaterial;
    [InspectorName("差し替えに使う色セット")]
    public FishMaterialColorSet ColorSet;
}

/// <summary>Build a complete replacement plan before editing any renderer.</summary>
public static class FishMaterialColorReplacement
{
    public static bool TryBuild(Material[] originals, FishMaterialColorBinding[] bindings, Color foodColor,
        out Material[] replacements, out string error)
    {
        replacements = null;
        error = null;
        if (originals == null || bindings == null || bindings.Length == 0)
        {
            error = "差し替え設定がありません。";
            return false;
        }
        var plan = (Material[])originals.Clone();
        var used = new HashSet<Material>();
        foreach (var binding in bindings)
        {
            if (binding == null || !binding.OriginalMaterial || !binding.ColorSet)
            {
                error = "元マテリアルと色セットを両方指定してください。";
                return false;
            }
            if (!used.Add(binding.OriginalMaterial))
            {
                error = "同じ元マテリアルが重複しています: " + binding.OriginalMaterial.name;
                return false;
            }
            if (!binding.ColorSet.TryFindClosest(foodColor, out int colorIndex))
            {
                error = "色セットに色を取得できるマテリアルがありません: " + binding.ColorSet.name;
                return false;
            }
            bool found = false;
            for (int slot = 0; slot < originals.Length; slot++)
                if (originals[slot] == binding.OriginalMaterial)
                {
                    plan[slot] = binding.ColorSet.GetMaterial(colorIndex);
                    found = true;
                }
            if (!found)
            {
                error = "魚に使用されていない元マテリアルです: " + binding.OriginalMaterial.name;
                return false;
            }
        }
        replacements = plan;
        return true;
    }
}
