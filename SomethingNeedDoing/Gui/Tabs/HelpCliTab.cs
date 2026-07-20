using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using SomethingNeedDoing.Services;

namespace SomethingNeedDoing.Gui.Tabs;
public class HelpCliTab(CommandService cmds)
{
    public void DrawTab()
    {
        using var child = ImRaii.Child(nameof(HelpCliTab));
        ImGuiUtils.Section("命令列介面", () => ImGui.TextWrapped("以下指令可在聊天視窗或巨集文字中使用。"));

        ImGuiUtils.Section("主指令", () => ImGui.TextUnformatted(cmds.MainCommand), contentFont: UiBuilder.MonoFont);

        ImGuiUtils.Section("別名", () => cmds.Aliases.Each(ImGui.TextUnformatted), contentFont: UiBuilder.MonoFont);

        ImGuiUtils.Section("指令", () =>
        {
            using var table = ImRaii.Table("CommandsTable", 2, ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInner);
            if (!table) return;

            ImGui.TableSetupColumn("指令", ImGuiTableColumnFlags.WidthFixed, 180 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("說明", ImGuiTableColumnFlags.WidthStretch);
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
