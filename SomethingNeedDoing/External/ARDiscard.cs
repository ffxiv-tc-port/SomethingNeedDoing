using ECommons.EzIpcManager;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.External;

public class ARDiscard : IPC
{
    public override string Name => "ARDiscard";
    // git.carvel.li 已 DNS 死亡（2026-09-05 實測 NXDOMAIN，plugins.carvel.li 同樣死亡），
    // 台服艦隊也沒有這個外掛的移植版，所以沒有任何可用的安裝來源。
    public override string Repo => Repos.Unavailable;

    [EzIPC]
    [LuaFunction(description: "Gets a list of item IDs that should be discarded")]
    public readonly Func<IReadOnlySet<uint>> GetItemsToDiscard = null!;
}
