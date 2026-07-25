using System;
using System.Collections.Generic;
using RoomGen.Contracts;
using RoomGen.Validation;
using UnityEngine;

namespace RoomGen.UI.Studio
{
    /// <summary>
    /// The Room Studio's brain. Pure C# and Unity-free apart from Mathf, so EditMode tests drive
    /// every behaviour without a scene: the view (RoomStudioPanelController) only reads these
    /// plain types and writes user input back.
    ///
    /// Three things here exist specifically to fix how the IMGUI studio failed:
    ///   • Slider bounds come from RoomSpecValidator, so a slider can no longer reach a value the
    ///     engine will refuse (the old panel kept its own constants and drifted from the gate).
    ///   • Edits are DEBOUNCED (ENGINE_SEAM ~150 ms). The old panel called Rebuild() on every
    ///     slider frame, regenerating the whole room mesh continuously while dragging.
    ///   • Unavailable actions carry a REASON. The old panel hid or greyed controls silently, which
    ///     is what made them read as broken.
    /// </summary>
    public sealed class RoomStudioViewModel
    {
        public const float DebounceSeconds = 0.15f;

        // ---- plain value types the view renders -------------------------------------------------

        public struct SliderSpec
        {
            public string Key;
            public string Label;
            public string Unit;
            public float Min;
            public float Max;
            public float Value;
            public int Decimals;
        }

        public struct ActionState
        {
            public bool Enabled;
            /// <summary>Shown to the operator whenever Enabled is false. Never empty when disabled.</summary>
            public string Reason;
        }

        public readonly struct RoomChoice
        {
            public readonly string Id;
            public readonly string Label;
            public RoomChoice(string id, string label) { Id = id; Label = label; }
        }

        // ---- catalogue --------------------------------------------------------------------------

        /// <summary>Glazed room first: daylight through windows is what the realism pass is judged on.</summary>
        public static readonly RoomChoice[] Rooms =
        {
            new RoomChoice("realism-test-pair", "Glazed realism room"),
            new RoomChoice("ceiling-height-pair", "Dining room"),
            new RoomChoice("ka-spec", "KA spec pair (adapter)"),
            new RoomChoice("saved", "Saved pair")
        };

        public static readonly string[] VariablePaths =
        {
            "geometry.ceiling_height_m", "geometry.width_m", "geometry.wall_bow"
        };

        public static readonly string[] VariableLabels = { "Ceiling height", "Width", "Wall bow" };

        // ---- state ------------------------------------------------------------------------------

        ConditionPairSpec _pair;
        float _pending = -1f;
        bool _vrAvailable;
        bool _walking;

        /// <summary>Raised once the debounce settles — the view rebuilds the rooms here, not per frame.</summary>
        public event Action RebuildRequested;

        public RoomStudioViewModel(ConditionPairSpec pair) => _pair = pair ?? new ConditionPairSpec();

        public ConditionPairSpec Pair => _pair;
        public string SelectedVariable => _pair.ManipulatedVariable ?? VariablePaths[0];
        public bool IsWalking => _walking;

        public void SetPair(ConditionPairSpec pair)
        {
            _pair = pair ?? new ConditionPairSpec();
            MarkDirty();
        }

        public void SetVrAvailable(bool available) => _vrAvailable = available;
        public void SetWalking(bool walking) => _walking = walking;

        // ---- debounce ---------------------------------------------------------------------------

        /// <summary>Queue a rebuild. Repeated calls while dragging coalesce into ONE rebuild.</summary>
        public void MarkDirty() => _pending = DebounceSeconds;

        /// <summary>Advance the debounce clock. Call once per frame with Time.deltaTime.</summary>
        public void Tick(float deltaSeconds)
        {
            if (_pending < 0f) return;
            _pending -= deltaSeconds;
            if (_pending > 0f) return;
            _pending = -1f;
            RebuildRequested?.Invoke();
        }

        /// <summary>True while an edit is waiting to be applied (the view can show a subtle hint).</summary>
        public bool HasPendingEdit => _pending >= 0f;

        // ---- sliders ----------------------------------------------------------------------------

        /// <summary>
        /// The two condition sliders for the active variable. Bounds track the validator, so the
        /// operator physically cannot drag into a spec the gate would reject.
        /// </summary>
        public SliderSpec ConditionSlider(bool treatment)
        {
            var spec = treatment ? _pair.Treatment : _pair.Control;
            var label = treatment ? "Treatment" : "Control";
            switch (SelectedVariable)
            {
                case "geometry.width_m":
                    return new SliderSpec
                    {
                        Key = "width", Label = label, Unit = " m",
                        Min = RoomSpecValidator.MinWidthM, Max = RoomSpecValidator.MaxWidthM,
                        Value = spec.Geometry.WidthM, Decimals = 2
                    };
                case "geometry.wall_bow":
                    return new SliderSpec
                    {
                        Key = "bow", Label = label, Unit = "",
                        Min = -1f, Max = 1f,
                        Value = spec.Geometry.WallBow?.Back ?? 0f, Decimals = 2
                    };
                default:
                    return new SliderSpec
                    {
                        Key = "ceiling", Label = label, Unit = " m",
                        Min = RoomSpecValidator.MinCeilingHeightM, Max = RoomSpecValidator.MaxCeilingHeightM,
                        Value = spec.Geometry.CeilingHeightM, Decimals = 2
                    };
            }
        }

