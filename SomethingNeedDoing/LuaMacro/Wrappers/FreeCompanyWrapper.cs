using Dalamud.Game.Text.SeStringHandling;
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
    // 🔴 p->Name 不是字串。InfoProxyFreeCompany._name 是 FixedSizeArray22<byte> 且標了
    //    isString: true，產生器對它產出兩個成員：Span<byte> Name 與 string NameString。
    //    原本這行是對 **Span<byte>** 呼叫 ToString()，那支回的是 Span 自己的型別描述
    //    （"System.Span<Byte>[22]"）—— 永遠不是部隊名，而且不報錯、不崩潰，
    //    巨集每次拿到的都是同一個常數字串。
    // ⚠️ 產生器另外給的 NameString 也不採用：那支是
    //    Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(...))，
    //    沒有長度上限 —— 22 個 byte 剛好塞滿沒有結尾 0 就會一路往後掃過整個結構。
    //    改走 SeString.Parse(ReadOnlySpan<byte>)：它在 span 範圍內自己找結尾 0
    //    （Dalamud SeString.cs:154-160），讀取夾在那 22 個 byte 之內，
    //    再用 GetText() 剝掉 payload，與本 repo 其他讀原生文字的地方一致。
    [LuaDocs] public string Name { get { var p = FreeCompanyProxy; return p is null ? string.Empty : SeString.Parse(p->Name).GetText(); } }
    [LuaDocs] public ulong Id { get { var p = FreeCompanyProxy; return p is null ? 0ul : p->Id; } }
}
