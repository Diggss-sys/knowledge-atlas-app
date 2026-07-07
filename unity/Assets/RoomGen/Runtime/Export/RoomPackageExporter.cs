using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using RoomGen.Contracts;
using RoomGen.Lighting;
using RoomGen.Validation;
using UnityEngine;

namespace RoomGen.Export
{
    public static class RoomPackageExporter
    {
        public static ExportResult Export(ConditionPairSpec pair, string outputRoot = null)
        {
            var validation = PairValidator.Validate(pair);
            if (!validation.Ok)
                return new ExportResult
                {
                    Ok = false,
                    Message = "Export blocked by validation errors.",
                    Validation = validation
                };

            var safeId = Sanitize(pair.PairId);
            outputRoot ??= Path.Combine(Application.persistentDataPath, "RoomGenExports");
            Directory.CreateDirectory(outputRoot);
            var staging = Path.Combine(outputRoot, safeId);
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);

            var files = new Dictionary<string, string>
            {
                ["pair.json"] = RoomJson.Serialize(pair),
                ["control.room.json"] = RoomJson.Serialize(pair.Control),
                ["treatment.room.json"] = RoomJson.Serialize(pair.Treatment),
                ["validation.json"] = RoomJson.Serialize(validation),
                ["metrics.json"] = RoomJson.Serialize(new
                {
                    control = validation.ControlMetrics,
                    treatment = validation.TreatmentMetrics,
                    coupled = validation.CoupledMetrics
                })
            };

            foreach (var file in files)
                File.WriteAllText(Path.Combine(staging, file.Key), file.Value, new UTF8Encoding(false));

            var manifest = new BuildManifest
            {
                UnityVersion = Application.unityVersion,
                PairId = pair.PairId,
                CreatedUtc = DateTime.UtcNow.ToString("O")
            };
            manifest.Calibration["control"] = LightingCalibrator.MatchTargetLux(pair.Control);
            manifest.Calibration["treatment"] = LightingCalibrator.MatchTargetLux(pair.Treatment);
            manifest.AssetVersions["dining-room-preset"] = pair.Control.Provenance.PresetId;
            manifest.AssetVersions["generator"] = pair.Control.Provenance.GeneratorVersion;

            foreach (var file in files.Keys)
                manifest.Sha256[file] = HashFile(Path.Combine(staging, file));

            var manifestPath = Path.Combine(staging, "manifest.json");
            File.WriteAllText(manifestPath, RoomJson.Serialize(manifest), new UTF8Encoding(false));

            var zipPath = Path.Combine(outputRoot, safeId + ".zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using (var zipStream = File.Create(zipPath))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                foreach (var sourcePath in Directory.GetFiles(staging))
                {
                    var entry = archive.CreateEntry(
                        Path.GetFileName(sourcePath),
                        System.IO.Compression.CompressionLevel.Optimal);
                    using var source = File.OpenRead(sourcePath);
                    using var destination = entry.Open();
                    source.CopyTo(destination);
                }
            }

            return new ExportResult
            {
                Ok = true,
                PackagePath = zipPath,
                Message = "Research package exported.",
                Validation = validation
            };
        }

        public static ConditionPairSpec LoadPackage(string packageDirectory)
        {
            return RoomJson.Deserialize<ConditionPairSpec>(
                File.ReadAllText(Path.Combine(packageDirectory, "pair.json")));
        }

        static string HashFile(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "room-pair";
            foreach (var invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '-');
            return value.Trim().Replace(' ', '-').ToLowerInvariant();
        }
    }
}
