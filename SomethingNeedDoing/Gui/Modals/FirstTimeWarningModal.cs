using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using ECommons.LanguageHelpers;
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
        if (C.AcknowledgedLegacyWarning) return;

        // AgentLobby.Instance() 是 C 類([Agent] 產生器,實作逐字帶
        // `agentModule == null ? null : ...`)—— 遊戲剛啟動、UIModule 尚未建立時合法回 null,
        // 而這支是每幀跑的繪製路徑,正好會在「還沒登入」的時段被呼叫。
        // 解參考 null 是 AccessViolation,corrupted-state exception,try/catch 攔不到。
        // 取不到代理人 ⇒ 當作「還沒進到區域」,這幀不畫(與 IsLoggedIntoZone 為 false 同義,
        // 下一幀會再試,警告視窗不會因此被永久跳過)。
        var lobby = AgentLobby.Instance();
        if (lobby == null || !lobby->IsLoggedIntoZone) return;

        var isOpen = !C.AcknowledgedLegacyWarning;

        ImGui.OpenPopup($"FirstTimeWarningPopup##{nameof(FirstTimeWarningModal)}");

        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(Size);

        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(15, 15));
        using var popup = ImRaii.PopupModal($"FirstTimeWarningPopup##{nameof(FirstTimeWarningModal)}", ref isOpen, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar);
        if (!popup) return;

        ImGui.TextWrapped("?? has been fully rewritten to support the framework changes from API 12.".Loc(P.Name));
        ImGui.BulletText("Native macros should work much the same as before.".Loc());
        ImGui.BulletText("Lua macros will not work at all. Scripts authors will need to write new scripts.".Loc());
        ImGui.BulletText("There is a legacy macro importer located in the settings menu.".Loc());

        ImGui.Spacing();
        ImGuiEx.TextCentered(ImGuiColors.DalamudGrey, "This message will only be displayed once and will stop showing upon the release of API13.".Loc());

        var group = new ImGuiEx.EzButtonGroup() { IsCentered = true };
        group.Add("Acknowledge and Close".Loc(), Close);
        group.Draw();
    }
}
