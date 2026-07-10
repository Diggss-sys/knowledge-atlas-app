using System.Collections.Generic;
using RoomGen.Contracts;
using UnityEngine;

namespace RoomGen.Generation
{
    public static class FurnitureLayoutResolver
    {
        public static List<string> Build(
            RoomSpec spec,
            Transform parent,
            SurfaceResolver surfaces,
            int layer)
        {
            var missing = new List<string>();
            var root = new GameObject("03 Furniture");
            root.layer = layer;
            root.transform.SetParent(parent, false);

            foreach (var placement in spec.Furniture)
            {
                var item = new GameObject(placement.SlotId + " [" + placement.AssetId + "]");
                item.layer = layer;
                item.transform.SetParent(root.transform, false);
                // Ceiling-mounted items derive their height from the room, not from the spec value —
                // see FurniturePlacementSpec.CeilingMounted.
                var y = placement.CeilingMounted ? spec.Geometry.CeilingHeightM : placement.PositionM.Y;
                item.transform.localPosition = new Vector3(
                    placement.PositionM.X, y, placement.PositionM.Z);
                item.transform.localRotation = Quaternion.Euler(0f, placement.RotationYDeg, 0f);

                var resourceId = placement.AssetId.Replace('.', '-');
                var prefab = Resources.Load<GameObject>("RoomGen/Furniture/" + resourceId);
                if (prefab != null)
                {
                    var instance = Object.Instantiate(prefab, item.transform, false);
                    instance.name = "Model";
                    GenerationUtil.SetLayerRecursively(instance, layer);
                    continue;
                }

                if (!placement.AssetId.StartsWith("builtin."))
                {
                    missing.Add(placement.AssetId);
                    BuildPlaceholder(item.transform, placement, surfaces, layer);
                    continue;
                }

                BuildBuiltin(item.transform, placement.AssetId, surfaces, layer);
            }

            return missing;
        }

        static void BuildBuiltin(Transform parent, string assetId, SurfaceResolver surfaces, int layer)
        {
            switch (assetId)
            {
                case "builtin.dining-table":
                    BuildTable(parent, surfaces, layer);
                    break;
                case "builtin.dining-chair":
                    BuildChair(parent, surfaces, layer);
                    break;
                case "builtin.sideboard":
                    BuildSideboard(parent, surfaces, layer);
                    break;
                case "builtin.plant":
                    BuildPlant(parent, surfaces, layer);
                    break;
                case "builtin.pendant-light":
                    BuildPendant(parent, surfaces, layer);
                    break;
                default:
                    BuildPlaceholder(parent, null, surfaces, layer);
                    break;
            }
        }

        static void BuildTable(Transform parent, SurfaceResolver surfaces, int layer)
        {
            var wood = surfaces.Resolve("builtin.walnut");
            GenerationUtil.CreateBox("Tabletop", parent, new Vector3(2.15f, 0.09f, 1.05f),
                new Vector3(0f, 0.76f, 0f), wood, layer);
            foreach (var x in new[] { -0.88f, 0.88f })
            foreach (var z in new[] { -0.36f, 0.36f })
                GenerationUtil.CreateBox("Leg", parent, new Vector3(0.09f, 0.72f, 0.09f),
                    new Vector3(x, 0.36f, z), wood, layer);
        }

        static void BuildChair(Transform parent, SurfaceResolver surfaces, int layer)
        {
            var wood = surfaces.Resolve("builtin.walnut");
            var fabric = surfaces.Resolve("builtin.fabric-charcoal");
            GenerationUtil.CreateBox("Seat", parent, new Vector3(0.5f, 0.1f, 0.5f),
                new Vector3(0f, 0.48f, 0f), fabric, layer);
            // Backrest on the item's -z side: contract convention is rotation 0 = the sitter FACES +z
            // (spec/PRESETS.md rotation table). The backrest sits behind the sitter.
            GenerationUtil.CreateBox("Back", parent, new Vector3(0.5f, 0.62f, 0.08f),
                new Vector3(0f, 0.78f, -0.21f), fabric, layer);
            foreach (var x in new[] { -0.19f, 0.19f })
            foreach (var z in new[] { -0.19f, 0.19f })
                GenerationUtil.CreateBox("Leg", parent, new Vector3(0.055f, 0.45f, 0.055f),
                    new Vector3(x, 0.225f, z), wood, layer);
        }

