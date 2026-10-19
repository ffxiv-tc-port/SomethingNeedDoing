using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ECommons.LanguageHelpers;
using ECommons.UIHelpers.AddonMasterImplementations;
using System.Reflection;

namespace SomethingNeedDoing.Gui.Tabs;
public static class HelpClicksTab
{
    // The set of click commands is fixed at assembly-load time (pure reflection over static
    // type metadata), so it's computed once here instead of being rebuilt from scratch every
    // frame the tab is drawn.
    private static readonly List<string> ClickNames = typeof(AddonMaster).Assembly.GetTypes()
        .Where(type => type.FullName!.StartsWith($"{typeof(AddonMaster).FullName}+") && type.DeclaringType == typeof(AddonMaster))
        .SelectMany(type => type.GetMembers()
            .Where(m => (m is MethodInfo info && !info.IsSpecialName && info.DeclaringType != typeof(object)) || (m is PropertyInfo prop && prop.GetAccessors().Length > 0 && prop.PropertyType.IsClass && prop.PropertyType.Namespace == type.Namespace))
            .Select(member => $"{(member is MethodInfo ? "m" : "p")}{type.Name} {member.Name}"))
        .ToList();

    public static void DrawTab()
    {
        using var child = ImRaii.Child(nameof(HelpClicksTab));
        ImGuiUtils.Section("Click Commands".Loc(), () =>
        {
            ImGui.TextWrapped("Click commands can be used to interact with game UI elements. You can use these in your macros.".Loc());
            ImGui.TextWrapped("Items in red are properties that themselves have methods (not callable directly).".Loc());
        });

        ImGuiUtils.Section("Available Clicks".Loc(), () =>
        {
            using var _ = ImRaii.Child("ClicksList", new(-1, 300), true);
            foreach (var name in ClickNames)
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
