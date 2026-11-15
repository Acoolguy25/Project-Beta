using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

/// <summary>
/// One-shot upgrader for the Greylands melee weapon materials.
/// </summary>
public static class GreylandsWeaponMaterialUpgrader
{
    private const string TargetFolder = "Assets/The Greylands Game Assets/One-Handed Melee Weapons Lite/Materials";

    [MenuItem("Tools/Upgrade Greylands Weapon Materials")]
    public static void Run()
    {
        //var guids = AssetDatabase.FindAssets("t:Material", new[] { TargetFolder });
        //var upgradedCount = 0;
        //var skippedCount = 0;

        //AssetDatabase.StartAssetEditing();
        //try
        //{
        //    foreach (var guid in guids)
        //    {
        //        var path = AssetDatabase.GUIDToAssetPath(guid);
        //        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        //        if (material == null)
        //        {
        //            skippedCount++;
        //            continue;
        //        }

        //        var shaderName = material.shader != null ? material.shader.name : string.Empty;
        //        if (shaderName != "Standard" && shaderName != "Standard (Specular setup)")
        //        {
        //            skippedCount++;
        //            continue;
        //        }

        //        MaterialUpgrader.Upgrade(material, new StandardUpgrader(shaderName), MaterialUpgrader.UpgradeFlags.None);
        //        EditorUtility.SetDirty(material);
        //        upgradedCount++;
        //    }
        //}
        //finally
        //{
        //    AssetDatabase.StopAssetEditing();
        //}

        //AssetDatabase.SaveAssets();
        //AssetDatabase.Refresh();
        //Debug.Log($"Greylands weapon material upgrade complete. Upgraded: {upgradedCount}, skipped: {skippedCount}.");
    }
}
