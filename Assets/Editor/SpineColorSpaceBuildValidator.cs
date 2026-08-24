using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Harma.EditorTools
{
    /// <summary>
    /// Scans only enabled build-scene dependencies for Spine materials that still
    /// expect premultiplied-alpha textures while the project uses Linear color space.
    /// </summary>
    public static class SpineColorSpaceValidation
    {
        public const string ChecklistPath = "Docs/SpineStraightAlphaReexportChecklist.md";

        [MenuItem("Tools/Harma/Validate Spine Alpha Compatibility")]
        public static void ValidateFromMenu()
        {
            SpineColorSpaceValidationResult result = ScanEnabledBuildScenes();
            string message = result.FormatMessage();

            if (result.IncompatibleMaterialPaths.Length > 0)
                Debug.LogWarning(message);
            else
                Debug.Log(message);

            EditorUtility.DisplayDialog(
                "Spine Alpha Compatibility",
                message,
                "OK");
        }

        public static SpineColorSpaceValidationResult ScanEnabledBuildScenes()
        {
            string[] scenePaths = EditorBuildSettings.scenes
                .Where(scene => scene.enabled && !string.IsNullOrEmpty(scene.path))
                .Select(scene => scene.path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            string[] materialPaths = AssetDatabase.GetDependencies(scenePaths, true)
                .Where(path => path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var checkedSpineMaterials = new List<string>();
            var incompatibleMaterials = new List<string>();
            bool isLinear = PlayerSettings.colorSpace == ColorSpace.Linear;

            foreach (string materialPath in materialPaths)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (!IsSpineMaterial(material))
                    continue;

                checkedSpineMaterials.Add(materialPath);
                if (isLinear && UsesPremultipliedAlphaTexture(material))
                    incompatibleMaterials.Add(materialPath);
            }

            return new SpineColorSpaceValidationResult(
                isLinear,
                scenePaths,
                checkedSpineMaterials.ToArray(),
                incompatibleMaterials.ToArray());
        }

        public static bool IsSpineMaterial(Material material)
        {
            return material != null &&
                   material.shader != null &&
                   material.shader.name.IndexOf("Spine/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool UsesPremultipliedAlphaTexture(Material material)
        {
            return IsSpineMaterial(material) &&
                   material.HasProperty("_StraightAlphaInput") &&
                   material.GetFloat("_StraightAlphaInput") < 0.5f;
        }
    }

    public sealed class SpineColorSpaceValidationResult
    {
        public SpineColorSpaceValidationResult(
            bool isLinear,
            string[] enabledScenePaths,
            string[] checkedMaterialPaths,
            string[] incompatibleMaterialPaths)
        {
            IsLinear = isLinear;
            EnabledScenePaths = enabledScenePaths ?? Array.Empty<string>();
            CheckedMaterialPaths = checkedMaterialPaths ?? Array.Empty<string>();
            IncompatibleMaterialPaths = incompatibleMaterialPaths ?? Array.Empty<string>();
        }

        public bool IsLinear { get; }
        public string[] EnabledScenePaths { get; }
        public string[] CheckedMaterialPaths { get; }
        public string[] IncompatibleMaterialPaths { get; }

        public string FormatMessage()
        {
            if (!IsLinear)
                return "Project color space is not Linear; no Linear/PMA compatibility check is required.";

            if (IncompatibleMaterialPaths.Length == 0)
            {
                return $"Spine alpha validation passed. Checked {CheckedMaterialPaths.Length} " +
                       "Spine material(s) referenced by enabled build scenes.";
            }

            var builder = new StringBuilder();
            builder.AppendLine(
                $"Linear color space has {IncompatibleMaterialPaths.Length} build-dependent " +
                "Spine material(s) using PMA textures:");
            foreach (string path in IncompatibleMaterialPaths)
                builder.AppendLine($"- {path}");
            builder.AppendLine();
            builder.Append($"Re-export the source atlases as Straight Alpha. Checklist: {SpineColorSpaceValidation.ChecklistPath}");
            return builder.ToString();
        }
    }

    public sealed class SpineColorSpaceBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            SpineColorSpaceValidationResult result =
                SpineColorSpaceValidation.ScanEnabledBuildScenes();
            if (result.IncompatibleMaterialPaths.Length > 0)
                Debug.LogWarning(result.FormatMessage());
        }
    }
}
