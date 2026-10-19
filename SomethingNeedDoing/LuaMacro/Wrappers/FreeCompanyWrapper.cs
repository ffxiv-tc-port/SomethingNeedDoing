using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.LuaMacro.Wrappers;
public unsafe class FreeCompanyWrapper : IWrapper
{
    // 🔴 Framework.Instance() -> UIModule -> GetInfoModule() -> GetInfoProxyById(...) 這條鏈每一層都合法會回 null:
    //  - Framework.Instance() 是 B 類(見 InstancesModule 開頭的分類);UIModule 是欄位指標,登入前為 null;
    //  - GetInfoModule() / GetInfoProxyById() 對尚未建立的模組與 proxy 回 null。
    // 這些是巨集會放在等待迴圈裡輪詢的屬性,未登入時解參考 null 就是攔不到的 AccessViolation
    // ⇒ 逐層判空,取不到**安靜回預設值**,不記 log、不擲例外。
    private InfoProxyFreeCompany* FreeCompanyProxy
    {
        get
        {
            // Framework 或 GetInfoProxyById 的位址沒解出(改版後特徵碼對不上)時同樣回 null。
            var framework = GameStatics.TryFramework();
            if (framework is null) return null;

            var uiModule = framework->UIModule;
            if (uiModule is null) return null;

            var infoModule = uiModule->GetInfoModule();
            if (infoModule is null || !GameStatics.Resolved(InfoModule.MemberFunctionPointers.GetInfoProxyById, "InfoModule.GetInfoProxyById")) return null;

            return (InfoProxyFreeCompany*)infoModule->GetInfoProxyById(InfoProxyId.FreeCompany);
        }
    }

    [LuaDocs] public FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany GrandCompany { get { var p = FreeCompanyProxy; return p is null ? default : p->GrandCompany; } }
    [LuaDocs] public byte Rank { get { var p = FreeCompanyProxy; return p is null ? (byte)0 : p->Rank; } }
    [LuaDocs] public int OnlineMemebers { get { var p = FreeCompanyProxy; return p is null ? 0 : p->OnlineMembers; } }
    [LuaDocs] public int TotalMembers { get { var p = FreeCompanyProxy; return p is null ? 0 : p->TotalMembers; } }
    // 🔴 p->Name 不是字串:它是 Span<byte>(FixedSizeArray22<byte>),對它呼叫 ToString() 拿到的是型別描述,不是部隊名。
    // ⚠️ 產生器另外給的 NameString 也不採用:它用 CreateReadOnlySpanFromNullTerminated,沒有長度上限 ——
    //    22 個 byte 剛好塞滿沒有結尾 0 就會一路往後掃過整個結構。
    //    SeString.Parse(ReadOnlySpan<byte>) 在 span 範圍內自己找結尾 0,讀取夾在那 22 個 byte 之內,
    //    再用 GetText() 剝掉 payload,與本 repo 其他讀原生文字的地方一致。
    [LuaDocs] public string Name { get { var p = FreeCompanyProxy; return p is null ? string.Empty : SeString.Parse(p->Name).GetText(); } }
    [LuaDocs] public ulong Id { get { var p = FreeCompanyProxy; return p is null ? 0ul : p->Id; } }
}
