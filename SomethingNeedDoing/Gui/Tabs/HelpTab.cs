using ECommons.ImGuiMethods;
using ECommons.LanguageHelpers;

namespace SomethingNeedDoing.Gui.Tabs;
public class HelpTab(HelpLuaTab _luaTab, HelpCliTab _cliTab, HelpCommandsTab _commandsTab)
{
    public void Draw()
    {
        ImGuiEx.EzTabBar("Tabs",
            ("General".Loc(), HelpGeneralTab.DrawTab, null, false),
            ("Commands".Loc(), _commandsTab.DrawTab, null, false),
            ("Lua", _luaTab.DrawTab, null, false),
            ("Cli", _cliTab.DrawTab, null, false),
            ("Clicks".Loc(), HelpClicksTab.DrawTab, null, false),
            ("Keys & Sends".Loc(), HelpKeysTab.DrawTab, null, false));
    }
}
