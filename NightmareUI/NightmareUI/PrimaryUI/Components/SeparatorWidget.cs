using System;

namespace NightmareUI.PrimaryUI.Components;
internal class SeparatorWidget : IWidget
{
    internal Action DrawAction;

    internal SeparatorWidget(Action draw)
    {
        DrawAction = draw;
    }
}