        static void BuildSideboard(Transform parent, SurfaceResolver surfaces, int layer)
        {
            var wood = surfaces.Resolve("builtin.oak");
            var metal = surfaces.Resolve("builtin.metal-black");
            GenerationUtil.CreateBox("Cabinet", parent, new Vector3(1.65f, 0.72f, 0.45f),
                new Vector3(0f, 0.55f, 0f), wood, layer);
            GenerationUtil.CreateBox("Top", parent, new Vector3(1.72f, 0.06f, 0.5f),
                new Vector3(0f, 0.94f, 0f), wood, layer);
            GenerationUtil.CreateBox("Handle Left", parent, new Vector3(0.18f, 0.025f, 0.025f),
                new Vector3(-0.42f, 0.62f, 0.24f), metal, layer);
            GenerationUtil.CreateBox("Handle Right", parent, new Vector3(0.18f, 0.025f, 0.025f),
                new Vector3(0.42f, 0.62f, 0.24f), metal, layer);
        }

        static void BuildPlant(Transform parent, SurfaceResolver surfaces, int layer)
        {
            var pot = surfaces.Resolve("builtin.ceiling-white");
            var green = surfaces.Resolve("builtin.green");
            var potObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            potObject.name = "Pot";
            potObject.layer = layer;
            potObject.transform.SetParent(parent, false);
            potObject.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            potObject.transform.localScale = new Vector3(0.34f, 0.25f, 0.34f);
            potObject.GetComponent<MeshRenderer>().sharedMaterial = pot;
            // Slight lean only: a strong Z tilt read as a fallen object once real furniture
            // models landed around it (Fable review).
            GenerationUtil.CreateBox("Plant", parent, new Vector3(0.45f, 1.05f, 0.45f),
                new Vector3(0f, 0.95f, 0f), green, layer,
                Quaternion.Euler(0f, 25f, 6f));
        }

        // Ceiling-mounted: the item's origin is placed AT the ceiling plane (SlotResolver ceiling
        // slots), so the fixture is built hanging DOWNWARD from local y = 0.
        static void BuildPendant(Transform parent, SurfaceResolver surfaces, int layer)
        {
            var metal = surfaces.Resolve("builtin.metal-black");
            var shade = surfaces.Resolve("builtin.warm-white");
            GenerationUtil.CreateBox("Cord", parent, new Vector3(0.02f, 0.5f, 0.02f),
                new Vector3(0f, -0.25f, 0f), metal, layer);
            var shadeObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shadeObject.name = "Shade";
            shadeObject.layer = layer;
            shadeObject.transform.SetParent(parent, false);
            shadeObject.transform.localPosition = new Vector3(0f, -0.58f, 0f);
            shadeObject.transform.localScale = new Vector3(0.38f, 0.09f, 0.38f);
            shadeObject.GetComponent<MeshRenderer>().sharedMaterial = shade;
        }

        static void BuildPlaceholder(
            Transform parent,
            FurniturePlacementSpec placement,
            SurfaceResolver surfaces,
            int layer)
        {
            var width = placement?.FootprintM.X ?? 0.8f;
            var depth = placement?.FootprintM.Y ?? 0.8f;
            GenerationUtil.CreateBox("Missing Asset", parent, new Vector3(width, 0.75f, depth),
                new Vector3(0f, 0.375f, 0f), surfaces.Resolve("builtin.missing"), layer);
        }
    }
}
