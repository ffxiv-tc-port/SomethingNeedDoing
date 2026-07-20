using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace SomethingNeedDoing.Gui.Modals;
public static class FirstTimeWarningModal
{
    private static Vector2 Size = new(600, 600);

    public static void Close()
    {
        C.AcknowledgedLegacyWarning = true;
        ImGui.CloseCurrentPopup();
    }

    public static unsafe void DrawModal()
    {
        if (C.AcknowledgedLegacyWarning || !AgentLobby.Instance()->IsLoggedIntoZone) return;
        var isOpen = !C.AcknowledgedLegacyWarning;

        ImGui.OpenPopup($"FirstTimeWarningPopup##{nameof(FirstTimeWarningModal)}");

        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(Size);

        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(15, 15));
        using var popup = ImRaii.PopupModal($"FirstTimeWarningPopup##{nameof(FirstTimeWarningModal)}", ref isOpen, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar);
        if (!popup) return;

        ImGui.TextWrapped($"{P.Name} 已為配合 API 12 的框架變更而完全重寫。");
        ImGui.BulletText("原生 (Native) 巨集運作方式應與之前大致相同。");
        ImGui.BulletText("Lua 巨集將完全無法運作。腳本作者需要重新編寫新腳本。");
        ImGui.BulletText("設定選單中提供了舊版巨集匯入工具。");

        ImGui.Spacing();
        ImGuiEx.TextCentered(ImGuiColors.DalamudGrey, $"此訊息僅會顯示一次，並將於 API13 發布後停止顯示。");

        var group = new ImGuiEx.EzButtonGroup() { IsCentered = true };
        group.Add("確認並關閉", Close);
        group.Draw();
    }
}
