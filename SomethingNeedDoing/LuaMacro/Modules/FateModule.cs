using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using NLua;
using SomethingNeedDoing.LuaMacro.Wrappers;

namespace SomethingNeedDoing.LuaMacro.Modules;
public unsafe class FateModule : LuaModuleBase
{
    public override string ModuleName => "Fates";

    // FateManager 是 [StaticAddress("48 89 01 48 8B 3D ?? ?? ?? ?? 48 8B 87", 6, isPointer: true)],
    // 也就是 InstancesModule 開頭那份分類裡的 B 類：產生器出來的是 `return *ppInstance;`，
    // 特徵碼解出來的是「存放指標的位址」，FATE 管理器尚未建立時（標題畫面、讀取畫面、剛登入、
    // 換區途中）解參考結果就是 null。原本本檔三個消費點全是裸 `Fm->`。
    // 🔴 對 null 解參考是 AccessViolationException，在 .NET Core 屬於 corrupted-state exception，
    //    try/catch 與 HookSafety.ExecuteSafe 都攔不到，唯一有效的作法是「解參考之前判空」。
    // ⇒ 每個呼叫點各自取一次再判空；這三個都是 [LuaFunction]，巨集會放在等待迴圈裡輪詢，
    //   照本 repo 既有慣例（見 InstancesModule 的失敗語意分界）安靜回預設值、不記 log：
    //   單值的回 null（Lua 端是 nil，與「現在不在 FATE 裡」是同一個結果），清單的回空清單。
    private FateManager* Fm => FateManager.Instance();

    public enum FateRule : byte
    {
        None = 0,
        Normal = 1, // trash fates or boss fates
        Collect = 2, // pick up EventObjects or get them from killing mobs
        Escort = 3, // guide some npc to the finish line
        Defend = 4, // defend objectives like crates from being destroyed
        EventFate = 5, // used for seasonal event fates, like Little Ladies Day, Hatching Tide
        Chase = 6, // that one special fate in The Peaks
        ConcertedWorks = 7, // rebuilding the firmament fates
        Fete = 8, // firmament fates
    }

    [LuaFunction]
    public FateWrapper? CurrentFate
    {
        get
        {
            var fm = Fm;
            if (fm == null) return null;
            // CurrentFate 是欄位 +0x88，沒在打 FATE 時本來就是 null（原本已判，只是判了兩次、
            // 兩次各自重解一遍整條鏈）。這裡改成只取一次。
            var fate = fm->CurrentFate;
            return fate == null ? null : new(fate->FateId);
        }
    }

    [LuaFunction] public FateWrapper? GetFateById(ushort fateID) => new(fateID);

    [LuaFunction]
    public FateWrapper? GetNearestFate()
    {
        var fm = Fm;
        if (fm == null) return null;
        return fm->Fates.Where(f => f.Value is not null)
            .OrderBy(f => Player.DistanceTo(f.Value->Location))
            .Select(f => new FateWrapper(f.Value->FateId))
            .FirstOrDefault();
    }

    [LuaFunction]
    public unsafe List<FateWrapper> GetActiveFates()
    {
        var fm = Fm;
        if (fm == null) return [];
        return [.. fm->Fates.Where(f => f.Value is not null)
            .OrderBy(f => Player.DistanceTo(f.Value->Location))
            .Select(f => new FateWrapper(f.Value->FateId))];
    }
}
