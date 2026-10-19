using ECommons.EzIpcManager;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.External;

public class AutoHook : IPC
{
    public override string Name => "AutoHook";
    public override string Repo => Repos.TcPort;

    [EzIPC]
    [LuaFunction(
        description: "Enables or disables the AutoHook plugin.",
        parameterDescriptions: ["enable"])]
    public readonly Action<bool> SetPluginState = null!;

    [EzIPC]
    [LuaFunction(
        description: "Enables or disables auto-gig functionality.",
        parameterDescriptions: ["enable"])]
    public readonly Action<bool> SetAutoGigState = null!;

    [EzIPC]
    [LuaFunction(
        description: "Sets the current preset by name.",
        parameterDescriptions: ["name"])]
    public readonly Action<string> SetPreset = null!;

    [EzIPC]
    [LuaFunction(
        description: "Sets the current spearfishing (AutoGig) preset by name.",
        parameterDescriptions: ["name"])]
    public readonly Action<string> SetPresetAutogig = null!;

    [EzIPC]
    [LuaFunction(
        description: "Creates and selects an anonymous preset.",
        parameterDescriptions: ["name"])]
    public readonly Action<string> CreateAndSelectAnonymousPreset = null!;

    [EzIPC]
    [LuaFunction(description: "Deletes the currently selected preset.")]
    public readonly Action DeleteSelectedPreset = null!;

    [EzIPC]
    [LuaFunction(description: "Deletes all anonymous presets.")]
    public readonly Action DeleteAllAnonymousPresets = null!;

    // AutoHook 已把全域的 AutoGig 大小／速度篩選拿掉(改成每個 gig 條目綁一條魚,大小與速度是從那條魚讀出來的
    // 唯讀衍生屬性),對端沒有可以寫入的欄位,所以這兩個 IPC 不存在。這裡保留 Lua 名稱但丟出說得清楚的例外:
    // 巨集照樣會停——刻意不靜默 no-op,否則使用者以為設了大小篩選、實際上會把整池的魚都叉起來。
    private static NotSupportedException Removed(string name) => new(
        $"IPC.AutoHook.{name} no longer exists. AutoHook removed the global AutoGig size/speed filter " +
        "in its 2024 \"AutoGig Rework\" — each gig entry is now bound to a specific fish inside a " +
        "spearfishing preset, and its size/speed are read from that fish. " +
        "Use IPC.AutoHook.SetPresetAutogig(presetName) to switch spearfishing presets instead.");

    [LuaFunction(
        description: "REMOVED upstream. AutoGig has no global size filter; use SetPresetAutogig instead.",
        parameterDescriptions: ["size"])]
    public void SetAutoGigSize(int size) => throw Removed(nameof(SetAutoGigSize));

    [LuaFunction(
        description: "REMOVED upstream. AutoGig has no global speed filter; use SetPresetAutogig instead.",
        parameterDescriptions: ["speed"])]
    public void SetAutoGigSpeed(int speed) => throw Removed(nameof(SetAutoGigSpeed));
}
