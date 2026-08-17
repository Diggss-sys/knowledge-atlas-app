using UnityEngine.UIElements;

namespace RoomGen.UI
{
    /// <summary>
    /// UI Toolkit sliders draw only a hairline tracker + a dragger — no value fill. This decorator
    /// injects a fill bar into the tracker and keeps its width at the value's percentage, giving the
    /// site-style "filled to the thumb" look (see ka-slider-fill in base.uss). Pure visuals: it reads
    /// the slider, never writes it.
    /// </summary>
    public static class SliderFill
    {
        public const string ClassName = "ka-slider-fill";

        public static void Attach(Slider slider)
        {
            var tracker = slider.Q<VisualElement>(className: "unity-base-slider__tracker");
            if (tracker == null || tracker.Q<VisualElement>(className: ClassName) != null) return;

            var fill = new VisualElement { name = "fill", pickingMode = PickingMode.Ignore };
            fill.AddToClassList(ClassName);
            fill.style.position = Position.Absolute;
            fill.style.left = 0;
            fill.style.top = 0;
            fill.style.bottom = 0;
            tracker.Insert(0, fill);

            slider.RegisterValueChangedCallback(_ => Refresh(slider));
            slider.RegisterCallback<GeometryChangedEvent>(_ => Refresh(slider));
            Refresh(slider);
        }

        /// <summary>Repaint an attached fill after a SetValueWithoutNotify model-to-widget sync.</summary>
        public static void Refresh(Slider slider)
        {
            var fill = slider?.Q<VisualElement>(className: ClassName);
            if (fill == null) return;
            var range = slider.highValue - slider.lowValue;
            var t = range > 0f ? (slider.value - slider.lowValue) / range : 0f;
            fill.style.width = Length.Percent(t * 100f);
        }
    }
}
