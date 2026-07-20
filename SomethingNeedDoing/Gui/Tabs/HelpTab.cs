using ECommons.ImGuiMethods;

namespace SomethingNeedDoing.Gui.Tabs;
public class HelpTab(HelpLuaTab _luaTab, HelpCliTab _cliTab, HelpCommandsTab _commandsTab)
{
    public void Draw()
    {
        ImGuiEx.EzTabBar("Tabs",
            ("一般", HelpGeneralTab.DrawTab, null, false),
            ("指令", _commandsTab.DrawTab, null, false),
            ("Lua", _luaTab.DrawTab, null, false),
            ("Cli", _cliTab.DrawTab, null, false),
            ("點擊", HelpClicksTab.DrawTab, null, false),
            ("按鍵與傳送", HelpKeysTab.DrawTab, null, false));
    }
}
