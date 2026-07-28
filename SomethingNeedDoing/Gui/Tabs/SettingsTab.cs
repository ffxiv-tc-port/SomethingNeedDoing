using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using ECommons.LanguageHelpers;
using SomethingNeedDoing.Gui.Modals;

namespace SomethingNeedDoing.Gui.Tabs;
public static class SettingsTab
{
    public static void DrawTab()
    {
        using var _ = ImRaii.Child("SettingsTab", Vector2.Create(-1), false);

        ImGuiUtils.Section("General Settings".Loc(), () =>
        {
            var chatChannel = C.ChatType;
            // "###ChatType" keeps the ImGui id (and ECommons' EnumComboSearch dictionary
            // key) language-independent; only the visible label is translated.
            if (ImGuiEx.EnumCombo("ChatType".Loc() + "###ChatType", ref chatChannel))
            {
                C.ChatType = chatChannel;
                C.Save();
            }

            var errorChannel = C.ErrorChatType;
            if (ImGuiEx.EnumCombo("ErrorChatType".Loc() + "###ErrorChatType", ref errorChannel))
            {
                C.ErrorChatType = errorChannel;
                C.Save();
            }

            var propagatePause = C.PropagateControlsToChildren;
            if (ImGui.Checkbox("Propagate Controls to Child Macros".Loc(), ref propagatePause))
            {
                C.PropagateControlsToChildren = propagatePause;
                C.Save();
            }
            ImGuiEx.Tooltip("When enabled, pausing, resuming and stopping macros will also pause, resume and stop the child macros.".Loc());
        });

        ImGuiUtils.Section("Crafting Settings".Loc(), () =>
        {
            var craftSkip = C.CraftSkip;
            if (ImGui.Checkbox("Skip craft actions when not crafting".Loc(), ref craftSkip))
            {
                C.CraftSkip = craftSkip;
                C.Save();
            }

            var smartWait = C.SmartWait;
            if (ImGui.Checkbox("Smart wait for crafting actions".Loc(), ref smartWait))
            {
                C.SmartWait = smartWait;
                C.Save();
            }

            var qualitySkip = C.QualitySkip;
            if (ImGui.Checkbox("Skip quality increasing actions when at 100% HQ chance".Loc(), ref qualitySkip))
            {
                C.QualitySkip = qualitySkip;
                C.Save();
            }

            var loopTotal = C.LoopTotal;
            if (ImGui.Checkbox("Count /loop number as total iterations".Loc(), ref loopTotal))
            {
                C.LoopTotal = loopTotal;
                C.Save();
            }

            var loopEcho = C.LoopEcho;
            if (ImGui.Checkbox("Always echo /loop commands".Loc(), ref loopEcho))
            {
                C.LoopEcho = loopEcho;
                C.Save();
            }

            var useCraftLoopTemplate = C.UseCraftLoopTemplate;
            if (ImGui.Checkbox("Use CraftLoop template".Loc(), ref useCraftLoopTemplate))
            {
                C.UseCraftLoopTemplate = useCraftLoopTemplate;
                C.Save();
            }

            if (useCraftLoopTemplate)
            {
                var craftLoopTemplate = C.CraftLoopTemplate;
                if (ImGui.InputTextMultiline("CraftLoop Template".Loc(), ref craftLoopTemplate, 1000, new Vector2(0, 100)))
                {
                    C.CraftLoopTemplate = craftLoopTemplate;
                    C.Save();
                }

                var craftLoopFromRecipeNote = C.CraftLoopFromRecipeNote;
                if (ImGui.Checkbox("Start crafting loops from recipe note window".Loc(), ref craftLoopFromRecipeNote))
                {
                    C.CraftLoopFromRecipeNote = craftLoopFromRecipeNote;
                    C.Save();
                }

                var craftLoopMaxWait = C.CraftLoopMaxWait;
                if (ImGui.SliderInt("CraftLoop maxwait value".Loc(), ref craftLoopMaxWait, 1, 10))
                    C.CraftLoopMaxWait = craftLoopMaxWait;
                if (ImGui.IsItemDeactivatedAfterEdit())
                    C.Save();

                var craftLoopEcho = C.CraftLoopEcho;
                if (ImGui.Checkbox("CraftLoop echo".Loc(), ref craftLoopEcho))
                {
                    C.CraftLoopEcho = craftLoopEcho;
                    C.Save();
                }
            }
        });

        ImGuiUtils.Section("Error Handling".Loc(), () =>
        {
            var stopOnError = C.StopOnError;
            if (ImGui.Checkbox("Stop on error".Loc(), ref stopOnError))
            {
                C.StopOnError = stopOnError;
                C.Save();
            }
            ImGuiEx.Tooltip("Only meant for native macros.".Loc());

            var maxTimeoutRetries = C.MaxTimeoutRetries;
            if (ImGui.SliderInt("Max Timeout Retries".Loc(), ref maxTimeoutRetries, 0, 10))
                C.MaxTimeoutRetries = maxTimeoutRetries;
            if (ImGui.IsItemDeactivatedAfterEdit())
                C.Save();

            var noisyErrors = C.NoisyErrors;
            if (ImGui.Checkbox("Noisy Errors".Loc(), ref noisyErrors))
            {
                C.NoisyErrors = noisyErrors;
                C.Save();
            }

            if (noisyErrors)
            {
                var beepFrequency = C.BeepFrequency;
                if (ImGui.SliderInt("Beep Frequency".Loc(), ref beepFrequency, 0, 1000))
                    C.BeepFrequency = beepFrequency;
                if (ImGui.IsItemDeactivatedAfterEdit())
                    C.Save();

                var beepDuration = C.BeepDuration;
                if (ImGui.SliderInt("Beep Duration".Loc(), ref beepDuration, 0, 1000))
                    C.BeepDuration = beepDuration;
                if (ImGui.IsItemDeactivatedAfterEdit())
                    C.Save();

                var beepCount = C.BeepCount;
                if (ImGui.SliderInt("Beep Count".Loc(), ref beepCount, 0, 10))
                    C.BeepCount = beepCount;
                if (ImGui.IsItemDeactivatedAfterEdit())
                    C.Save();
            }
        });

        ImGuiUtils.Section("Lua Options".Loc(), () =>
        {
            ImGui.TextWrapped("Lua require paths (where to look for Lua modules):".Loc());

            var paths = C.LuaRequirePaths.ToArray();
            using (ImRaii.Table("LuaRequirePaths", 2, ImGuiTableFlags.SizingStretchProp))
            {
                for (var index = 0; index < paths.Length; index++)
                {
                    var path = PathHelper.NormalizePath(paths[index]);

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();

                    var isValid = PathHelper.ValidatePath(path);
                    ImGui.TextColored(isValid ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed, "Path #??".Loc(index));
                    ImGuiEx.Tooltip(isValid ? "This path is valid.".Loc() : "This path is invalid.".Loc());

                    ImGui.TableNextColumn();

                    if (ImGui.InputText($"##Path{index}", ref path, 200))
                    {
                        var newPaths = paths.ToList();
                        newPaths[index] = PathHelper.NormalizePath(path);
                        C.LuaRequirePaths = [.. newPaths.Where(p => !string.IsNullOrWhiteSpace(p))];
                        C.Save();
                    }
                }
            }

            if (ImGui.Button("Add Path".Loc()))
            {
                var newPaths = paths.ToList();
                newPaths.Add(string.Empty);
                C.LuaRequirePaths = [.. newPaths];
                C.Save();
            }
        });

        ImGuiUtils.Section("Legacy Macro Import".Loc(), () =>
        {
            ImGui.TextWrapped("Import macros from the old version of ??. These are not guaranteed to work any more but can be imported as a reference.\nYou can copy an old config to clipboard and click the import button, or it will automatically attempt to find the old config file.".Loc(P.Name));
            ImGui.Spacing();

            if (ImGuiUtils.IconButton(FontAwesomeHelper.IconImport, "Import".Loc()))
                MigrationModal.Open(ImGui.GetClipboardText());
        });
    }
}
