using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>Edit for a drawers review (spec section 1): every move as a tick box under its drawer, and a name box for each drawer the plan
/// creates. Existing drawers are renamed from their own menu, not here.</summary>
internal sealed partial class DrawerPlanEditWindow : Window
{
    private readonly Dictionary<string, CheckBox> _moves = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBox> _names = new(StringComparer.Ordinal);

    public DrawerPlanEditWindow(DrawerPlan plan, IReadOnlyCollection<string> existingIds)
    {
        InitializeComponent();
        foreach (var d in plan.Drawers)
        {
            var group = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            if (existingIds.Contains(d.Id))
                group.Children.Add(Heading(StreamerMode.Mask.Name(CodenameCategory.Folder, d.Name)));   // streamer: masked - Mask.Name gives the codename while on
            else
            {
                var box = new TextBox { Text = d.Name, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) };   // streamer: masked - the user's own new drawer's name, editable here
                AutomationProperties.SetName(box, "New drawer's name");
                _names[d.Id] = box;
                group.Children.Add(box);
            }
            foreach (var m in plan.Moves.Where(m => m.DrawerId == d.Id))
            {
                var name = StreamerMode.Mask.Omit(DrawerNames.Display(Path.GetFileName(m.Source))) ?? "File";   // streamer: masked - Mask.Omit hides the file name while on
                var tick = new CheckBox { Content = name, IsChecked = true, Margin = new Thickness(4, 2, 0, 2) };
                tick.SetResourceReference(ForegroundProperty, "Sanduhr.Brush.Text");
                AutomationProperties.SetName(tick, StreamerMode.Mask.Omit(DrawerNames.Display(Path.GetFileName(m.Source))) ?? "File");   // streamer: masked - Mask.Omit hides the file name while on
                _moves[m.Source] = tick;
                group.Children.Add(tick);
            }
            Groups.Children.Add(group);
        }
        OkButton.Click += (_, _) =>
        {
            Result = TryResult(out var error);
            Error.Text = error ?? "";
            if (Result is not null) DialogResult = true;
        };
    }

    public DrawerPlanEdits? Result { get; private set; }

    public bool CanRename(string id) => _names.ContainsKey(id);
    public void Untick(string path) => _moves[path].IsChecked = false;
    public void Rename(string id, string name) => _names[id].Text = name;

    public DrawerPlanEdits? TryResult(out string? error)
    {
        foreach (var box in _names.Values)
            if (!DrawerNames.IsValid(box.Text.Trim()))
            {
                error = $"\"{box.Text.Trim()}\" can't be a folder name.";
                return null;
            }
        error = null;
        return new DrawerPlanEdits(
            _moves.Where(kv => kv.Value.IsChecked != true).Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase),
            _names.ToDictionary(kv => kv.Key, kv => kv.Value.Text.Trim(), StringComparer.Ordinal));
    }

    private static TextBlock Heading(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        t.SetResourceReference(ForegroundProperty, "Sanduhr.Brush.Text");
        return t;
    }
}
