using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using SomethingNeedDoing.Core.Interfaces;
using SomethingNeedDoing.Gui.Modals;
using SomethingNeedDoing.Managers;
using System.Threading.Tasks;

namespace SomethingNeedDoing.Gui.Tabs;

public class MacroSettingsSection(IMacroScheduler scheduler, DependencyFactory dependencyFactory, VersionHistoryModal versionHistoryModal, MetadataParser metadataParser, GitMacroManager gitManager, IEnumerable<IDisableable> disableablePlugins)
{
    private string _pluginDependency = string.Empty;
    private string _pluginToDisable = string.Empty;
    private string _gitUrl = string.Empty;
    private string _localFilePath = string.Empty;
    private DependencyType _dependencyType = DependencyType.Local;
    private LocalDependencyType _localDependencyType = LocalDependencyType.Macro;
    private readonly List<string> _disableablePluginNames = [.. disableablePlugins.Select(p => p.InternalName)];

    public Action? OnContentUpdated { get; set; } // for refreshing after writing the metadata

    public void Draw(ConfigMacro? selectedMacro)
    {
        using var child = ImRaii.Child("SettingsChild", new(-1, ImGui.GetContentRegionAvail().Y), false);
        if (!child) return;

        if (selectedMacro != null)
        {
            DrawMacroConfig(selectedMacro);
            DrawGeneralInfo(selectedMacro);
            DrawGitInfo(selectedMacro);

            if (selectedMacro.Type is MacroType.Native)
                DrawCraftLoop(selectedMacro);

            DrawTriggers(selectedMacro);
            DrawPluginDependencies(selectedMacro);
            DrawPluginConflicts(selectedMacro);
            DrawDependencies(selectedMacro);
        }
        else
            ImGui.TextColored(ImGuiColors.DalamudGrey, "選擇一個巨集以檢視及編輯其設定");
    }

