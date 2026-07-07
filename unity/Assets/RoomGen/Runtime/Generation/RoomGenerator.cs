using System.Linq;
using RoomGen.Contracts;
using RoomGen.Lighting;
using RoomGen.Metrics;
using RoomGen.Validation;
using UnityEngine;

namespace RoomGen.Generation
{
    public sealed class RoomGenerator : MonoBehaviour
    {
        [SerializeField] int generatedLayer;

        GameObject generatedRoot;

        public RoomSpec CurrentSpec { get; private set; }
        public RoomBuildResult LastResult { get; private set; }

        public RoomBuildResult Build(RoomSpec spec)
        {
            Clear();
            if (spec == null)
            {
                LastResult = new RoomBuildResult { Ok = false };
                LastResult.Warnings.Add("Room specification is missing.");
                return LastResult;
            }
            CurrentSpec = RoomJson.Clone(spec);
            var issues = RoomSpecValidator.Validate(CurrentSpec);
            if (issues.Any(issue => issue.Severity == "error"))
            {
                LastResult = new RoomBuildResult
                {
                    Ok = false,
                    Metrics = RoomMetrics.Calculate(CurrentSpec)
                };
                LastResult.Warnings.AddRange(issues.Select(issue => issue.Path + ": " + issue.Message));
                return LastResult;
            }

            generatedRoot = new GameObject("Generated Room");
            generatedRoot.layer = generatedLayer;
            generatedRoot.transform.SetParent(transform, false);
            var surfaces = new SurfaceResolver();

            ShellGenerator.Build(CurrentSpec, generatedRoot.transform, surfaces, generatedLayer);
            var openings = new GameObject("02 Openings");
            openings.layer = generatedLayer;
            openings.transform.SetParent(generatedRoot.transform, false);
            OpeningGenerator.BuildInserts(CurrentSpec, openings.transform, surfaces, generatedLayer);
            var missingAssets = FurnitureLayoutResolver.Build(
                CurrentSpec, generatedRoot.transform, surfaces, generatedLayer);
            var lighting = LightingSystem.Build(CurrentSpec, generatedRoot.transform, generatedLayer);

            LastResult = new RoomBuildResult
            {
                Root = generatedRoot,
                Ok = missingAssets.Count == 0 && lighting.Ok,
                Metrics = RoomMetrics.Calculate(CurrentSpec),
                Lighting = lighting
            };
            foreach (var asset in missingAssets)
                LastResult.Warnings.Add("Missing furniture asset: " + asset);
            if (!lighting.Ok)
                LastResult.Warnings.Add(
                    $"Lighting probes exceed tolerance by {lighting.MaximumErrorLux:0.0} lux.");
            return LastResult;
        }

        public void Clear()
        {
            if (generatedRoot == null) return;
            if (Application.isPlaying)
            {
                generatedRoot.SetActive(false);
                Destroy(generatedRoot);
            }
            else
                DestroyImmediate(generatedRoot);
            generatedRoot = null;
        }

        public void SetGeneratedLayer(int layer)
        {
            generatedLayer = layer;
            if (generatedRoot != null)
                GenerationUtil.SetLayerRecursively(generatedRoot, layer);
        }
    }
}
