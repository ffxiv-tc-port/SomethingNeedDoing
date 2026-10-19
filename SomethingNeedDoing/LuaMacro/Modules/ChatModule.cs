using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace SomethingNeedDoing.LuaMacro.Modules;

/// <summary>
/// Lua module for reading recent chat/system messages.
/// </summary>
/// <remarks>
/// Keeps a per-channel buffer (last message per raw XivChatType) in addition to the legacy
/// "last message of any type" value, so scripts can read e.g. the last system message without
/// player chat clobbering it.
/// </remarks>
public class ChatModule : LuaModuleBase
{
    /// <summary>Raw value of <see cref="XivChatType.SystemMessage"/>; also the low-7-bit channel of its source-flagged variants (e.g. 2105).</summary>
    private const int SystemMessageChannel = (int)XivChatType.SystemMessage;

    private static string _lastMessage = string.Empty;
    private static long _sequence;
    private static readonly Dictionary<ushort, (long Sequence, string Text)> _lastByType = [];
    private static bool _subscribed;

    public override string ModuleName => "Chat";

    private static void EnsureSubscribed()
    {
        if (_subscribed) return;
        Svc.Chat.ChatMessage += OnChatMessage;
        _subscribed = true;
    }

    private static void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        var text = message.TextValue;
        _lastMessage = text;
        lock (_lastByType)
            _lastByType[(ushort)type] = (++_sequence, text);
    }

    [LuaFunction(description: "Clears all captured chat messages (the any-type last message and the per-channel buffer), so the Get* functions only return messages received after this call.")]
    public void ClearLastMessage()
    {
        EnsureSubscribed();
        _lastMessage = string.Empty;
        lock (_lastByType)
            _lastByType.Clear();
    }

    [LuaFunction(description: "Gets the most recent chat/system message of any type received since the last ClearLastMessage call. Note: any channel (including other players' chat) overwrites this; prefer GetLastSystemMessage/GetLastMessageOfType when reading game status messages.")]
    public string GetLastMessage()
    {
        EnsureSubscribed();
        return _lastMessage;
    }

    [LuaFunction(description: "Gets the most recent message whose XivChatType matches the given value, either exactly or on the low 7 bits (the channel, ignoring source/target flags — e.g. 57 also matches 2105). Returns an empty string if none arrived since the last ClearLastMessage call.")]
    public string GetLastMessageOfType(int chatType)
    {
        EnsureSubscribed();
        var best = (Sequence: 0L, Text: string.Empty);
        lock (_lastByType)
        {
            foreach (var (rawType, entry) in _lastByType)
            {
                if ((rawType == chatType || (rawType & 0x7F) == chatType) && entry.Sequence > best.Sequence)
                    best = entry;
            }
        }
        return best.Text;
    }

    [LuaFunction(description: "Gets the most recent system message (XivChatType channel 57, including source-flagged variants), unaffected by player chat. Returns an empty string if none arrived since the last ClearLastMessage call.")]
    public string GetLastSystemMessage()
        => GetLastMessageOfType(SystemMessageChannel);
}
