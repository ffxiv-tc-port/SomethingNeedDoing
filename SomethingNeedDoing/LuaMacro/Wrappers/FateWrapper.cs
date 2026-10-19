using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using SomethingNeedDoing.Core.Interfaces;
using static SomethingNeedDoing.LuaMacro.Modules.FateModule;

namespace SomethingNeedDoing.LuaMacro.Wrappers;
public unsafe class FateWrapper(ushort id) : IWrapper
{
    // 兩個彼此獨立的 null 來源,解參考之前都要判空(對 null 解參考是攔不到的 AccessViolationException):
    //  1. FateManager.Instance() 是 B 類(見 InstancesModule 開頭的分類),FATE 管理器尚未建立時回 null。
    //  2. GetFateById 查不到就回 null:id 是巨集傳進來的參數,打錯、FATE 已結束、還沒開始全都落在這條路上。
    // 取不到時一律安靜回預設值,不寫 log:這些是巨集會放在等待迴圈裡輪詢的存取子。
    // 要分辨「真的是 0」與「這個 FATE 不存在」請用 Exists。
    private FateContext* Fate
    {
        get
        {
            // 管理器或 GetFateById 的位址沒解出時也回 null,與「這個 FATE 不存在」走同一條路。
            var manager = GameStatics.TryFateManager();
            if (manager == null || !GameStatics.Resolved(FateManager.MemberFunctionPointers.GetFateById, "FateManager.GetFateById")) return null;
            return manager->GetFateById(Id);
        }
    }

    [LuaDocs][Changelog("12.22")] public ushort Id => id;
    [LuaDocs] public bool Exists => Fate != null;

    // 兩層連鎖:管理器本身可能是 null,而 CurrentFate 在人不在任何 FATE 裡時也是 null,兩層都要擋。
    [LuaDocs]
    public bool InFate
    {
        get
        {
            var manager = GameStatics.TryFateManager();
            if (manager == null) return false;
            var current = manager->CurrentFate;
            if (current == null) return false;
            return current->FateId == Id;
        }
    }

    // ⚠️ FateState 沒有零值(Preparing=3 / Running=4 / Ending=5 / Ended=7 / Failed=8),
    //    所以 default 落在 0 這個不屬於任何合法狀態的值上 —— 這裡刻意要的就是那個效果:
    //    巨集拿它去比對任何一個真實狀態都會是 false,不會被誤導成「這個 FATE 已經結束了」。
    //    (回 Ended 才是真的危險:那會讓等待迴圈以為結束了而往下走。)
    [LuaDocs] public FateState State { get { var f = Fate; if (f == null) return default; return f->State; } }
    [LuaDocs] public int StartTimeEpoch { get { var f = Fate; if (f == null) return 0; return f->StartTimeEpoch; } }
    [LuaDocs] public float Duration { get { var f = Fate; if (f == null) return 0f; return f->Duration; } }
    // 🔴 同 DynamicEventWrapper：Utf8String.ToString() 不剝 SeString payload，
    //    FATE 名稱帶圖示／連結 payload 時會解出 U+FFFD 與雜字元，
    //    而巨集典型寫法是 if fate.Name == "某某" —— 比不中而且完全不報錯。
    [LuaDocs] public string Name { get { var f = Fate; if (f == null) return string.Empty; return f->Name.GetText(); } }
    [LuaDocs] public float HandInCount { get { var f = Fate; if (f == null) return 0f; return f->HandInCount; } }
    [LuaDocs] public Vector3 Location { get { var f = Fate; if (f == null) return default; return f->Location; } }
    [LuaDocs] public float Progress { get { var f = Fate; if (f == null) return 0f; return f->Progress; } }
    [LuaDocs] public bool IsBonus { get { var f = Fate; if (f == null) return false; return f->IsBonus; } }
    [LuaDocs] public float Radius { get { var f = Fate; if (f == null) return 0f; return f->Radius; } }
    // FateRule 有明確的 None = 0,直接用它當「無」。
    [LuaDocs] public FateRule Rule { get { var f = Fate; if (f == null) return FateRule.None; return (FateRule)f->Rule; } }
    [LuaDocs] public int Level { get { var f = Fate; if (f == null) return 0; return f->Level; } }
    [LuaDocs] public int MaxLevel { get { var f = Fate; if (f == null) return 0; return f->MaxLevel; } }
    [LuaDocs] public ushort FATEChain { get { var f = Fate; if (f == null) return 0; return f->FATEChain; } }
    [LuaDocs] public uint EventItem { get { var f = Fate; if (f == null) return 0u; return f->EventItem; } }
    [LuaDocs][Changelog("12.22")] public uint IconId { get { var f = Fate; if (f == null) return 0u; return f->IconId; } }

    // 🔴 這個不能回 0。Location 取不到時是 (0,0,0),而「到原點的距離」對多數地圖都是一個
    //    看起來很正常的數字;巨集典型寫法是 if fate.DistanceToPlayer < 30 then …,回 0 會讓
    //    它以為自己就站在 FATE 上。回 float.MaxValue 才能保證任何「夠近嗎」的判斷都是 false。
    [LuaDocs] public float DistanceToPlayer { get { var f = Fate; if (f == null) return float.MaxValue; return Player.DistanceTo(f->Location); } }
}
