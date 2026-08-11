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
            else if (stableId.Contains("glass"))
            {
                // The committed builtin-glass.mat is authored OPAQUE (SurfaceType 0, alpha 1), so
                // window panes render as flat blue boxes. Clone it (never dirty the shared asset) and
                // flip the clone to a transparent refractive pane so sky + sun read through.
                material = new Material(material) { name = material.name + " (transparent)" };
                if (material.HasProperty("_BaseColor"))
                {
                    var c = material.GetColor("_BaseColor"); c.a = 0.15f;
                    material.SetColor("_BaseColor", c);
                }
                ConfigureTransparentGlass(material);
            }
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

            // Glass must be a real transparent dielectric so sky + sun read through the window
            // panes instead of rendering as an opaque blue box. HDRP/Lit needs the full transparent
            // surface state set explicitly — SurfaceType alone is inert without blend + queue + keyword.
            if (stableId.Contains("glass")) ConfigureTransparentGlass(material);
            return material;
        }

        // Flip an HDRP/Lit material into a thin refractive glass pane. All the state HDRP samples at
        // shader-select time: SurfaceType=Transparent, premultiplied alpha blend, ZWrite off,
        // transparent render queue, thin refraction (IOR ~1.5) so the sky bends slightly through it.
        static void ConfigureTransparentGlass(Material material)
        {
            if (!material.HasProperty("_SurfaceType")) return; // Standard fallback: alpha color is enough

            material.SetFloat("_SurfaceType", 1f);          // 0 Opaque, 1 Transparent
            material.SetFloat("_BlendMode", 0f);            // Alpha
            material.SetFloat("_SrcBlend", 1f);             // One (HDRP premultiplies alpha in-shader)
            material.SetFloat("_DstBlend", 10f);            // OneMinusSrcAlpha
            material.SetFloat("_AlphaDstBlend", 10f);

            // THE bug that made every window a flat blown-white card: with premultiplied blending
            // (SrcBlend=One, DstBlend=OneMinusSrcAlpha), alpha=1 makes DstBlend = 1-1 = 0, so the
            // refracted/transmitted background contributes NOTHING to the final pixel — the pane is
            // structurally opaque no matter what _SurfaceType says. The committed .mat is authored
            // OPAQUE (alpha=1) and nothing here ever lowered it after flipping to Transparent. What
            // reached the screen was only the specular/SSR reflection of the sky sitting on a flat
            // base color — no sun disc, no sky, no depth. A real window needs a LOW alpha so the
            // background actually shows through.
            var c = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
            c.a = 0.08f;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", c);
            if (material.HasProperty("_Color")) material.SetColor("_Color", c);

            // REVERTED a same-session Smoothness override (was 0.35, meant to weaken the SSR mirror
            // so transmission would show). Wrong lever: HDRP's _REFRACTION_THIN samples its
            // color-pyramid mip level FROM roughness, so lowering smoothness blurs the TRANSMITTED
            // view exactly as much as the reflection — it can't isolate one from the other. Left at
            // PbrFor's physically-real 0.96 (glass IS a near-mirror); the reflection-vs-transmission
            // balance has to be solved elsewhere (SSR strength/weight), not by roughening the BSDF.
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_TransparentZWrite", 0f);
            material.SetFloat("_ZTestDepthEqualForOpaque", 4f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_BLENDMODE_PRESERVE_SPECULAR_LIGHTING");
            material.DisableKeyword("_ENABLE_FOG_ON_TRANSPARENT");

            // Let SSR reflect off the glass (the committed .mat ships with transparent SSR disabled).
            if (material.HasProperty("_ReceivesSSRTransparent")) material.SetFloat("_ReceivesSSRTransparent", 1f);
            material.DisableKeyword("_DISABLE_SSR_TRANSPARENT");

            // Thin refraction: single-interface bend, right model for a flat window pane.
            if (material.HasProperty("_RefractionModel"))
            {
                material.SetFloat("_RefractionModel", 3f);  // 0 None, 1 Box, 2 Sphere, 3 Thin
                material.EnableKeyword("_REFRACTION_THIN");
                if (material.HasProperty("_Ior")) material.SetFloat("_Ior", 1.5f);
            }

            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent; // 3000
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
            if (stableId.Contains("glass")) return new Color(0.6f, 0.78f, 0.82f, 0.15f);
            if (stableId.Contains("metal")) return new Color(0.13f, 0.14f, 0.15f);
            if (stableId.Contains("green")) return new Color(0.16f, 0.29f, 0.18f);
            if (stableId.Contains("fabric")) return new Color(0.29f, 0.34f, 0.36f);
            if (stableId.Contains("ceiling")) return new Color(0.93f, 0.93f, 0.91f);
            return new Color(0.82f, 0.8f, 0.75f);
        }
    }
}
