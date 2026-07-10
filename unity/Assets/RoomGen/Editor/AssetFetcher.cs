using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace RoomGen.Editor
{
    /// <summary>
    /// L1 asset pipeline. Downloads (or reuses cached) ambientCG CC0 texture zips pinned in
    /// unity/ASSETS.md, verifies each against its SHA-256, extracts the maps, packs an HDRP
    /// MaskMap, and builds a triplanar world-mapped HDRP/Lit material at the RoomSpec's
    /// material resource path — overwriting the flat bootstrap builtin.
    ///
    /// Batchmode: -executeMethod RoomGen.Editor.AssetFetcher.FetchAll
    /// Missing network or a SHA mismatch is a soft failure: the flat builtin is left in place,
    /// so a clone without fetched textures still renders (greybox fidelity ladder rung 1).
    /// </summary>
    public static class AssetFetcher
    {
        const string CacheDir = "Assets/RoomGen/.cache";
        const string MatRoot = "Assets/RoomGen/Resources/RoomGen/Materials";
        const string TexRoot = "Assets/RoomGen/Resources/RoomGen/Materials/Textures";

        struct Pin
        {
            public string Id;         // ambientCG asset id
            public string Target;     // material resource name (SurfaceResolver id, '.'->'-')
            public float SizeM;       // real-world texture size in meters (world-scale tiling)
            public string Sha256;     // lock for the 2K-JPG zip
            public Color Tint;        // multiplies base color (white = untinted)
        }

        static Pin[] Manifest()
        {
            return new[]
            {
                new Pin { Id = "WoodFloor043", Target = "builtin-oak", SizeM = 2f, Tint = Color.white,
                    Sha256 = "5f9cab5c67eeeb441a6aa6744f9ebbd56186dfeab4f9c1d924220d3d43b73bdb" },
                new Pin { Id = "Plaster001", Target = "builtin-warm-white", SizeM = 2f,
                    Tint = new Color(0.96f, 0.94f, 0.90f),
                    Sha256 = "9894765632cfa0feb3349b581825a9c224dee9efa1cebb8aba63bd1c7bc9fc3c" },
                new Pin { Id = "Plaster001", Target = "builtin-ceiling-white", SizeM = 2f,
                    Tint = new Color(0.98f, 0.98f, 0.97f),
                    Sha256 = "9894765632cfa0feb3349b581825a9c224dee9efa1cebb8aba63bd1c7bc9fc3c" },
                new Pin { Id = "Carpet012", Target = "cc0-carpet", SizeM = 2f, Tint = Color.white,
                    Sha256 = "476528d0deb91b38608e7d2ef51991ba89b5c976e0ae726d655588728f5852c8" },
                new Pin { Id = "Tiles107", Target = "cc0-tile", SizeM = 1f, Tint = Color.white,
                    Sha256 = "fa76887bb33af0972154700126d1f86d01eaed518341ce7411616c1d1202ac55" },
                // Furniture materials (smaller physical sizes so grain/weave reads at furniture scale;
                // tints push each ambientCG source toward its builtin identity).
                new Pin { Id = "Wood066", Target = "builtin-walnut", SizeM = 0.8f,
                    Tint = new Color(0.55f, 0.40f, 0.28f),
                    Sha256 = "fa88e3f0cb03e940e6618056c4b4b10577488f01383a7d39ca85d62d13122800" },
                new Pin { Id = "Fabric045", Target = "builtin-fabric-charcoal", SizeM = 0.6f,
                    Tint = new Color(0.30f, 0.31f, 0.34f),
                    Sha256 = "e6ca2d15806601a0f9199981dd3c3efe80519ecd9d806910b2b0ab3a8320e770" },
                new Pin { Id = "Metal032", Target = "builtin-metal-black", SizeM = 0.4f,
                    Tint = new Color(0.22f, 0.22f, 0.24f),
                    Sha256 = "4b8884843c490963d5734be036c35639d064e7a817eb61145322f69c6c19895a" },
            };
        }

        [MenuItem("RoomGen/Fetch CC0 Materials")]
        public static void FetchAll()
        {
            Directory.CreateDirectory(AbsPath(CacheDir));
            EnsureFolders();

            int ok = 0, soft = 0;
            foreach (var pin in Manifest())
            {
                try
                {
                    if (BuildMaterial(pin)) ok++; else soft++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"AssetFetcher: {pin.Id} -> {pin.Target} failed: {e}");
                    soft++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"ASSETFETCHER done built={ok} soft-skipped={soft}");
        }

        static bool BuildMaterial(Pin pin)
        {
            var zip = Path.Combine(AbsPath(CacheDir), pin.Id + "_2K-JPG.zip");
            if (!File.Exists(zip) &&
                !Download($"https://ambientcg.com/get?file={pin.Id}_2K-JPG.zip", zip))
            {
                Debug.LogWarning($"AssetFetcher: no cached zip and download failed for {pin.Id}; keeping flat builtin.");
                return false;
            }

            var sha = Sha256File(zip);
            if (!string.Equals(sha, pin.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"AssetFetcher: SHA mismatch for {pin.Id}; expected {pin.Sha256} got {sha}. Skipping (determinism guard).");
                return false;
            }

            var texDir = TexRoot + "/" + pin.Id;
            var absTexDir = AbsPath(texDir);
            Directory.CreateDirectory(absTexDir);

            string color = null, normal = null, rough = null, ao = null, metal = null;
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var entry in archive.Entries)
                {
                    var nm = entry.Name;
                    if (!nm.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) continue;
                    string dest = null;
                    if (nm.EndsWith("_Color.jpg", StringComparison.OrdinalIgnoreCase)) dest = color = pin.Id + "_Color.jpg";
                    else if (nm.EndsWith("_NormalGL.jpg", StringComparison.OrdinalIgnoreCase)) dest = normal = pin.Id + "_Normal.jpg";
                    else if (nm.EndsWith("_Roughness.jpg", StringComparison.OrdinalIgnoreCase)) dest = rough = pin.Id + "_Roughness.jpg";
                    else if (nm.EndsWith("_AmbientOcclusion.jpg", StringComparison.OrdinalIgnoreCase)) dest = ao = pin.Id + "_AO.jpg";
                    else if (nm.EndsWith("_Metalness.jpg", StringComparison.OrdinalIgnoreCase)) dest = metal = pin.Id + "_Metal.jpg";
                    if (dest != null) entry.ExtractToFile(Path.Combine(absTexDir, dest), true);
                }
            }
            if (color == null)
            {
                Debug.LogWarning($"AssetFetcher: no Color map in {pin.Id}; skipping.");
                return false;
            }
            AssetDatabase.Refresh();

            SetImporter(texDir + "/" + color, srgb: true, normal: false);
            if (normal != null) SetImporter(texDir + "/" + normal, srgb: false, normal: true);

            var maskRel = rough != null ? BuildMaskMap(pin, absTexDir, texDir, rough, ao, metal) : null;

            var mat = new Material(Shader.Find("HDRP/Lit")) { name = pin.Target };
            var colorTex = Load<Texture2D>(texDir + "/" + color);
            if (colorTex != null) mat.SetTexture("_BaseColorMap", colorTex);
            mat.SetColor("_BaseColor", pin.Tint);

            if (normal != null)
            {
                var nrm = Load<Texture2D>(texDir + "/" + normal);
                if (nrm != null)
                {
                    mat.SetTexture("_NormalMap", nrm);
                    mat.SetFloat("_NormalScale", 1f);
                    mat.EnableKeyword("_NORMALMAP");
                }
            }

            if (maskRel != null)
            {
                var mk = Load<Texture2D>(maskRel);
                if (mk != null)
                {
                    mat.SetTexture("_MaskMap", mk);
                    mat.EnableKeyword("_MASKMAP");
                }
            }
            else
            {
                mat.SetFloat("_Smoothness", 0.2f);
            }

            // Triplanar world-space mapping: tiles cube walls and prism floor/ceiling uniformly
            // without depending on mesh UVs. TexWorldScale is tiles-per-meter -> 1 / physical size.
            mat.SetFloat("_UVBase", 5f);              // 5 = Triplanar
            mat.SetFloat("_ObjectSpaceUVMapping", 0f); // world space
            mat.SetFloat("_TexWorldScale", 1f / pin.SizeM);
            mat.SetColor("_UVMappingMask", new Color(1f, 0f, 0f, 0f));
            mat.EnableKeyword("_MAPPING_TRIPLANAR");

            CreateOrReplaceMaterial(mat, MatRoot + "/" + pin.Target + ".mat");
            Debug.Log($"ASSETFETCHER built {pin.Target} from {pin.Id} normal={(normal != null)} mask={(maskRel != null)}");
            return true;
        }

        // HDRP MaskMap channel packing: R=Metalness, G=AO, B=Detail(neutral 128), A=Smoothness=1-Roughness.
        public static Color32 PackMaskTexel(byte roughness, byte ambientOcclusion, byte metalness)
            => new Color32(metalness, ambientOcclusion, 128, (byte)(255 - roughness));

        // Packs the HDRP MaskMap from the ambientCG Roughness (+ optional AO/Metalness) maps.
        static string BuildMaskMap(Pin pin, string absTexDir, string texDir, string roughRel, string aoRel, string metalRel)
        {
            var rough = LoadReadable(Path.Combine(absTexDir, Path.GetFileName(roughRel)));
            if (rough == null) return null;

            int w = rough.width, h = rough.height;
            var rp = rough.GetPixels32();
            Color32[] ap = null, mp = null;

            if (aoRel != null)
            {
                var t = LoadReadable(Path.Combine(absTexDir, Path.GetFileName(aoRel)));
                if (t != null && t.width == w && t.height == h) ap = t.GetPixels32();
                if (t != null) UnityEngine.Object.DestroyImmediate(t);
            }
            if (metalRel != null)
            {
                var t = LoadReadable(Path.Combine(absTexDir, Path.GetFileName(metalRel)));
                if (t != null && t.width == w && t.height == h) mp = t.GetPixels32();
                if (t != null) UnityEngine.Object.DestroyImmediate(t);
            }

            var mask = new Color32[w * h];
            for (var i = 0; i < mask.Length; i++)
            {
                byte metal = mp != null ? mp[i].r : (byte)0;
                byte occ = ap != null ? ap[i].r : (byte)255;
                mask[i] = PackMaskTexel(rp[i].r, occ, metal);
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(mask);
            tex.Apply();
            var maskFile = pin.Id + "_Mask.png";
            File.WriteAllBytes(Path.Combine(absTexDir, maskFile), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(rough);

            AssetDatabase.Refresh();
            var maskRel = texDir + "/" + maskFile;
            SetImporter(maskRel, srgb: false, normal: false);
            return maskRel;
        }

        static bool Download(string url, string dest)
        {
            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = true };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(180) };
                var bytes = client.GetByteArrayAsync(url).GetAwaiter().GetResult();
                if (bytes == null || bytes.Length < 10000) return false;
                File.WriteAllBytes(dest, bytes);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"AssetFetcher: download {url} failed: {e.Message}");
                return false;
            }
        }

        static Texture2D LoadReadable(string absPath)
        {
            if (!File.Exists(absPath)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!tex.LoadImage(File.ReadAllBytes(absPath)))
            {
                UnityEngine.Object.DestroyImmediate(tex);
                return null;
            }
            return tex;
        }

        static void SetImporter(string assetPath, bool srgb, bool normal)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter imp) return;
            var changed = false;
            if (normal)
            {
                if (imp.textureType != TextureImporterType.NormalMap)
                {
                    imp.textureType = TextureImporterType.NormalMap;
                    changed = true;
                }
            }
            else if (imp.sRGBTexture != srgb)
            {
                imp.sRGBTexture = srgb;
                changed = true;
            }
            if (changed) imp.SaveAndReimport();
        }

        static void CreateOrReplaceMaterial(Material mat, string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mat, path);
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(MatRoot))
                Directory.CreateDirectory(AbsPath(MatRoot));
            AssetDatabase.Refresh();
            if (!AssetDatabase.IsValidFolder(TexRoot))
                AssetDatabase.CreateFolder(MatRoot, "Textures");
        }

        static T Load<T>(string assetPath) where T : UnityEngine.Object
            => AssetDatabase.LoadAssetAtPath<T>(assetPath);

        static string AbsPath(string assetRelative)
            => Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, assetRelative);

        static string Sha256File(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
