using ECommons.Configuration;
using OtterGui.Classes;
using OtterGui;
using OtterGui.Filesystem;
using OtterGui.FileSystem.Selector;
using System.IO;
using OtterGui.Raii;
using Dalamud.Interface.Colors;
using Dalamud.Interface;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using System.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using ECommons;
using ECommons.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI;
using Dalamud.Interface.Utility;


#pragma warning disable CS8618
#pragma warning disable

namespace NightmareUI.OtterGuiWrapper.FileSystems.Generic;
public sealed class GenericFileSystem<TData> : FileSystem<TData> where TData : class, IFileSystemStorage, new()
{
    string FilePath;
    public readonly FileSystemSelector Selector;
    public readonly ICollection<TData> DataStorage;
    public GenericFileSystem(ICollection<TData> dataStorage, string dataFilePrefix)
    {
        FilePath = Path.Combine(Svc.PluginInterface.ConfigDirectory.FullName, dataFilePrefix + "FileSystem.json");
        DataStorage = dataStorage;
        EzConfig.OnSave += Save;
        try
        {
            var info = new FileInfo(FilePath);
            if (info.Exists)
            {
                Load(info, DataStorage, ConvertToIdentifier, ConvertToName);
            }
            Selector = new(this);
        }
        catch (Exception e)
        {
            e.Log();
        }
    }

    public bool Create(TData newItem, string newName, out string error)
    {
        if (DataStorage.Contains(newItem))
        {
            error = "This item already exists";
            return false;
        }
        while (DataStorage.Any(x => x.GUID == newItem.GUID))
        {
            newItem.GUID = Guid.NewGuid();
        }
        try
        {
            CreateLeaf(Root, newName, newItem);
        }
        catch (Exception e)
        {
            error = e.ToString();
            return false;
        }
        DataStorage.Add(newItem);
        newItem.SetCustomName(newName);
        error = "";
        return true;
    }

    public void DoDelete(TData item)
    {
        PluginLog.Debug($"Deleting {item.GUID}");
        DataStorage.Remove(item);
        if (FindLeaf(item, out var leaf))
        {
            Delete(leaf);
        }
        Save();
    }

    public bool FindLeaf(TData? item, [NotNullWhen(true)] out Leaf? leaf)
    {
        leaf = Root.GetAllDescendants(ISortMode<TData>.Lexicographical)
                .OfType<Leaf>()
                .FirstOrDefault(l => l.Value == item);
        return leaf != null;
    }

    public IEnumerable<Folder> GetAllFolders()
    {
        return Root.GetAllDescendants(ISortMode<TData>.Lexicographical).OfType<Folder>();
    }

    public IEnumerable<Leaf> GetAllLeaves()
    {
        return Root.GetAllDescendants(ISortMode<TData>.Lexicographical).OfType<Leaf>();
    }

    public bool TryGetPathByID(Guid id, [NotNullWhen(true)] out string? path)
    {
        if (FindLeaf(DataStorage.FirstOrDefault(x => x.GUID == id), out var leaf))
        {
            path = leaf.FullName();
            return true;
        }
        path = default;
        return false;
    }

    private string ConvertToName(TData item)
    {
        PluginLog.Verbose($"Request conversion of {item.GetCustomName()} {item.GUID} to name");
        return $"Unnamed " + item.GUID;
    }

    private string ConvertToIdentifier(TData item)
    {
        PluginLog.Verbose($"Request conversion of {item.GetCustomName()} {item.GUID} to identifier");
        return item.GUID.ToString();
    }

