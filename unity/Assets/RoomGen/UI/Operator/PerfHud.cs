using RoomGen.Metrics;
using UnityEngine;

namespace RoomGen.UI
{
    /// <summary>
    /// The operator's live frame-rate readout: a small top-right counter, colour-coded against the
    /// team's targets (green ≥60, amber ≥45, red below). Drawn with OnGUI ON PURPOSE — the operator
    /// panel (a UIToolkit document) is hidden during a walk, and the walk is exactly when frame rate
    /// matters most, so the HUD has to live outside the panel and paint over everything.
    ///
    /// This is the OPERATOR cockpit only. The participant runner never attaches it — an on-screen fps
    /// number would be a distraction/confound; there, frames are logged silently (see PerfLog).
    /// </summary>
    public sealed class PerfHud : MonoBehaviour
    {
        public bool Visible = true;

        readonly RollingPerf _rolling = new RollingPerf(0.5f);
        GUIStyle _style;

        void Update() => _rolling.Add(Time.unscaledDeltaTime);

        void OnGUI()
        {
            if (!Visible) return;
            var s = _rolling.Current;
            if (!s.HasData) return;

            EnsureStyle();
            var text = $"{s.AvgFps:0} fps   ·   min {s.MinFps:0}   ·   {s.HitchCount} hitch";
            const float w = 220f, h = 26f;
            var rect = new Rect(Screen.width - w - 12f, 12f, w, h);

            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;

            _style.normal.textColor = Colour(s);
            GUI.Label(rect, text, _style);
        }

        static Color Colour(PerfSample s)
        {
            switch (PerfTargets.Rate(s))
            {
                case PerfTargets.Good: return new Color(0.55f, 0.85f, 0.60f);
                case PerfTargets.Ok:   return new Color(0.96f, 0.76f, 0.36f);
                default:               return new Color(0.95f, 0.47f, 0.47f);
            }
        }

        void EnsureStyle()
        {
            if (_style != null) return;
            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }
    }
}
