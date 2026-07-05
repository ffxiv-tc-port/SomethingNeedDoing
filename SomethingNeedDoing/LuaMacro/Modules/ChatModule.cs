using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace SomethingNeedDoing.LuaMacro.Modules;

/// <summary>
/// Lua module for reading recent chat/system messages.
/// </summary>
public class ChatModule : LuaModuleBase
{
    private static string _lastMessage = string.Empty;
    private static bool _subscribed;

    public override string ModuleName => "Chat";

    private static void EnsureSubscribed()
    {
        if (_subscribed) return;
        Svc.Chat.ChatMessage += OnChatMessage;
        _subscribed = true;
    }

    private static void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
        => _lastMessage = message.TextValue;

    [LuaFunction(description: "Clears the last captured chat message, so GetLastMessage only returns messages received after this call.")]
    public void ClearLastMessage()
    {
        EnsureSubscribed();
        _lastMessage = string.Empty;
    }

    [LuaFunction(description: "Gets the most recent chat/system message received since the last ClearLastMessage call.")]
    public string GetLastMessage()
    {
        EnsureSubscribed();
        return _lastMessage;
    }
}
