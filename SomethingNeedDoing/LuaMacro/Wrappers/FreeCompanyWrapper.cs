using FFXIVClientStructs.FFXIV.Client.UI.Info;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.LuaMacro.Wrappers;
public unsafe class FreeCompanyWrapper : IWrapper
{
    // 🔴 原本這條鏈四層全裸:
    //   Framework.Instance() -> UIModule -> GetInfoModule() -> GetInfoProxyById(...)
    // 每一層都合法會回 null,而且成因各不相同:
    //  - Framework.Instance() 是 [StaticAddress(..., isPointer: true)],回的是「存放指標的位址」
    //    解出來的值,遊戲還沒把 Framework 配起來時真的是 null(B 類,必須判)。
    //  - Framework.UIModule 是欄位指標,登入前為 null。
    //  - GetInfoModule() / GetInfoProxyById() 對尚未建立的模組與 proxy 回 null。
    // 這些是 Lua 巨集叫得到的屬性,腳本常常放在等待迴圈裡輪詢 —— 未登入時輪詢一次就是
    // AccessViolation,而 AVE 在 .NET Core 是 corrupted-state exception,try/catch 攔不到。
    // 失敗語意照本 repo 既有慣例(見 InstancesModule 的分類註解):**輪詢型存取子安靜回預設值**
    // (每幀記一行會把整份 log 洗掉),不記 log、不擲例外。
    private InfoProxyFreeCompany* FreeCompanyProxy
    {
        get
        {
            var framework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();
            if (framework is null) return null;

            var uiModule = framework->UIModule;
            if (uiModule is null) return null;

            var infoModule = uiModule->GetInfoModule();
            if (infoModule is null) return null;

            return (InfoProxyFreeCompany*)infoModule->GetInfoProxyById(InfoProxyId.FreeCompany);
        }
    }

    [LuaDocs] public FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany GrandCompany { get { var p = FreeCompanyProxy; return p is null ? default : p->GrandCompany; } }
    [LuaDocs] public byte Rank { get { var p = FreeCompanyProxy; return p is null ? (byte)0 : p->Rank; } }
    [LuaDocs] public int OnlineMemebers { get { var p = FreeCompanyProxy; return p is null ? 0 : p->OnlineMembers; } }
    [LuaDocs] public int TotalMembers { get { var p = FreeCompanyProxy; return p is null ? 0 : p->TotalMembers; } }
    [LuaDocs] public string Name { get { var p = FreeCompanyProxy; return p is null ? string.Empty : p->Name.ToString(); } }
    [LuaDocs] public ulong Id { get { var p = FreeCompanyProxy; return p is null ? 0ul : p->Id; } }
}
