using Godot;

namespace Dogeometric.App.UI;

/// <summary>
/// File › Recover Backup…: every automatic backup, newest first, by model and time. The chosen one opens as an
/// unsaved copy; "Open Folder" shows where they are kept.
/// </summary>
public static class RecoverBackupDialog
{
    /// <param name="open">Opens a backup file; the second argument names the model it was a copy of.</param>
    public static void Show(Node parent, Action<string, string> open)
    {
        var backups = Backups.All();
        var d = new ConfirmationDialog { Title = "Recover Backup", OkButtonText = "Open", CancelButtonText = "Cancel" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(560, 360) };
        box.AddChild(new Label
        {
            Text = backups.Count == 0
                ? "There are no backups yet. They are made every few minutes while a model has unsaved changes (Preferences › General)."
                : "Backups are copies made while you work; your own files are never changed. The one you open is an unsaved copy.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        foreach (var f in backups)
            list.AddItem($"{f.Directory!.Name}    {f.LastWriteTime:g}    {f.Length / 1024.0:0} KB");
        if (backups.Count > 0)
            list.Select(0);
        box.AddChild(list);
        d.AddChild(box);
        d.AddButton("Open Folder", false, "folder");
        d.GetOkButton().Disabled = backups.Count == 0;

        void OpenSelected()
        {
            if (list.GetSelectedItems() is not [var i] || i >= backups.Count)
                return;
            d.QueueFree();
            open(backups[i].FullName, backups[i].Directory!.Name + ".dog");
        }

        d.Confirmed += OpenSelected;
        list.ItemActivated += _ => OpenSelected();
        d.CustomAction += action =>
        {
            if (action == "folder")
            {
                System.IO.Directory.CreateDirectory(Backups.Folder);
                OS.ShellOpen(Backups.Folder);
            }
        };
        d.Canceled += d.QueueFree;
        parent.AddChild(d);
        d.PopupCentered();
    }
}
