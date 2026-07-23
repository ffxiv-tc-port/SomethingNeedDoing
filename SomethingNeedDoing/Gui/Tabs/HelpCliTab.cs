using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.LanguageHelpers;
using SomethingNeedDoing.Services;

namespace SomethingNeedDoing.Gui.Tabs;
public class HelpCliTab(CommandService cmds)
{
    public void DrawTab()
    {
        using var child = ImRaii.Child(nameof(HelpCliTab));
        ImGuiUtils.Section("Command Line Interface".Loc(), () => ImGui.TextWrapped("The following commands can be used in chat or your macro text.".Loc()));

        ImGuiUtils.Section("Main Command".Loc(), () => ImGui.TextUnformatted(cmds.MainCommand), contentFont: UiBuilder.MonoFont);

        ImGuiUtils.Section("Aliases".Loc(), () => cmds.Aliases.Each(ImGui.TextUnformatted), contentFont: UiBuilder.MonoFont);

        ImGuiUtils.Section("Commands".Loc(), () =>
        {
            using var table = ImRaii.Table("CommandsTable", 2, ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInner);
            if (!table) return;

            ImGui.TableSetupColumn("Command".Loc(), ImGuiTableColumnFlags.WidthFixed, 180 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Description".Loc(), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var (name, desc) in cmds.GetCommandData())
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted($"{cmds.Aliases[0]} {name}");

                ImGui.TableSetColumnIndex(1);
                ImGui.TextWrapped(desc);
            }
        }, contentFont: UiBuilder.MonoFont);
    }
}
