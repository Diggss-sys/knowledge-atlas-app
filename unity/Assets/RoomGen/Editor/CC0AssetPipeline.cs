using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace RoomGen.Editor
{
    public static class CC0AssetPipeline
    {
        private const string TextureDir = "Assets/RoomGen/Resources/RoomGen/Materials/Textures";
        private const string MaterialDir = "Assets/RoomGen/Resources/RoomGen/Materials";

        [MenuItem("RoomGen/Fetch Sample CC0 Wood Floor")]
        public static async void FetchSampleWoodFloor()
        {
            string url = "https://dl.polyhaven.org/file/ph-assets/Textures/zip/1k/brown_planks_03_1k.zip";
            string assetName = "brown_planks_03";
            string matId = "cc0-brown-planks-03";
            
            await DownloadAndCreateMaterial(url, assetName, matId);
        }

        private static async Task DownloadAndCreateMaterial(string zipUrl, string assetName, string matId)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Fetching CC0 Asset", "Downloading textures...", 0.2f);
                
                string tempZipPath = Path.Combine(Application.temporaryCachePath, $"{assetName}.zip");
                
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync(zipUrl);
                    response.EnsureSuccessStatusCode();
                    File.WriteAllBytes(tempZipPath, await response.Content.ReadAsByteArrayAsync());
                }

                EditorUtility.DisplayProgressBar("Fetching CC0 Asset", "Extracting textures...", 0.5f);
                
                string extractPath = Path.Combine(TextureDir, assetName);
                if (!AssetDatabase.IsValidFolder(extractPath))
                {
                    if (!AssetDatabase.IsValidFolder(TextureDir))
                    {
                        AssetDatabase.CreateFolder(MaterialDir, "Textures");
                    }
                    AssetDatabase.CreateFolder(TextureDir, assetName);
                }

                // Delete existing files in that dir to avoid extraction errors
                var fullExtractPath = Path.Combine(Application.dataPath, extractPath.Substring(7));
                if (Directory.Exists(fullExtractPath))
                {
                    Directory.Delete(fullExtractPath, true);
                }
                Directory.CreateDirectory(fullExtractPath);

                ZipFile.ExtractToDirectory(tempZipPath, fullExtractPath);
                File.Delete(tempZipPath);

                EditorUtility.DisplayProgressBar("Fetching CC0 Asset", "Importing assets...", 0.7f);
                AssetDatabase.Refresh();

                CreateHDRPMaterial(assetName, matId, extractPath);

                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Success", $"Material {matId} created successfully!", "OK");
            }
            catch (System.Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"Failed to fetch CC0 asset: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to fetch CC0 asset.\n{e.Message}", "OK");
            }
        }

        private static void CreateHDRPMaterial(string assetName, string matId, string textureFolder)
        {
            string matPath = $"{MaterialDir}/{matId}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            
            if (material == null)
            {
                var shader = Shader.Find("HDRP/Lit");
                if (shader == null)
                {
                    Debug.LogError("HDRP/Lit shader not found.");
                    return;
                }
                material = new Material(shader) { name = matId };
                AssetDatabase.CreateAsset(material, matPath);
            }

            // Find textures
            var diffPath = $"{textureFolder}/{assetName}_diff_1k.jpg";
            var norPath = $"{textureFolder}/{assetName}_nor_gl_1k.jpg"; // OpenGL normal
            var armPath = $"{textureFolder}/{assetName}_arm_1k.jpg"; // AO, Roughness, Metallic

            var diffTex = AssetDatabase.LoadAssetAtPath<Texture2D>(diffPath);
            var norTex = AssetDatabase.LoadAssetAtPath<Texture2D>(norPath);
            var armTex = AssetDatabase.LoadAssetAtPath<Texture2D>(armPath);

            // Fix normal map import settings
            if (norTex != null)
            {
                var importer = AssetImporter.GetAtPath(norPath) as TextureImporter;
                if (importer != null && importer.textureType != TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    importer.SaveAndReimport();
                }
            }

            // Fix mask map import settings (sRGB must be false)
            if (armTex != null)
            {
                var importer = AssetImporter.GetAtPath(armPath) as TextureImporter;
                if (importer != null && importer.sRGBTexture)
                {
                    importer.sRGBTexture = false;
                    importer.SaveAndReimport();
                }
            }

            // Apply to material
            if (diffTex != null) material.SetTexture("_BaseColorMap", diffTex);
            if (norTex != null) material.SetTexture("_NormalMap", norTex);
            
            if (armTex != null)
            {
                material.SetTexture("_MaskMap", armTex);
                material.EnableKeyword("_MASKMAP");
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }
    }
}
