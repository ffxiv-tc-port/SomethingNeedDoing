using ECommons.EzIpcManager;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.External;

public class DeliverooIPC : IPC
{
    public override string Name => "Deliveroo";
    // git.carvel.li 已 DNS 死亡（2026-09-05 實測 NXDOMAIN，plugins.carvel.li 同樣死亡），
    // 台服艦隊也沒有這個外掛的移植版，所以沒有任何可用的安裝來源。
    public override string Repo => Repos.Unavailable;

    [EzIPC]
    [LuaFunction(description: "Checks if a turn-in is currently running")]
    public Func<bool> IsTurnInRunning = null!;
}
