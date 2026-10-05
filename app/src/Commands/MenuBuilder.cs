using Godot;

namespace Dogeometric.App.Commands;

/// <summary>Builds SketchUp's main menu from the registry. Unimplemented commands show disabled.</summary>
public static class MenuBuilder
{
    public static MenuBar Build(CommandRegistry registry, Action<string> showHint, Action restoreHint)
    {
        // Not flat: a flat MenuBar draws no hover or open-menu highlight; the theme keeps the idle title transparent.
        var bar = new MenuBar { Flat = false };
        foreach (var top in registry.Menus)
        {
            var popup = BuildPopup(top, registry, showHint, restoreHint);
            popup.Name = top.Label;
            bar.AddChild(popup);
        }
        return bar;
    }

    private static PopupMenu BuildPopup(MenuNode node, CommandRegistry registry, Action<string> showHint, Action restoreHint)
    {
        var popup = new PopupMenu();
        var submenus = new Dictionary<MenuNode, PopupMenu>();
        // Items expanded from dynamic entries (recent files) run from here; their ids sit above every command id.
        var dynamicActions = new List<Action>();
        const int dynamicBase = 1_000_000;
        void Fill()
        {
            popup.Clear(false);
            dynamicActions.Clear();
            foreach (var child in node.Children ?? [])
            {
                if (child.IsSeparator)
                    popup.AddSeparator();
                else if (child.Children != null && registry.DynamicMenus.TryGetValue(child.Label, out var items))
                {
                    if (!submenus.TryGetValue(child, out var sub))
                        submenus[child] = sub = Dynamic(items);
                    popup.AddSubmenuNodeItem(child.Label, sub);
                }
                else if (child.Children != null)
                {
                    if (!submenus.TryGetValue(child, out var sub))
                        submenus[child] = sub = BuildPopup(child, registry, showHint, restoreHint);
                    popup.AddSubmenuNodeItem(child.Label, sub);
                }
                else if (registry.DynamicItems.TryGetValue(child.Id!.Value, out var inline))
                {
                    var added = false;
                    foreach (var (label, run) in inline())
                    {
                        popup.AddItem(label, dynamicBase + dynamicActions.Count);
                        dynamicActions.Add(run);
                        added = true;
                    }
                    if (!added)
                        AddCommandItem(popup, registry.Get(child.Id.Value), child.Label);
                }
                else
                    AddCommandItem(popup, registry.Get(child.Id!.Value), child.Label);
            }
        }
        Fill();

        popup.IdPressed += id =>
        {
            if (id >= dynamicBase)
                dynamicActions[(int)id - dynamicBase]();
            else
                registry.Execute((int)id);
        };
        popup.IdFocused += id =>
        {
            if (id < dynamicBase)
                showHint(registry.Get((int)id).Description);
        };
        var hasDynamic = (node.Children ?? []).Any(c => c.Id is { } id && registry.DynamicItems.ContainsKey(id));
        popup.AboutToPopup += () =>
        {
            if (hasDynamic)
                Fill();
            Refresh(popup, registry);
        };
        // Like SketchUp, the status bar shows the hovered command's description, then the tool hint again.
        popup.PopupHide += restoreHint;
        return popup;
    }

    private static PopupMenu Dynamic(Func<IEnumerable<(string Label, Action Run)>> items)
    {
        var popup = new PopupMenu();
        var actions = new List<Action>();
        popup.AboutToPopup += () =>
        {
            popup.Clear();
            actions.Clear();
            foreach (var (label, run) in items())
            {
                popup.AddItem(label, actions.Count);
                actions.Add(run);
            }
        };
        popup.IdPressed += id => actions[(int)id]();
        return popup;
    }

    private static void AddCommandItem(PopupMenu popup, Command cmd, string label)
    {
        if (cmd.IsChecked != null && cmd.IsRadio)
            popup.AddRadioCheckItem(label, cmd.Id, cmd.Shortcut);
        else if (cmd.IsChecked != null)
            popup.AddCheckItem(label, cmd.Id, cmd.Shortcut);
        else
            popup.AddItem(label, cmd.Id, cmd.Shortcut);

        popup.SetItemDisabled(popup.ItemCount - 1, !cmd.IsImplemented);
    }

    private static void Refresh(PopupMenu popup, CommandRegistry registry)
    {
        for (var i = 0; i < popup.ItemCount; i++)
        {
            var id = popup.GetItemId(i);
            if (id < 0 || id >= 1_000_000 || popup.IsItemSeparator(i))
                continue;
            var cmd = registry.Get(id);
            if (popup.GetItemSubmenuNode(i) == null)
                popup.SetItemDisabled(i, !cmd.IsImplemented);
            if (cmd.IsChecked is { } isChecked)
                popup.SetItemChecked(i, isChecked());
        }
    }
}
