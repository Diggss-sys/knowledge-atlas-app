using UnityEngine;

namespace RoomGen.Validation
{
    public static class ScreenshotRegression
    {
        public static float MeanAbsoluteError(Texture2D baseline, Texture2D candidate)
        {
            if (baseline == null || candidate == null)
                return float.PositiveInfinity;
            if (baseline.width != candidate.width || baseline.height != candidate.height)
                return float.PositiveInfinity;

            var left = baseline.GetPixels32();
            var right = candidate.GetPixels32();
            var total = 0d;
            for (var i = 0; i < left.Length; i++)
            {
                total += Mathf.Abs(left[i].r - right[i].r);
                total += Mathf.Abs(left[i].g - right[i].g);
                total += Mathf.Abs(left[i].b - right[i].b);
            }
            return (float)(total / (left.Length * 3d * 255d));
        }

        public static bool Matches(Texture2D baseline, Texture2D candidate, float tolerance = 0.02f) =>
            MeanAbsoluteError(baseline, candidate) <= tolerance;
    }
}
