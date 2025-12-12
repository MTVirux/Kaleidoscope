namespace Kaleidoscope.Gui.TopBar
{
    using System;
    using System.Numerics;
    using Dalamud.Bindings.ImGui;
    using ImGui = Dalamud.Bindings.ImGui.ImGui;

    public static class TopBar
    {
        private const float BarHeight = 28f;
        // Animation state: 0 = hidden, 1 = visible
        private static float _progress = 0f;
        // Duration in seconds for the show/hide transition
        private const float TransitionDuration = 0.18f;
        // Optional callback that will be invoked when the topbar's exit-fullscreen button is pressed.
        public static Action? OnExitFullscreenRequested;
        // Force the bar to hide (used by MainWindow when exiting fullscreen so the bar can animate out)
        private static bool _forceHide = false;

        // Expose whether the topbar is currently animating (used so callers can keep drawing it until it finishes)
        public static bool IsAnimating => _progress > 0f && _progress < 1f;

        public static void ForceHide()
        {
            _forceHide = true;
        }

        // Draws a simple, absolute-positioned bar that sits at the top of the screen.
        // It uses ImGui's next-window positioning so it always appears at (0,0)
        // and spans the full display width.
        public static void Draw()
        {
            var io = ImGui.GetIO();

            // We'll animate the show/hide transition instead of instant show/hide.
            // Allow forcing hide (e.g., when exiting fullscreen) by MainWindow.
            var targetVisible = !_forceHide && io.KeyAlt;
            // update progress
            var dt = io.DeltaTime;
            var speed = TransitionDuration > 0f ? (1f / TransitionDuration) : 60f;
            if (targetVisible)
                _progress = MathF.Min(1f, _progress + dt * speed);
            else
                _progress = MathF.Max(0f, _progress - dt * speed);

            // nothing to draw when fully hidden
            if (_progress <= 0f)
            {
                // clear forced hide once fully hidden
                _forceHide = false;
                return;
            }

            var eased = 1f - (float)Math.Pow(1f - _progress, 3f);

            var displaySize = io.DisplaySize;

            // Slide down from off-screen into the title origin
            var yOffset = ImGui.GetFrameHeight();
            var rectMinY = yOffset - BarHeight * (1f - eased);
            var rectMin = new System.Numerics.Vector2(0f, rectMinY);
            var rectMax = new System.Numerics.Vector2(displaySize.X, rectMinY + BarHeight);

            var style = ImGui.GetStyle();
            var baseBg = style.Colors[(int)ImGuiCol.WindowBg];
            var baseText = style.Colors[(int)ImGuiCol.Text];
            var bgCol = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(baseBg.X, baseBg.Y, baseBg.Z, baseBg.W * eased));
            var textCol = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(baseText.X, baseText.Y, baseText.Z, baseText.W * eased));

            // Title origin alignment for screen-absolute: use style.WindowPadding.X as left origin
            var titleOriginX = style.WindowPadding.X;
            var textPos = new System.Numerics.Vector2(titleOriginX, rectMinY + (BarHeight - ImGui.GetFontSize()) / 2);

            var drawList = ImGui.GetForegroundDrawList();
            drawList.AddRectFilled(rectMin, rectMax, bgCol, 0f);
            drawList.AddText(textPos, textCol, "Kaleidoscope");

            // Add an exit-fullscreen button on the right side when fully or partially visible
            var btnSize = new System.Numerics.Vector2(28f, 20f);
            var padding = 8f;
            var btnMin = new System.Numerics.Vector2(rectMax.X - padding - btnSize.X, rectMin.Y + (BarHeight - btnSize.Y) / 2);
            var btnMax = btnMin + btnSize;
            var btnBg = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0f, 0f, 0f, 0.15f * eased));
            drawList.AddRectFilled(btnMin, btnMax, btnBg, 4f);
            // draw an X or icon center
            var xText = "✕";
            var txtCol = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(baseText.X, baseText.Y, baseText.Z, baseText.W * eased));
            var txtPos = new System.Numerics.Vector2(btnMin.X + (btnSize.X - ImGui.CalcTextSize(xText).X) / 2, btnMin.Y + (btnSize.Y - ImGui.GetFontSize()) / 2);
            drawList.AddText(txtPos, txtCol, xText);

            // Hit test for clicks
            var mouse = ImGui.GetMousePos();
            var hovered = mouse.X >= btnMin.X && mouse.Y >= btnMin.Y && mouse.X <= btnMax.X && mouse.Y <= btnMax.Y;
            if (hovered)
            {
                ImGui.SetTooltip("Exit fullscreen");
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    // Request exit; let MainWindow handle actual state change and force-hide
                    try { OnExitFullscreenRequested?.Invoke(); } catch { }
                }
            }
        }

        // Draw relative to a parent window position/size. The bar will be positioned
        // at the parent's top-left and span the parent's width.
        public static void Draw(System.Numerics.Vector2 parentPos, System.Numerics.Vector2 parentSize)
        {
            var io = ImGui.GetIO();
            // Animate visibility instead of instant show/hide
            var targetVisible = !_forceHide && io.KeyAlt;
            var dt = io.DeltaTime;
            var speed = TransitionDuration > 0f ? (1f / TransitionDuration) : 60f;
            if (targetVisible)
                _progress = MathF.Min(1f, _progress + dt * speed);
            else
                _progress = MathF.Max(0f, _progress - dt * speed);

            if (_progress <= 0f)
            {
                _forceHide = false;
                return;
            }

            var eased = 1f - (float)Math.Pow(1f - _progress, 3f);

            // Position the bar at the top-left of the parent window (window-local 0,0)
            // Slide in from above by interpolating Y
            var rectMinY = parentPos.Y - BarHeight * (1f - eased);
            var rectMin = new System.Numerics.Vector2(parentPos.X, rectMinY);
            var rectMax = new System.Numerics.Vector2(parentPos.X + parentSize.X, rectMinY + BarHeight);

            var style = ImGui.GetStyle();
            var baseBg = style.Colors[(int)ImGuiCol.WindowBg];
            var baseText = style.Colors[(int)ImGuiCol.Text];
            var bgCol = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(baseBg.X, baseBg.Y, baseBg.Z, baseBg.W * eased));
            var textCol = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(baseText.X, baseText.Y, baseText.Z, baseText.W * eased));

            // Title origin alignment: position text where the window title would start.
            var titleOriginX = parentPos.X + style.WindowPadding.X;
            var textPos = new System.Numerics.Vector2(titleOriginX, rectMinY + (BarHeight - ImGui.GetFontSize()) / 2);

            // Limit drawing to the parent window area for cleanliness.
            var drawList = ImGui.GetForegroundDrawList();
            drawList.PushClipRect(parentPos, parentPos + parentSize);
            drawList.AddRectFilled(rectMin, rectMax, bgCol, 0f);
            drawList.AddText(textPos, textCol, "Kaleidoscope");

            // Add an exit-fullscreen button to the right
            var btnSize = new System.Numerics.Vector2(28f, 20f);
            var padding = 8f;
            var btnMin = new System.Numerics.Vector2(rectMax.X - padding - btnSize.X, rectMin.Y + (BarHeight - btnSize.Y) / 2);
            var btnMax = btnMin + btnSize;
            var btnBg = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0f, 0f, 0f, 0.15f * eased));
            drawList.AddRectFilled(btnMin, btnMax, btnBg, 4f);
            var xText = "✕";
            var txtCol = ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(baseText.X, baseText.Y, baseText.Z, baseText.W * eased));
            var txtSize = ImGui.CalcTextSize(xText);
            var txtPos = new System.Numerics.Vector2(btnMin.X + (btnSize.X - txtSize.X) / 2, btnMin.Y + (btnSize.Y - ImGui.GetFontSize()) / 2);
            drawList.AddText(txtPos, txtCol, xText);

            var mouse = ImGui.GetMousePos();
            var hovered = mouse.X >= btnMin.X && mouse.Y >= btnMin.Y && mouse.X <= btnMax.X && mouse.Y <= btnMax.Y;
            if (hovered)
            {
                ImGui.SetTooltip("Exit fullscreen");
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    try { OnExitFullscreenRequested?.Invoke(); } catch { }
                }
            }

            drawList.PopClipRect();
        }
    }
}
