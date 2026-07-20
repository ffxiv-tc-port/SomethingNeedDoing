using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ECommons.UIHelpers.AddonMasterImplementations;
using System.Reflection;

namespace SomethingNeedDoing.Gui.Tabs;
public static class HelpClicksTab
{
    public static void DrawTab()
    {
        using var child = ImRaii.Child(nameof(HelpClicksTab));
        ImGuiUtils.Section("點擊指令", () =>
        {
            ImGui.TextWrapped("點擊指令可用於與遊戲 UI 元素互動，你可以在巨集中使用這些指令。");
            ImGui.TextWrapped("紅色標示的項目是本身帶有方法的屬性（無法直接呼叫）。");
        });

        ImGuiUtils.Section("可用的點擊項目", () =>
        {
            using var _ = ImRaii.Child("ClicksList", new(-1, 300), true);
            foreach (var name in typeof(AddonMaster).Assembly.GetTypes()
            .Where(type => type.FullName!.StartsWith($"{typeof(AddonMaster).FullName}+") && type.DeclaringType == typeof(AddonMaster))
            .SelectMany(type => type.GetMembers()
                .Where(m => (m is MethodInfo info && !info.IsSpecialName && info.DeclaringType != typeof(object)) || (m is PropertyInfo prop && prop.GetAccessors().Length > 0 && prop.PropertyType.IsClass && prop.PropertyType.Namespace == type.Namespace))
                .Select(member => $"{(member is MethodInfo ? "m" : "p")}{type.Name} {member.Name}")))
            {
                var isProperty = name.StartsWith('p');
                var color = isProperty ? ImGuiColors.DalamudRed : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];

                using var textColor = ImRaii.PushColor(ImGuiCol.Text, color);
                if (ImGui.Selectable($"/click {name[1..]}"))
                    Copy($"/click {name[1..]}");
            }
        });
    }
}
