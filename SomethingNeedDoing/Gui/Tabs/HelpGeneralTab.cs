using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using SomethingNeedDoing.Managers;

namespace SomethingNeedDoing.Gui.Tabs;
public static class HelpGeneralTab
{
    public static void DrawTab()
    {
        using var child = ImRaii.Child(nameof(HelpGeneralTab));
        ImGuiUtils.Section(P.Name, () =>
        {
            ImGui.TextWrapped($"{P.Name} 是原生巨集系統的擴充功能，提供智慧輔助工具、額外的指令與修飾詞，以及無限量的巨集。");
            ImGui.TextWrapped("它同時也支援使用 Lua 進行腳本編寫，讓你可以寫出比原生系統所能處理的更複雜的巨集。");
        });

        ImGuiUtils.Section("狀態監控", () =>
        {
            ImGui.TextWrapped("該狀態視窗會顯示目前所有正在執行的巨集及其目前的狀態");
            ImGui.Spacing();

            ImGuiEx.Text(ImGuiColors.DalamudOrange, "巨集狀態");
            ImGui.BulletText("Ready（就緒）：巨集已載入但尚未開始執行");
            ImGui.BulletText("Running（執行中）：巨集正在執行");
            ImGui.BulletText("Paused（已暫停）：巨集執行已被暫時停止");
            ImGui.BulletText("Completed（已完成）：巨集已執行完畢");
            ImGui.BulletText("Failed（失敗）：巨集在執行過程中發生錯誤");
        });

        ImGuiUtils.Section("觸發事件", () =>
        {
            ImGui.TextWrapped("巨集可以設定為根據特定的遊戲事件自動觸發：");
            Enum.GetNames<TriggerEvent>().Each(name => ImGui.BulletText(name));

            ImGui.TextWrapped("Lua 巨集也可以設定讓個別函式自動觸發（前提是該腳本已經在執行中）。");
            ImGui.TextWrapped($"任何以 TriggerEvent 名稱開頭的函式，都會在腳本啟動時註冊到 {nameof(TriggerEventManager)} 中。");
            ImGui.TextWrapped($"特別是對於 {TriggerEvent.OnAddonEvent}，事件名稱後面必須接上 addon 名稱與事件類型，例如");
            ImGui.SameLine();
            ImGuiEx.Text(ImGuiColors.DalamudOrange, "OnAddonEvent_SelectYesno_PostSetup");
        });

        ImGuiUtils.Section("巨集中繼資料", () =>
        {
            ImGuiEx.Text(ImGuiColors.DalamudOrange, "一般");
            ImGui.TextWrapped("巨集可以設定中繼資料，為框架提供關於巨集執行方式的特定設定。");
            ImGui.TextWrapped("在資料庫中選取某個巨集後，可以在「巨集設定」區塊編輯其中繼資料。");
            ImGui.TextWrapped("可以使用上方區塊提供的按鈕，將中繼資料寫入檔案。這對於存放在遠端（例如 github）的巨集來說十分重要，可讓框架在匯入時知道該使用哪些設定");

            ImGuiEx.Text(ImGuiColors.DalamudOrange, "相依性與衝突");
            ImGui.TextWrapped("巨集也可以設定為需要其他插件才能執行，若不符合此需求則會印出訊息。");
            ImGui.TextWrapped("同樣地，巨集也可以設定在執行期間停用其他插件，不過這需要相關插件事先在框架中預先定義以支援此功能。");
            ImGui.TextWrapped("如同插件相依性一樣，巨集也可以相依於其他巨集，不論是本機的（已載入 snd 中）還是遠端的（例如 github）");
        });

        ImGuiUtils.Section("Git 整合", () =>
        {
            ImGui.TextWrapped("巨集現在可以連結至 github 網址，並自動轉換為「Git 巨集」");
            ImGui.TextWrapped("Git 巨集支援在有新版本發布時自動更新（於啟動時檢查），並可透過版本歷史對話框（位於「巨集設定」區塊中）在不同版本間切換");
            ImGui.TextWrapped("當巨集的中繼資料中含有儲存庫網址時，會自動被識別為 Git 巨集。若要將 Git 巨集轉回本機巨集，只需清空網址，或點擊設定中的「重設 Git 資料」按鈕即可");
        });
    }
}
