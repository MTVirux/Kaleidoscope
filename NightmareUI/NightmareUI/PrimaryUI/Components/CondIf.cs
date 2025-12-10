using System;

namespace NightmareUI.PrimaryUI.Components;
internal class CondIf : IWidget
{
    internal Func<bool> Predicate;

    public CondIf(Func<bool> predicate)
    {
        Predicate = predicate;
    }
}
