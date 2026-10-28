using System.Collections.ObjectModel;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Brings a bound list to <c>wanted</c> without clearing it: items that left are removed, the rest are moved or inserted into order. A combo
/// bound to the list keeps its selection, because the selected item is never removed while it is wanted (a combo whose selected item leaves
/// its items writes null back, WF2).
/// </summary>
internal static class FontListSync
{
    public static void Sync<T>(ObservableCollection<T> list, IReadOnlyList<T> wanted)
    {
        var eq = EqualityComparer<T>.Default;
        for (int i = list.Count - 1; i >= 0; i--)
            if (!wanted.Contains(list[i], eq)) list.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < list.Count && eq.Equals(list[i], wanted[i])) continue;
            int at = -1;
            for (int j = i + 1; j < list.Count; j++)
                if (eq.Equals(list[j], wanted[i])) { at = j; break; }
            if (at >= 0) list.Move(at, i);
            else list.Insert(i, wanted[i]);
        }
    }
}