    public void Save()
    {
        try
        {
            using var FileStream = new FileStream(FilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
            using var StreamWriter = new StreamWriter(FileStream);
            SaveToFile(StreamWriter, SaveConverter, true);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Error saving GenericFileSystem:");
            ex.Log();
        }
    }

    private (string, bool) SaveConverter(TData item, string arg2)
    {
        PluginLog.Debug($"Saving {item.GetCustomName()}  {item.GUID}");
        return (item.GUID.ToString(), true);
    }

    public class FileSystemSelector : FileSystemSelector<TData, FileSystemSelector.State>
    {
        string NewName = "";
        string? ClipboardText = null;
        TData? CloneItem = null;
        public override ISortMode<TData> SortMode => ISortMode<TData>.FoldersFirst;

        GenericFileSystem<TData> FS;
        public FileSystemSelector(GenericFileSystem<TData> fs) : base(fs, Svc.KeyState, new(), (e) => e.Log())
        {
            FS = fs;
            AddButton(NewItemButton, 0);
            AddButton(ImportButton, 10);
            AddButton(CopyToClipboardButton, 20);
            AddButton(DeleteButton, 1000);
        }

        public float Width = 0f;
        protected override float CurrentWidth => Width;

        public void Draw(float width)
        {
            this.Width = width;
            this.Draw();
        }

        protected override uint CollapsedFolderColor => ImGuiColors.DalamudViolet.ToUint();
        protected override uint ExpandedFolderColor => CollapsedFolderColor;

        public event Action<Leaf, State, bool> OnAfterDrawLeafName;

        protected override void DrawLeafName(Leaf leaf, in State state, bool selected)
        {
            var flag = selected ? ImGuiTreeNodeFlags.Selected | LeafFlags : LeafFlags;
            flag |= ImGuiTreeNodeFlags.SpanFullWidth;
            using var _ = ImRaii.TreeNode(leaf.Name, flag);
            OnAfterDrawLeafName?.Invoke(leaf, state, selected);
        }

        public delegate void OnBeforeCopyDelegate(TData original, ref TData copy);

        public event OnBeforeCopyDelegate OnBeforeCopy;

        private void CopyToClipboardButton(Vector2 vector)
        {
            if (!ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Copy.ToIconString(), vector, "Copy to clipboard.", Selected == null, true)) return;
            if (Selected != null)
            {
                var copy = Selected.JSONClone();
                copy.GUID = Guid.Empty;
                OnBeforeCopy?.Invoke(Selected, ref copy);
                GenericHelpers.Copy(EzConfig.DefaultSerializationFactory.Serialize(copy, false));
            }
        }

        public delegate void OnImportPopupOpenDelegate(string clipboardText, ref string newName);
        public event OnImportPopupOpenDelegate OnImportPopupOpen;


        private void ImportButton(Vector2 size)
        {
            if (!ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.FileImport.ToIconString(), size, "Try to import an item from your clipboard.", false,
                    true))
                return;

            try
            {
                CloneItem = null;
                ClipboardText = GenericHelpers.Paste();
                OnImportPopupOpen?.Invoke(ClipboardText, ref NewName);

                ImGui.OpenPopup("##NewItem");
            }
            catch
            {
                Notify.Error("Could not import data from clipboard.");
            }
        }

        private void DeleteButton(Vector2 vector)
        {
            try
            {
                DeleteSelectionButton(vector, new DoubleModifier(ModifierHotkey.Control), "item", "items", FS.DoDelete);
            }
            catch (Exception e)
            {
                e.Log();
            }
        }

        private void NewItemButton(Vector2 size)
        {
            if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Plus.ToIconString(), size, "Create new item", false,
                    true))
            {
                ClipboardText = null;
                CloneItem = null;
                ImGui.OpenPopup("##NewItem");
            }
        }

        public delegate void OnBeforeItemCreationDelegate(ref TData item);

        public event OnBeforeItemCreationDelegate OnBeforeItemCreation = null;

        private void DrawNewItemPopup()
        {
            if (!OpenNameField("##NewItem", ref NewName)) return;

            if (NewName == "")
            {
                Notify.Error($"Name can not be empty!");
                return;
            }

            if (ClipboardText != null)
            {
                try
                {
                    var newItem = EzConfig.DefaultSerializationFactory.Deserialize<TData>(ClipboardText);
                    if (newItem != null)
                    {
                        OnBeforeItemCreation(ref newItem);
                        if (newItem != null)
                        {
                            var success = FS.Create(newItem, NewName, out var error);
                            if (!success) Notify.Error($"{error}");
                        }
                    }
                    else
                    {
                        Notify.Error($"Invalid clipboard data");
                    }
                }
                catch (Exception e)
                {
                    e.LogVerbose();
                    Notify.Error($"Error: {e.Message}");
                }
            }
            else if (CloneItem != null)
            {

            }
            else
            {
                try
                {
                    var newItem = new TData();
                    FS.Create(newItem, NewName, out _);
                }
                catch (Exception e)
                {
                    e.LogVerbose();
                    Notify.Error($"This name already exists!");
                }
            }

            NewName = string.Empty;
        }

        private static bool OpenNameField(string popupName, ref string newName)
        {
            using var popup = ImRaii.Popup(popupName);
            if (!popup)
                return false;

            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                ImGui.CloseCurrentPopup();

            ImGui.SetNextItemWidth(300 * ImGuiHelpers.GlobalScale);
            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();
            var enterPressed = ImGui.InputTextWithHint("##newName", "Enter New Name...", ref newName, 512, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            if (ImGui.Button("OK")) enterPressed = true;

            if (!enterPressed)
                return false;

            ImGui.CloseCurrentPopup();
            return true;
        }

        protected override void DrawPopups()
        {
            DrawNewItemPopup();
        }

        public record struct State { }
        protected override bool ApplyFilters(IPath path)
        {
            return FilterValue.Length > 0 && !path.FullName().Contains(FilterValue, StringComparison.OrdinalIgnoreCase);
        }

    }
}
