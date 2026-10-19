using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using NLua;
using SomethingNeedDoing.LuaMacro.Wrappers;

namespace SomethingNeedDoing.LuaMacro.Modules;
public unsafe class FateModule : LuaModuleBase
{
    public override string ModuleName => "Fates";

    // FateManager 是 InstancesModule 開頭那份分類裡的 B 類([StaticAddress(..., isPointer: true)]):FATE 管理器尚未建立時
    // （標題畫面、讀取畫面、剛登入、換區途中）Instance() 回 null ⇒ 每個呼叫點各自取一次再判空。
    // 本檔三個消費點都是巨集會放在等待迴圈裡輪詢的 [LuaFunction],取不到安靜回預設值、不記 log:
    // 單值的回 null（Lua 端是 nil，與「現在不在 FATE 裡」是同一個結果），清單的回空清單。
    // 位址沒解出(改版後特徵碼對不上)時 TryFateManager 同樣回 null,走同一條「取不到」的路。
    private FateManager* Fm => GameStatics.TryFateManager();

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
            // CurrentFate 沒在打 FATE 時本來就是 null；只取一次，不要判空與取值各重解一遍整條鏈。
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
