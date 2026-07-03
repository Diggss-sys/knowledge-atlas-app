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
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", stableId.Contains("oak") ? 0.28f : 0.12f);
            return material;
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
