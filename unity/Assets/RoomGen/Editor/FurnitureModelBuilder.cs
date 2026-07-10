using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoomGen.Editor
{
    /// <summary>
    /// Turns the downloaded Poly Haven CC0 FBX models (Resources/RoomGen/Furniture/src/&lt;asset&gt;/)
    /// into placement-ready prefabs at Resources/RoomGen/Furniture/&lt;builtin-id&gt;.prefab — the exact
    /// path FurnitureLayoutResolver prefers over its greybox builtins, so no runtime code changes.
    ///
    /// Each prefab is a wrapper whose child model is: rotated by the manifest yaw so rotation 0 obeys
    /// the contract (chair backrest on -z / sitter faces +z), scaled (optionally to a target footprint,
    /// so the table still matches the slot layout chairs sit around), and floored (bounds min y = 0,
    /// centred in x/z). FBX importers get colliders so desktop-walk collision keeps working.
    /// </summary>
    public static class FurnitureModelBuilder
    {
        const string SrcRoot = "Assets/RoomGen/Resources/RoomGen/Furniture/src";
        const string PrefabRoot = "Assets/RoomGen/Resources/RoomGen/Furniture";

        struct ModelSpec
        {
            public string Asset;      // Poly Haven id (folder + file names)
            public string Target;     // prefab name = SurfaceResolver-style id
            public Vector3 RotationEuler; // axis/facing correction so rotation 0 obeys the contract
            public Vector3? FitSize;  // scale to this (x,y,z) bounds; null = keep real-world scale
            public float Smoothness;  // fallback smoothness when no mask is packed
        }

        static ModelSpec[] Manifest() => new[]
        {
            // Blender-authored FBX bakes Z-up into the vertices: stand the models up with -90 X.
            new ModelSpec { Asset = "dining_chair_02", Target = "builtin-dining-chair",
                RotationEuler = new Vector3(-90f, 0f, 0f), FitSize = null, Smoothness = 0.25f },
            // The slot layout seats chairs around a 2.15 x 1.05 table at h 0.76 — scale the model to it.
            new ModelSpec { Asset = "wooden_table_02", Target = "builtin-dining-table",
                RotationEuler = new Vector3(-90f, 0f, 0f), FitSize = new Vector3(2.15f, 0.78f, 1.05f), Smoothness = 0.3f },
            new ModelSpec { Asset = "GothicCabinet_01", Target = "builtin-sideboard",
                RotationEuler = new Vector3(-90f, 0f, 0f), FitSize = new Vector3(1.72f, 0.97f, 0.5f), Smoothness = 0.3f },
        };

        public static void BuildAll()
        {
            foreach (var spec in Manifest())
            {
                try { Build(spec); }
                catch (System.Exception e) { Debug.LogError($"FURNITUREBUILD {spec.Asset} FAILED: {e}"); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("FURNITUREBUILD done");
        }

        static void Build(ModelSpec spec)
        {
            var fbxPath = $"{SrcRoot}/{spec.Asset}/{spec.Asset}.fbx";
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) { Debug.LogError($"FURNITUREBUILD missing fbx {fbxPath}"); return; }

            var reimport = false;
            if (!importer.addCollider) { importer.addCollider = true; reimport = true; }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                reimport = true;
            }
            if (reimport) importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) { Debug.LogError($"FURNITUREBUILD load failed {fbxPath}"); return; }

            var material = BuildMaterial(spec);

            var wrapper = new GameObject(spec.Target);
            var child = (GameObject)Object.Instantiate(model);
            child.name = "Model";
            child.transform.SetParent(wrapper.transform, false);
            child.transform.localRotation = Quaternion.Euler(spec.RotationEuler);

            foreach (var renderer in child.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterial = material;

            // Measure in wrapper space via sharedMesh bounds + matrices (renderer.bounds can be stale
            // within a single batchmode tick). The child holds the rotation; the WRAPPER holds the fit
            // scale (scaling the rotated child per-axis would mix axes). Wrapper root scale survives
            // prefab instantiation, and the runtime yaw is applied on the parent item, outside it.
            var bounds = ComputeLocalBounds(wrapper, child);
            Debug.Log($"FURNITUREBUILD {spec.Target}: raw size=({bounds.size.x:0.000},{bounds.size.y:0.000},{bounds.size.z:0.000}) m");
            var wrapperScale = Vector3.one;
            if (spec.FitSize.HasValue && bounds.size.x > 0.0001f && bounds.size.y > 0.0001f && bounds.size.z > 0.0001f)
            {
                var fit = spec.FitSize.Value;
                wrapperScale = new Vector3(
                    fit.x / bounds.size.x, fit.y / bounds.size.y, fit.z / bounds.size.z);
                wrapper.transform.localScale = wrapperScale;
            }
            // Floor + centre in pre-scale wrapper units (the wrapper scale multiplies through).
            child.transform.localPosition = new Vector3(
                -bounds.center.x, -bounds.min.y, -bounds.center.z);

            var final = Vector3.Scale(bounds.size, wrapperScale);
            Debug.Log($"FURNITUREBUILD {spec.Target}: final size=({final.x:0.00},{final.y:0.00},{final.z:0.00}) m");

            Directory.CreateDirectory(PrefabRoot);
            var prefabPath = $"{PrefabRoot}/{spec.Target}.prefab";
            PrefabUtility.SaveAsPrefabAsset(wrapper, prefabPath);
            Object.DestroyImmediate(wrapper);
        }

        static Material BuildMaterial(ModelSpec spec)
        {
            var dir = $"{SrcRoot}/{spec.Asset}/textures";
            Texture2D Find(string tag)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileName(path).ToLowerInvariant().Contains(tag))
                        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
                return null;
            }

            var mat = new Material(Shader.Find("HDRP/Lit")) { name = spec.Target + "-mat" };
            var diff = Find("diff");
            if (diff != null) mat.SetTexture("_BaseColorMap", diff);

            // Pack the model's roughness map into an HDRP MaskMap (same channel layout as
            // AssetFetcher.PackMaskTexel) so surfaces get spatial roughness, not one flat value.
            var mask = BuildMaskFromRoughness(spec, dir);
            if (mask != null)
            {
                mat.SetTexture("_MaskMap", mask);
                mat.EnableKeyword("_MASKMAP");
            }
            else
            {
                mat.SetFloat("_Smoothness", spec.Smoothness);
            }

            var matPath = $"{PrefabRoot}/{spec.Target}-mat.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(matPath) != null) AssetDatabase.DeleteAsset(matPath);
            Directory.CreateDirectory(PrefabRoot);
            AssetDatabase.CreateAsset(mat, matPath);
            return AssetDatabase.LoadAssetAtPath<Material>(matPath);
        }

        // Packs <asset>_Mask.png (R=0 metal, G=255 AO, B=128, A=1-roughness) from the model's
        // roughness jpg; returns the imported texture, or null when no roughness map exists.
        static Texture2D BuildMaskFromRoughness(ModelSpec spec, string texDir)
        {
            string roughPath = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path).ToLowerInvariant().Contains("rough")) { roughPath = path; break; }
            }
            if (roughPath == null) return null;

            var abs = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, roughPath);
            var rough = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!rough.LoadImage(File.ReadAllBytes(abs))) { Object.DestroyImmediate(rough); return null; }

            var pixels = rough.GetPixels32();
            var mask = new Color32[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
                mask[i] = AssetFetcher.PackMaskTexel(pixels[i].r, 255, 0);
            var tex = new Texture2D(rough.width, rough.height, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(mask);
            tex.Apply();

            var maskAssetPath = $"{texDir}/{spec.Asset}_Mask.png";
            File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, maskAssetPath),
                tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rough);
            AssetDatabase.Refresh();

            if (AssetImporter.GetAtPath(maskAssetPath) is TextureImporter imp && imp.sRGBTexture)
            {
                imp.sRGBTexture = false;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(maskAssetPath);
        }

        // Bounds of all meshes under `child`, expressed in `space`'s local frame, computed from
        // sharedMesh.bounds corners transformed by the matrix chain — always fresh, unlike
        // Renderer.bounds which may not update within a single batchmode editor tick.
        static Bounds ComputeLocalBounds(GameObject space, GameObject child)
        {
            var toSpace = space.transform.worldToLocalMatrix;
            var has = false;
            var result = new Bounds();
            foreach (var mf in child.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var m = toSpace * mf.transform.localToWorldMatrix;
                var b = mf.sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    var p = m.MultiplyPoint3x4(corner);
                    if (!has) { result = new Bounds(p, Vector3.zero); has = true; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }
    }
}
