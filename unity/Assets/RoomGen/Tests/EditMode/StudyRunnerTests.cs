using System.IO;
using NUnit.Framework;
using RoomGen.Runner;
using UnityEngine;

namespace RoomGen.Tests
{
    public class StudyRunnerTests
    {
        static string SampleStudy => Resources.Load<TextAsset>("RoomGen/Examples/ceiling-study").text;

        [Test]
        public void Study_driven_rating_session_runs_and_writes_valid_csv()
        {
            var csv = Path.Combine(Application.temporaryCachePath, "study-run.csv");
            var jsonl = Path.Combine(Application.temporaryCachePath, "study-run.jsonl");

            var result = StudyRunner.Run(SampleStudy, seed: 160, csv, jsonl);

            Assert.IsTrue(result.Ran, "A published, validated study must run. Reason: " + result.Reason);
            Assert.AreEqual(4, result.Written, "The study declares 4 trials.");
            Assert.IsTrue(result.AllValid, "Every study-driven row must validate.");

            // manipulated_variables is sourced from validation.diff keys and stamped into every row.
            StringAssert.Contains("shell.ceiling_height_m", File.ReadAllText(csv));
        }

        [Test]
        public void Study_runner_refuses_an_unpublished_study()
        {
            var csv = Path.Combine(Application.temporaryCachePath, "study-refuse.csv");
            var result = StudyRunner.Run("{\"status\":\"draft\",\"validation\":{\"ok\":true}}", 1, csv, csv + ".jsonl");

            Assert.IsFalse(result.Ran, "A draft study must be refused.");
            StringAssert.Contains("published", result.Reason);
            Assert.AreEqual(0, result.Written);
        }
    }
}