        /// <summary>Apply a condition slider value (already clamped to the slider's legal range).</summary>
        public void SetConditionValue(bool treatment, float value)
        {
            var spec = treatment ? _pair.Treatment : _pair.Control;
            switch (SelectedVariable)
            {
                case "geometry.width_m":
                    spec.Geometry.WidthM = Clamp(value, RoomSpecValidator.MinWidthM, RoomSpecValidator.MaxWidthM);
                    break;
                case "geometry.wall_bow":
                    spec.Geometry.WallBow ??= new WallBowSpec();
                    var bow = Clamp(value, -1f, 1f);
                    // One conceptual variable applied to all four walls — the gate's whitelisted
                    // group variable, so the pair stays single-variable by construction.
                    spec.Geometry.WallBow.Front = spec.Geometry.WallBow.Back =
                        spec.Geometry.WallBow.Left = spec.Geometry.WallBow.Right = bow;
                    break;
                default:
                    spec.Geometry.CeilingHeightM = Clamp(value,
                        RoomSpecValidator.MinCeilingHeightM, RoomSpecValidator.MaxCeilingHeightM);
                    break;
            }
            MarkDirty();
        }

        /// <summary>Shared (nuisance) parameters — applied to BOTH conditions, so they can never
        /// create a pair difference and the gate stays green by construction.</summary>
        public IReadOnlyList<SliderSpec> SharedSliders() => new[]
        {
            new SliderSpec
            {
                Key = "shared.width_m", Label = "Width", Unit = " m",
                Min = RoomSpecValidator.MinWidthM, Max = RoomSpecValidator.MaxWidthM,
                Value = _pair.Control.Geometry.WidthM, Decimals = 2
            },
            new SliderSpec
            {
                Key = "shared.length_m", Label = "Length", Unit = " m",
                Min = RoomSpecValidator.MinLengthM, Max = RoomSpecValidator.MaxLengthM,
                Value = _pair.Control.Geometry.LengthM, Decimals = 2
            },
            new SliderSpec
            {
                Key = "shared.color_temperature_k", Label = "Colour temp", Unit = " K",
                Min = 2700f, Max = 6500f,
                Value = _pair.Control.Lighting.ColorTemperatureK, Decimals = 0
            },
            new SliderSpec
            {
                Key = "shared.target_lux", Label = "Brightness", Unit = " lux",
                Min = 100f, Max = 500f,
                Value = _pair.Control.Lighting.TargetLux, Decimals = 0
            }
        };

        public void SetSharedValue(string key, float value)
        {
            switch (key)
            {
                case "shared.width_m":
                    var w = Clamp(value, RoomSpecValidator.MinWidthM, RoomSpecValidator.MaxWidthM);
                    _pair.Control.Geometry.WidthM = _pair.Treatment.Geometry.WidthM = w;
                    break;
                case "shared.length_m":
                    var l = Clamp(value, RoomSpecValidator.MinLengthM, RoomSpecValidator.MaxLengthM);
                    _pair.Control.Geometry.LengthM = _pair.Treatment.Geometry.LengthM = l;
                    break;
                case "shared.color_temperature_k":
                    var k = Clamp(value, 2700f, 6500f);
                    _pair.Control.Lighting.ColorTemperatureK = _pair.Treatment.Lighting.ColorTemperatureK = k;
                    break;
                case "shared.target_lux":
                    var lux = Clamp(value, 100f, 500f);
                    _pair.Control.Lighting.TargetLux = _pair.Treatment.Lighting.TargetLux = lux;
                    break;
                default:
                    return; // unknown key: ignore rather than corrupt the spec
            }
            MarkDirty();
        }

        // ---- action availability ----------------------------------------------------------------

        /// <summary>VR needs a headset; say so instead of hiding the button.</summary>
        public ActionState VrAction() => _vrAvailable
            ? new ActionState { Enabled = true, Reason = "" }
            : new ActionState { Enabled = false, Reason = "No VR headset detected." };

        /// <summary>
        /// The seam walk needs the WALK generator to hold a room. Every loader must drive the seam;
        /// when one does not, this is where the operator finds out why the button is dark instead of
        /// clicking a button that silently does nothing.
        /// </summary>
        public ActionState SeamWalkAction(bool walkRoomReady) => walkRoomReady
            ? new ActionState { Enabled = true, Reason = "" }
            : new ActionState { Enabled = false, Reason = "Load a pair first — the walk room is empty." };

        public string RoomTitle(string roomId)
        {
            foreach (var room in Rooms)
                if (room.Id == roomId) return room.Label + " · " + VariableLabel();
            return VariableLabel();
        }

        public string VariableLabel()
        {
            var index = Array.IndexOf(VariablePaths, SelectedVariable);
            return VariableLabels[index < 0 ? 0 : index];
        }

        static float Clamp(float value, float min, float max) => Mathf.Clamp(value, min, max);
    }
}
