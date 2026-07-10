namespace RoomGen.Runner
{
    /// <summary>
    /// The runner's honesty gate (EXPERIMENT_RUNTIME.md): refuse to run a study whose
    /// <c>validation.ok</c> is false or whose <c>status</c> is not <c>published</c>. Takes JSON so
    /// the EditMode test can call it without naming Newtonsoft types.
    /// </summary>
    public static class StudyGate
    {
        public static bool CanRun(string studyJson, out string reason)
        {
            reason = "";
            var study = ResponseJson.Parse(studyJson);

            var status = (string)study["status"];
            if (status != "published")
            {
                reason = $"study status is '{status ?? "(none)"}', must be 'published'";
                return false;
            }

            var okToken = study["validation"]?["ok"];
            var ok = okToken != null && okToken.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)okToken;
            if (!ok)
            {
                reason = "study validation.ok is not boolean true (unvalidated pair)";
                return false;
            }

            return true;
        }
    }
}