    private void DrawMacroConfig(ConfigMacro selectedMacro)
    {
        if (selectedMacro.Metadata.Configs.Count == 0) return;
        ImGuiUtils.Section("巨集設定", () =>
        {
            foreach (var kvp in selectedMacro.Metadata.Configs)
            {
                var configName = kvp.Key;
                var configValue = kvp.Value;

                using var _ = ImRaii.PushId(configName);

                ImGui.AlignTextToFramePadding();
                ImGuiEx.Text(ImGuiColors.DalamudOrange, configName);
                ImGui.SameLine();
                ImGuiEx.Text(ImGuiColors.DalamudGrey, configValue.Description);

                ImGui.SameLine(ImGui.GetContentRegionAvail().X - 80);
                using (ImRaii.Disabled(configValue.IsValueDefault()))
                {
                    if (ImGui.Button("重置", new Vector2(70, 0)))
                    {
                        configValue.Value = configValue.DefaultValue;
                        C.Save();
                    }
                }
                ImGuiEx.Tooltip($"重置為預設值：{configValue.DefaultValue}");
                ImGui.Spacing();

                // Value editor based on type
                var valueChanged = false;
                switch (configValue.Type.ToLower())
                {
                    case "int":
                        var intValue = Convert.ToInt32(configValue.Value);
                        var intDefault = Convert.ToInt32(configValue.DefaultValue);
                        var intMin = configValue.MinValue != null ? Convert.ToInt32(configValue.MinValue) : int.MinValue;
                        var intMax = configValue.MaxValue != null ? Convert.ToInt32(configValue.MaxValue) : int.MaxValue;

                        ImGui.SetNextItemWidth(200);
                        if (ImGui.InputInt($"##{configName}Value", ref intValue))
                        {
                            if (intValue < intMin) intValue = intMin;
                            if (intValue > intMax) intValue = intMax;
                            configValue.Value = intValue;
                            valueChanged = true;
                        }

                        if (configValue.MinValue != null || configValue.MaxValue != null)
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(ImGuiColors.DalamudGrey, $"範圍：{intMin} - {intMax}");
                        }
                        break;

                    case "float":
                    case "double":
                        var floatValue = Convert.ToSingle(configValue.Value);
                        var floatDefault = Convert.ToSingle(configValue.DefaultValue);
                        var floatMin = configValue.MinValue != null ? Convert.ToSingle(configValue.MinValue) : float.MinValue;
                        var floatMax = configValue.MaxValue != null ? Convert.ToSingle(configValue.MaxValue) : float.MaxValue;

                        ImGui.SetNextItemWidth(200);
                        if (ImGui.InputFloat($"##{configName}Value", ref floatValue))
                        {
                            if (floatValue < floatMin) floatValue = floatMin;
                            if (floatValue > floatMax) floatValue = floatMax;
                            configValue.Value = floatValue;
                            valueChanged = true;
                        }

                        if (configValue.MinValue != null || configValue.MaxValue != null)
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(ImGuiColors.DalamudGrey, $"範圍：{floatMin:F2} - {floatMax:F2}");
                        }
                        break;

                    case "bool":
                    case "boolean":
                        var boolValue = Convert.ToBoolean(configValue.Value);
                        ImGui.SetNextItemWidth(200);
                        if (ImGui.Checkbox($"##{configName}Value", ref boolValue))
                        {
                            configValue.Value = boolValue;
                            valueChanged = true;
                        }
                        break;

                    case "string":
                    default:
                        var stringValue = configValue.Value.ToString() ?? string.Empty;
                        ImGui.SetNextItemWidth(300);

                        var isValid = true;
                        var validationMessage = string.Empty;
                        if (!string.IsNullOrEmpty(configValue.ValidationPattern))
                        {
                            try
                            {
                                var regex = new System.Text.RegularExpressions.Regex(configValue.ValidationPattern);
                                isValid = regex.IsMatch(stringValue);
                                if (!isValid)
                                    validationMessage = configValue.ValidationMessage ?? "數值不符合格式規則";
                            }
                            catch (Exception ex)
                            {
                                isValid = false;
                                validationMessage = $"無效的驗證規則：{ex.Message}";
                            }
                        }

                        using (ImRaii.PushColor(ImGuiCol.Text, isValid ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed, !string.IsNullOrEmpty(configValue.ValidationPattern)))
                        {
                            if (ImGui.InputText($"##{configName}Value", ref stringValue, 1000))
                            {
                                configValue.Value = stringValue;
                                valueChanged = true;
                            }
                        }

                        if (!string.IsNullOrEmpty(configValue.ValidationPattern))
                        {
                            ImGui.SameLine();
                            ImGuiEx.Icon(isValid ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed, isValid ? FontAwesomeIcon.Check : FontAwesomeIcon.ExclamationTriangle);

                            if (!isValid && !string.IsNullOrEmpty(validationMessage))
                                ImGuiEx.Tooltip(validationMessage);
                            else if (isValid)
                                ImGuiEx.Tooltip("數值符合驗證規則");
                        }
                        break;
                }

                if (valueChanged)
                    C.Save();

                if (configValue.Required)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudRed, "*");
                    ImGuiEx.Tooltip("此設定為必填項目");
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }
        });
    }

    private void DrawGeneralInfo(ConfigMacro selectedMacro)
    {
        ImGuiUtils.Section("一般資訊", () =>
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text("作者：");
            ImGui.SameLine(100);

            var author = selectedMacro.Metadata.Author ?? string.Empty;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.InputText("##Author", ref author, 100))
            {
                selectedMacro.Metadata.Author = author;
                C.Save();
            }

            ImGui.AlignTextToFramePadding();
            ImGui.Text("版本：");
            ImGui.SameLine(100);

            var version = selectedMacro.Metadata.Version ?? string.Empty;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.InputText("##Version", ref version, 50))
            {
                selectedMacro.Metadata.Version = version;
                C.Save();
            }

            ImGui.AlignTextToFramePadding();
            ImGui.Text("描述：");

            var description = selectedMacro.Metadata.Description ?? string.Empty;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.InputTextMultiline("##Description", ref description, 1000, new Vector2(-1, 100)))
            {
                selectedMacro.Metadata.Description = description;
                C.Save();
            }

            if (ImGui.Button("將中繼資料寫入內容"))
            {
                if (metadataParser.WriteMetadata(selectedMacro, OnContentUpdated))
                    FrameworkLogger.Debug($"Wrote metadata to macro {selectedMacro.Name}");
                else
                    FrameworkLogger.Error($"Failed to write metadata to macro {selectedMacro.Name}");
            }
            ImGuiEx.Tooltip("將目前的中繼資料（作者、版本、描述、相依項目、觸發條件）寫入巨集內容。若已存在中繼資料，將會被更新。");

            ImGui.SameLine();

            if (ImGui.Button("從內容讀取中繼資料"))
            {
                selectedMacro.Metadata = metadataParser.ParseMetadata(selectedMacro.Content);
                C.Save();
            }
            ImGuiEx.Tooltip("從巨集內容讀取中繼資料（作者、版本、描述、相依項目、觸發條件）並更新設定。");
        });
    }

    private void DrawGitInfo(ConfigMacro selectedMacro)
    {
        ImGuiUtils.Section("Git 資訊", () =>
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text("GitHub 網址：");
            ImGui.SameLine(100);

            var repoUrl = selectedMacro.GitInfo.RepositoryUrl;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.InputText("##RepoUrl", ref repoUrl, 500))
            {
                selectedMacro.GitInfo.RepositoryUrl = repoUrl;
                C.Save();
            }
            ImGuiEx.Tooltip("輸入 GitHub 網址（例如：https://github.com/owner/repo/blob/branch/path）");

            if (selectedMacro.IsGitMacro)
            {
                ImGui.AlignTextToFramePadding();
                var autoUpdate = selectedMacro.GitInfo.AutoUpdate;
                if (ImGui.Checkbox("自動更新", ref autoUpdate))
                {
                    selectedMacro.GitInfo.AutoUpdate = autoUpdate;
                    C.Save();
                }

                var group = new ImGuiEx.EzButtonGroup();
                group.AddIconWithText(FontAwesomeIcon.Download, "匯入", () =>
                {
                    if (!string.IsNullOrWhiteSpace(repoUrl))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await gitManager.AddGitInfoToMacro(selectedMacro, repoUrl);
                            }
                            catch (Exception ex)
                            {
                                FrameworkLogger.Error(ex, $"Failed to import macro from {repoUrl}");
                            }
                        });
                    }
                });
                group.AddIconWithText(FontAwesomeIcon.History, "版本歷史紀錄", () => versionHistoryModal.Open(selectedMacro));
                group.AddIconWithText(FontAwesomeIcon.Sync, "重置 Git 資訊",
                    () => { selectedMacro.GitInfo = new GitInfo(); C.Save(); }, "清除所有 Git 資訊，並將此巨集還原為標準本機巨集。",
                    new() { ButtonColor = EzColor.Red });
                group.Draw();
            }
        });
    }

    private void DrawCraftLoop(ConfigMacro selectedMacro)
    {
        ImGuiUtils.Section("CraftLoop 設定", () =>
        {
            var craftingLoop = selectedMacro.Metadata.CraftingLoop;
            if (ImGui.Checkbox("啟用製作循環", ref craftingLoop))
            {
                selectedMacro.Metadata.CraftingLoop = craftingLoop;
                C.Save();
            }

            if (craftingLoop)
            {
                ImGui.Indent(20);

                var loopCount = selectedMacro.Metadata.CraftLoopCount;
                ImGui.SetNextItemWidth(100);
                if (ImGui.InputInt("循環次數", ref loopCount))
                {
                    if (loopCount < -1)
                        loopCount = -1;

                    selectedMacro.Metadata.CraftLoopCount = loopCount;
                    C.Save();
                }

                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.DalamudGrey, "（-1 = 無限）");

                ImGui.Unindent(20);
            }
        });
    }

    private void DrawTriggers(ConfigMacro selectedMacro)
    {
        ImGuiUtils.Section("觸發事件", () =>
        {
            var events = new List<TriggerEvent>(selectedMacro.Metadata.TriggerEvents);
            if (ImGuiUtils.EnumCheckboxes(ref events, [TriggerEvent.None]))
                selectedMacro.SetTriggerEvents(scheduler, events);

            // Show addon event configuration only when OnAddonEvent is selected
            if (events.Contains(TriggerEvent.OnAddonEvent))
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.Text("Addon 事件設定");
                ImGui.Spacing();

                var addonConfig = selectedMacro.Metadata.AddonEventConfig ?? new AddonEventConfig();
                var addonName = addonConfig.AddonName;
                var eventType = addonConfig.EventType;

                ImGui.AlignTextToFramePadding();
                ImGui.Text("Addon 名稱：");
                ImGui.SameLine(100);

                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputText("##AddonName", ref addonName, 100))
                {
                    addonConfig.AddonName = addonName;
                    selectedMacro.Metadata.AddonEventConfig = addonConfig;
                    C.Save();
                }

                ImGui.AlignTextToFramePadding();
                ImGui.Text("事件類型：");
                ImGui.SameLine(100);

                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGuiEx.EnumCombo("##EventType", ref eventType))
                {
                    addonConfig.EventType = eventType;
                    selectedMacro.Metadata.AddonEventConfig = addonConfig;
                    C.Save();
                }

                if (ImGui.Button("清除 Addon 事件設定"))
                {
                    selectedMacro.Metadata.AddonEventConfig = null;
                    C.Save();
                }
            }
        });
    }

    private void DrawPluginDependencies(ConfigMacro selectedMacro)
    {
        ImGuiUtils.Section("外掛相依項目", () =>
        {
            var installedPlugins = Svc.PluginInterface.InstalledPlugins
                .Where(p => p.IsLoaded)
                .Select(p => p.InternalName)
                .OrderBy(p => p)
                .ToList();

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGuiEx.Combo("##PluginSelector", ref _pluginDependency, installedPlugins))
            {
                if (!selectedMacro.Metadata.PluginDependecies.Contains(_pluginDependency))
                {
                    var newDeps = selectedMacro.Metadata.PluginDependecies.ToList();
                    newDeps.Add(_pluginDependency);
                    selectedMacro.Metadata.PluginDependecies = [.. newDeps];
                    C.Save();
                }
            }

            ImGui.Spacing();

            if (selectedMacro.Metadata.PluginDependecies.Length == 0)
                ImGui.TextColored(ImGuiColors.DalamudGrey, "尚未設定任何外掛相依項目");
            else
            {
                foreach (var plugin in selectedMacro.Metadata.PluginDependecies)
                {
                    using var __ = ImRaii.PushId(plugin);

                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(plugin);
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - 30);

                    if (ImGuiUtils.IconButton(FontAwesomeIcon.Trash, "移除相依項目"))
                    {
                        var newDeps = selectedMacro.Metadata.PluginDependecies.ToList();
                        newDeps.Remove(plugin);
                        selectedMacro.Metadata.PluginDependecies = [.. newDeps];
                        C.Save();
                    }
                }
            }
        });
    }

    private void DrawPluginConflicts(ConfigMacro selectedMacro)
    {
        ImGuiUtils.Section("外掛衝突", () =>
        {
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGuiEx.Combo("##DisableablePluginSelector", ref _pluginToDisable, _disableablePluginNames))
            {
                if (!selectedMacro.Metadata.PluginsToDisable.Contains(_pluginToDisable))
                {
                    var newDeps = selectedMacro.Metadata.PluginsToDisable.ToList();
                    newDeps.Add(_pluginToDisable);
                    selectedMacro.Metadata.PluginsToDisable = [.. newDeps];
                    C.Save();
                }
            }

            ImGui.Spacing();

            if (selectedMacro.Metadata.PluginsToDisable.Length == 0)
                ImGui.TextColored(ImGuiColors.DalamudGrey, "尚未設定任何需停用的外掛");
            else
            {
                foreach (var plugin in selectedMacro.Metadata.PluginsToDisable)
                {
                    using var __ = ImRaii.PushId(plugin);

                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(plugin);
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - 30);

                    if (ImGuiUtils.IconButton(FontAwesomeIcon.Trash, "移除外掛"))
                    {
                        var newDeps = selectedMacro.Metadata.PluginsToDisable.ToList();
                        newDeps.Remove(plugin);
                        selectedMacro.Metadata.PluginsToDisable = [.. newDeps];
                        C.Save();
                    }
                }
            }
        });
    }

    private void DrawDependencies(ConfigMacro selectedMacro)
    {
        ImGuiUtils.Section("巨集相依項目", () =>
        {
            if (selectedMacro.Metadata.Dependencies.Count == 0)
                ImGui.TextColored(ImGuiColors.DalamudGrey, "尚未設定任何巨集相依項目");
            else
            {
                for (var i = 0; i < selectedMacro.Metadata.Dependencies.Count; i++)
                {
                    var dependency = selectedMacro.Metadata.Dependencies[i];
                    using var __ = ImRaii.PushId(i);

                    var macroId = dependency.Id;
                    var displayName = $"[{macroId[..7]}] {dependency.Name}";

                    var icon = macroId.StartsWith("git://") ? FontAwesomeIcon.CloudDownloadAlt :
                              dependency is LocalMacroDependency ? FontAwesomeIcon.Code :
                              dependency is LocalDependency ? FontAwesomeIcon.FileAlt :
                              FontAwesomeIcon.Globe;

                    ImGuiEx.IconWithText(icon, displayName);

                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - 30);
                    if (ImGuiUtils.IconButton(FontAwesomeIcon.Trash, "移除相依項目"))
                    {
                        selectedMacro.Metadata.Dependencies.RemoveAt(i--);
                        C.Save();
                    }
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("新增相依項目");
            ImGui.Spacing();

            ImGuiEx.EnumRadio(ref _dependencyType, true);

            ImGui.Spacing();

            if (_dependencyType == DependencyType.Local)
            {
                ImGui.Text("本機相依項目類型：");
                ImGui.Spacing();

                ImGuiEx.EnumRadio(ref _localDependencyType, true);
                ImGui.Spacing();

                if (_localDependencyType == LocalDependencyType.Macro)
                {
                    var localMacros = C.Macros.Where(m => m.Id != selectedMacro.Id).OrderBy(m => m.Name).ToList();
                    var selectedMacroId = string.Empty;
                    var macroNames = localMacros.ToDictionary(m => m.Id, m => $"{m.Name} [{m.FolderPath}]");

                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGuiEx.Combo("##LocalMacroSelector", ref selectedMacroId, localMacros.Select(m => m.Id), names: macroNames))
                    {
                        selectedMacro.Metadata.Dependencies.Add(dependencyFactory.CreateDependency(selectedMacroId));
                        C.Save();
                    }
                }
                else
                {
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    ImGui.InputText("##LocalFilePath", ref _localFilePath, 1000);
                    ImGuiEx.Tooltip("輸入本機檔案的完整路徑");

                    if (ImGui.Button("新增檔案相依項目"))
                    {
                        if (!string.IsNullOrWhiteSpace(_localFilePath))
                        {
                            selectedMacro.Metadata.Dependencies.Add(dependencyFactory.CreateDependency(_localFilePath));
                            C.Save();
                            _localFilePath = string.Empty;
                        }
                    }
                }
            }
            else
            {
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                ImGui.InputText("##GitUrl", ref _gitUrl, 1000);
                ImGuiEx.Tooltip("輸入 GitHub 網址（例如：https://github.com/owner/repo 或 https://github.com/owner/repo/blob/branch/path）");

                if (ImGui.Button("新增相依項目"))
                {
                    if (!string.IsNullOrWhiteSpace(_gitUrl))
                    {
                        selectedMacro.Metadata.Dependencies.Add(dependencyFactory.CreateDependency(_gitUrl));
                        C.Save();
                        _gitUrl = string.Empty;
                    }
                }
            }
        });
    }
}
