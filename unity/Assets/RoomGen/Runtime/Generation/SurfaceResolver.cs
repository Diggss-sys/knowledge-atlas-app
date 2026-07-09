using System.Collections.Generic;
using UnityEngine;

namespace RoomGen.Generation
{
    public sealed class SurfaceResolver
    {
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        public Material Resolve(string stableId)
        {
            if (Cache.TryGetValue(stableId, out var cached)) return cached;

            var resourceName = stableId.Replace('.', '-');
            var material = Resources.Load<Material>("RoomGen/Materials/" + resourceName);
            if (material == null)
                material = CreateFallback(stableId);
            Cache[stableId] = material;
            return material;
        }

        Material CreateFallback(string stableId)
        {
            var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = stableId + " (generated)" };
            var color = ColorFor(stableId);
            var pbr = PbrFor(stableId);

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            // Per-surface-class PBR so untextured fallbacks read as real materials, not flat
            // shaded plastic: wood is semi-rough dielectric, paint matte, metal reflective, glass
            // near-mirror. Values feed HDRP/Lit (and Standard, which shares the property names).
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", pbr.smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", pbr.metallic);
            return material;
        }

        // (smoothness 0..1, metallic 0..1) grouped by material family.
        static (float smoothness, float metallic) PbrFor(string stableId)
        {
            if (stableId.Contains("glass")) return (0.96f, 0f);
            if (stableId.Contains("metal")) return (0.62f, 0.9f);
            if (stableId.Contains("oak")) return (0.34f, 0f);
            if (stableId.Contains("walnut")) return (0.42f, 0f);   // walnut reads glossier than oak
            if (stableId.Contains("fabric")) return (0.06f, 0f);
            if (stableId.Contains("ceiling")) return (0.08f, 0f);  // matte painted ceiling
            return (0.12f, 0f);                                     // painted wall default
        }

        static Color ColorFor(string stableId)
        {
            if (stableId.Contains("oak")) return new Color(0.43f, 0.25f, 0.11f);
            if (stableId.Contains("walnut")) return new Color(0.2f, 0.09f, 0.045f);
            if (stableId.Contains("glass")) return new Color(0.52f, 0.72f, 0.78f, 0.28f);
            if (stableId.Contains("metal")) return new Color(0.13f, 0.14f, 0.15f);
            if (stableId.Contains("green")) return new Color(0.16f, 0.29f, 0.18f);
            if (stableId.Contains("fabric")) return new Color(0.29f, 0.34f, 0.36f);
            if (stableId.Contains("ceiling")) return new Color(0.93f, 0.93f, 0.91f);
            return new Color(0.82f, 0.8f, 0.75f);
        }
    }
}
