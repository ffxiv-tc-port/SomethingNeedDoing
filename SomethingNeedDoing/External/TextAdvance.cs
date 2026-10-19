using ECommons.EzIpcManager;
using SomethingNeedDoing.Core.Interfaces;
using System.Threading.Tasks;

namespace SomethingNeedDoing.External;
public class TextAdvance : IPC, IDisableable, IDisposable
{
    public override string Name => "TextAdvance";
    public override string Repo => Repos.TcPort;
    public string InternalName => Name;

    [EzIPC]
    [LuaFunction(description: "Stops the plugin")]
    public Action Stop = null!;

    [EzIPC]
    [LuaFunction(description: "Gets whether or not the plugin is busy (actively processing an event)")]
    public Func<bool> IsBusy = null!;

    [EzIPC]
    [LuaFunction(description: "Indicates whether external control is enabled")]
    public Func<bool> IsInExternalControl = null!;

    /// <summary>
    /// Indicates whether user has plugin enabled. Respects territory configuration. If in external control, will return true.
    /// </summary>
    [EzIPC]
    [LuaFunction(description: $"Indicates whether plugin is enabled. If {nameof(IsInExternalControl)}, this will always be true")]
    public Func<bool> IsEnabled = null!;

    [EzIPC]
    [LuaFunction(description: "Indicates whether plugin is paused by other plugin")]
    public Func<bool> IsPaused = null!;

    /// <summary>
    /// All the functions below return currently configured plugin state with respect for territory config and external control. 
    /// However, it does not includes IsEnabled or IsPaused check. A complete check whether TextAdvance is currently ready to process appropriate event will look like: <br></br>
    /// IsEnabled() &amp;&amp; !IsPaused() &amp;&amp; GetEnableQuestAccept()
    /// </summary>
    [EzIPC]
    [LuaFunction(description: "Indicates whether the Quest Accept module is enabled")]
    public Func<bool> GetEnableQuestAccept = null!;

    [EzIPC]
    [LuaFunction(description: "Indicates whether the Quest Complete module is enabled")]
    public Func<bool> GetEnableQuestComplete = null!;

    [EzIPC]
    [LuaFunction(description: "Indicates whether the Reward Pick module is enabled")]
    public Func<bool> GetEnableRewardPick = null!;

    [EzIPC]
    [LuaFunction(description: "Indicates whether the Cutscene Skip module is enabled")]
    public Func<bool> GetEnableCutsceneEsc = null!;

    [EzIPC]
    [LuaFunction(description: "Indicates whether the Cutscene Skip Confirm module is enabled")]
    public Func<bool> GetEnableCutsceneSkipConfirm = null!;

    [EzIPC]
    [LuaFunction(description: "Indicates whether the Request Hand-In module is enabled")]
    public Func<bool> GetEnableRequestHandin = null!;

    [EzIPC]
    [LuaFunction(description: "Indicates whether the Request Fill module is enabled")]
    public Func<bool> GetEnableRequestFill = null!;

    [EzIPC]
    [LuaFunction(description: "Indicates whether the Talk Skip module is enabled")]
    public Func<bool> GetEnableTalkSkip = null!;

    [EzIPC]
    [LuaFunction(description: "Indicates whether the Auto Interact module is enabled")]
    public Func<bool> GetEnableAutoInteract = null!;

    /// <summary>
    /// Enables external control of TextAdvance. 
    /// First argument = your plugin's name. 
    /// Second argument is options. Copy ExternalTerritoryConfig to your plugin. Configure it as you wish: set "null" values to features that you want to keep as configured by user. Set "true" or "false" to forcefully enable or disable feature. 
    /// Returns whether external control successfully enabled or not. When already in external control, it will succeed if called again if plugin name matches with one that already has control and new settings will take effect, otherwise it will fail.
    /// External control completely disables territory-specific settings.
    /// </summary>
    [EzIPC] public Func<string, ExternalTerritoryConfig, bool> EnableExternalControl = null!;

    /// <summary>
    /// Disables external control. Will fail if external control is obtained from other plugin.
    /// </summary>
    [EzIPC] public Func<string, bool> DisableExternalControl = null!;

    public class ExternalTerritoryConfig
    {
        public bool? EnableQuestAccept = null;
        public bool? EnableQuestComplete = null;
        public bool? EnableRewardPick = null;
        public bool? EnableRequestHandin = null;
        public bool? EnableCutsceneEsc = null;
        public bool? EnableCutsceneSkipConfirm = null;
        public bool? EnableTalkSkip = null;
        public bool? EnableRequestFill = null;
        public bool? EnableAutoInteract = null;
    }

    private ExternalTerritoryConfig AllOff => new()
    {
        EnableQuestAccept = false,
        EnableQuestComplete = false,
        EnableRewardPick = false,
        EnableRequestHandin = false,
        EnableCutsceneEsc = false,
        EnableCutsceneSkipConfirm = false,
        EnableTalkSkip = false,
        EnableRequestFill = false,
        EnableAutoInteract = false,
    };

    public Task<bool> EnableAsync()
    {
        try
        {
            return Task.FromResult(DisableExternalControl(P.Name));
        }
        catch
        {
            FrameworkLogger.Error("Failed to enable plugin");
            return Task.FromResult(false);
        }
    }

    public Task<bool> DisableAsync()
    {
        try
        {
            return Task.FromResult(EnableExternalControl(P.Name, AllOff));
        }
        catch
        {
            FrameworkLogger.Error("Failed to disable plugin");
            return Task.FromResult(false);
        }
    }

    /// <summary>卸載時交回外部控制。</summary>
    /// <remarks>
    /// 🔴 提供端的外部控制<b>沒有逾時也不偵測租用者消失</b> ⇒ 不交回的話它的模組會一直被
    /// 我們壓著，使用者只能自己去設定頁按「取消外部控制」。
    /// 📌 以 requester 為鍵，對「持有者不是我們」與「沒人控制」都是 no-op，不必記帳。
    /// </remarks>
    public void Dispose()
    {
        try
        {
            DisableExternalControl(P.Name);
        }
        catch (Exception ex)
        {
            FrameworkLogger.Info($"卸載時交回 TextAdvance 外部控制失敗（{ex.GetType().Name}），多半是它已經先卸載了");
        }

        GC.SuppressFinalize(this);
    }
}
