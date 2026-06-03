using System;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Optimizer.WinUI.Controls.Hud;

/// <summary>Attaches a subtle scale + lift on pointer hover via Composition (no layout reflow).</summary>
internal static class HoverLift
{
    public static void Attach(FrameworkElement element, float scale = 1.03f, float lift = -5f)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        element.PointerEntered += (_, _) => Animate(element, scale, lift);
        element.PointerExited  += (_, _) => Animate(element, 1.0f, 0f);
    }

    private static void Animate(FrameworkElement element, float scale, float liftY)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var comp = visual.Compositor;
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2f, (float)element.ActualHeight / 2f, 0f);

        var duration = TimeSpan.FromMilliseconds(160);

        var scaleAnim = comp.CreateVector3KeyFrameAnimation();
        scaleAnim.InsertKeyFrame(1f, new Vector3(scale, scale, 1f));
        scaleAnim.Duration = duration;
        visual.StartAnimation("Scale", scaleAnim);

        var liftAnim = comp.CreateVector3KeyFrameAnimation();
        liftAnim.InsertKeyFrame(1f, new Vector3(0f, liftY, 0f));
        liftAnim.Duration = duration;
        visual.StartAnimation("Translation", liftAnim);
    }
}
