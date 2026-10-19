using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace SomethingNeedDoing;

// Broadcasts a Dalamud IPC event whenever /callback fires a programmatic FireCallback on a game
// addon, so other plugins (e.g. Accountant) can detect these selections without having to install
// their own hook on the shared native AtkUnitBase::FireCallback function themselves - doing so was
// found to corrupt unrelated dialogs' closing behavior (any hook there, regardless of its own
// logic, disrupted cascading FireCallback calls the client makes internally).
internal static class CallbackNotifier
{
    private const string IpcName = "SomethingNeedDoing.CallbackFired";

    private static ICallGateProvider<string, int, bool, object?>? _provider;

    public static void Init()
        => _provider = Svc.PluginInterface.GetIpcProvider<string, int, bool, object?>(IpcName);

    public static void Publish(string addonName, int index, bool updateState)
    {
        try
        {
            _provider?.SendMessage(addonName, index, updateState);
        }
        catch
        {
            // no subscribers listening
        }
    }
}
