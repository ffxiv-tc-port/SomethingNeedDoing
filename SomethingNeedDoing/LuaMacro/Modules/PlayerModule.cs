using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using NLua;
using SomethingNeedDoing.Core.Interfaces;
using SomethingNeedDoing.LuaMacro.Wrappers;
using static FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState;
using static SomethingNeedDoing.LuaMacro.Modules.InventoryModule;

namespace SomethingNeedDoing.LuaMacro.Modules;
public unsafe class PlayerModule : LuaModuleBase
{
    public override string ModuleName => "Player";

    // PlayerState 位址沒解出(改版後特徵碼對不上)時:讀取回型別最大值(0 是合法的「沒有大國防聯軍」/「沒裝餌」),
    // 軍階的寫入記一行錯誤後不寫。
    private static PlayerState* PsForWrite()
    {
        var ps = GameStatics.TryPlayerState();
        if (ps == null) FrameworkLogger.Error("Player state is unavailable; nothing was changed");
        return ps;
    }

    [LuaFunction] public byte GrandCompany { get { var ps = GameStatics.TryPlayerState(); return ps == null ? byte.MaxValue : ps->GrandCompany; } }
    [LuaFunction] public byte GCRankMaelstrom { get { var ps = GameStatics.TryPlayerState(); return ps == null ? byte.MaxValue : ps->GCRankMaelstrom; } set { var ps = PsForWrite(); if (ps != null) ps->GCRankMaelstrom = value; } }
    [LuaFunction] public byte GCRankImmortalFlames { get { var ps = GameStatics.TryPlayerState(); return ps == null ? byte.MaxValue : ps->GCRankImmortalFlames; } set { var ps = PsForWrite(); if (ps != null) ps->GCRankImmortalFlames = value; } }
    [LuaFunction] public byte GCRankTwinAdders { get { var ps = GameStatics.TryPlayerState(); return ps == null ? byte.MaxValue : ps->GCRankTwinAdders; } set { var ps = PsForWrite(); if (ps != null) ps->GCRankTwinAdders = value; } }

    [LuaFunction] public uint FishingBait { get { var ps = GameStatics.TryPlayerState(); return ps == null ? uint.MaxValue : ps->FishingBait; } }

    [LuaFunction] public EntityWrapper Entity => new(Player.Object);
    [LuaFunction] public ushort TerritoryType => Svc.ClientState.TerritoryType;

    /// <summary>房屋所在的房區（Ward），不在房屋範圍內時為 -1。</summary>
    [LuaFunction] public sbyte HousingWard { get { var hm = GameStatics.TryHousingManager(); return hm != null && GameStatics.Resolved(HousingManager.MemberFunctionPointers.GetCurrentWard, "HousingManager.GetCurrentWard") ? hm->GetCurrentWard() : (sbyte)-1; } }
    /// <summary>房屋所在的地皮（Plot），不在房屋範圍內時為 -1。</summary>
    [LuaFunction] public sbyte HousingPlot { get { var hm = GameStatics.TryHousingManager(); return hm != null && GameStatics.Resolved(HousingManager.MemberFunctionPointers.GetCurrentPlot, "HousingManager.GetCurrentPlot") ? hm->GetCurrentPlot() : (sbyte)-1; } }
    /// <summary>室內房間編號（例如公寓樓層/房號），不在室內時為 0。</summary>
    [LuaFunction] public short HousingRoom { get { var hm = GameStatics.TryHousingManager(); return hm != null && GameStatics.Resolved(HousingManager.MemberFunctionPointers.GetCurrentRoom, "HousingManager.GetCurrentRoom") ? hm->GetCurrentRoom() : (short)0; } }
    [LuaFunction] public FreeCompanyWrapper FreeCompany => new();

    [LuaFunction] public JobWrapper Job => new(Player.JobId);
    [LuaFunction] public JobWrapper GetJob(uint classJobId) => new(classJobId);
    [LuaFunction][Changelog("12.21")] public GearsetWrapper Gearset { get { var module = GearsetWrapper.Module; return new(module == null ? GearsetWrapper.NoGearset : module->CurrentGearsetIndex); } }
    [LuaFunction][Changelog("12.21")] public GearsetWrapper GetGearset(int id) => new(id);
    [LuaFunction][Changelog("12.21")] public List<GearsetWrapper> Gearsets { get { var module = GearsetWrapper.Module; return module == null ? [] : [.. module->Entries.ToArray().Select((g, i) => new GearsetWrapper(i))]; } }
    public class GearsetWrapper(int id) : IWrapper
    {
        /// <summary>取不到裝備組時用的哨兵索引：所有存取都會走 null 分支。</summary>
        internal const int NoGearset = -1;

        /// <summary>
        /// 裝備組的數量上限，取自 <c>RaptureGearsetModule._entries</c> 的 <c>FixedSizeArray100</c>。
        /// Lua 這一側的 id 是巨集作者自己填的，越界時不能把它交給原生函式。
        /// </summary>
        private const int MaxGearsets = 100;

        /// <summary>模組指標不跨呼叫保存：每次存取重取，取不到（含位址沒解出）就回 null 讓上層走保守分支。</summary>
        internal static RaptureGearsetModule* Module => GameStatics.UiModuleResolved() ? RaptureGearsetModule.Instance() : null;

        /// <summary>條目指標同樣每次重取。模組為 null、id 越界、函式位址沒解出、或原生查詢回 null 時一律回 null。</summary>
        private RaptureGearsetModule.GearsetEntry* Entry
        {
            get
            {
                var module = Module;
                if (module == null || id is < 0 or >= MaxGearsets) return null;
                return GameStatics.Resolved(RaptureGearsetModule.MemberFunctionPointers.GetGearset, "RaptureGearsetModule.GetGearset") ? module->GetGearset(id) : null;
            }
        }

        /// <summary>這個包裝物件目前指得到真的裝備組嗎（未登入／索引越界時為 false）。</summary>
        [LuaDocs] public bool Exists => Entry != null;

        [LuaDocs][Changelog("12.21")] public bool IsValid { get { var module = Module; return module != null && id is >= 0 and < MaxGearsets && GameStatics.Resolved(RaptureGearsetModule.MemberFunctionPointers.IsValidGearset, "RaptureGearsetModule.IsValidGearset") && module->IsValidGearset(id); } }
        [LuaDocs][Changelog("12.21")] public byte ClassJob { get { var entry = Entry; return entry == null ? (byte)0 : entry->ClassJob; } }
        [LuaDocs][Changelog("12.21")] public byte GlamourSetLink { get { var entry = Entry; return entry == null ? (byte)0 : entry->GlamourSetLink; } }
        [LuaDocs][Changelog("12.21")] public short ItemLevel { get { var entry = Entry; return entry == null ? (short)0 : entry->ItemLevel; } }
        [LuaDocs][Changelog("12.21")] public byte BannerIndex { get { var entry = Entry; return entry == null ? (byte)0 : entry->BannerIndex; } }
        [LuaDocs][Changelog("12.21")] public string Name { get { var entry = Entry; return entry == null ? string.Empty : entry->NameString; } }
        [LuaDocs][Changelog("12.21")] public List<InventoryItemWrapper> Items { get { var entry = Entry; return entry == null ? [] : [.. entry->Items.ToArray().Select(i => new InventoryItemWrapper(i.ItemId))]; } }
        [LuaDocs][Changelog("12.21")] public void Equip() { var module = Module; if (module != null && id is >= 0 and < MaxGearsets && GameStatics.Resolved(RaptureGearsetModule.MemberFunctionPointers.EquipGearset, "RaptureGearsetModule.EquipGearset")) module->EquipGearset(id); }
        [LuaDocs][Changelog("12.21")] public void Update() { var module = Module; if (module != null && id is >= 0 and < MaxGearsets && GameStatics.Resolved(RaptureGearsetModule.MemberFunctionPointers.UpdateGearset, "RaptureGearsetModule.UpdateGearset")) module->UpdateGearset(id); }
    }

    [LuaFunction] public bool IsMoving => Player.IsMoving;
    [LuaFunction] public bool IsInDuty => Player.IsInDuty;
    [LuaFunction] public bool IsOnIsland => Player.IsOnIsland;
    [LuaFunction] public bool CanMount => Player.CanMount;
    [LuaFunction] public bool CanFly => Player.CanFly;
    [LuaFunction] public bool Revivable => Player.Revivable;
    [LuaFunction] public bool Available => Player.Available;
    [LuaFunction][Changelog("12.40")] public bool IsLevelSynced => Player.IsLevelSynced;
    [LuaFunction][Changelog("12.40")] public int SyncedLevel => Player.SyncedLevel;

    [LuaFunction][Changelog("12.8")] public bool IsBusy => Player.IsBusy;
    [LuaFunction][Changelog("12.22")] public List<StatusWrapper> Status => [.. Player.BattleChara->GetStatusManager()->Status.ToArray().Select(s => new StatusWrapper(s))];

    [LuaFunction][Changelog("12.12")] public BingoWrapper Bingo => new(this);
    public class BingoWrapper(PlayerModule parentModule) : IWrapper
    {
        // PlayerState 或函式位址沒解出(改版後特徵碼對不上)時:兩個 bool 回 false;點數與貼紙數回型別最大值
        // (0 是合法的「沒有」);GetWeeklyBingoOrderDataRow 回 nil;GetWeeklyBingoTaskStatus 回 -1(不是任何一個狀態)。
        private static bool FlagsResolved => GameStatics.Resolved(PlayerState.MemberFunctionPointers.GetWeeklyBingoFlagsValue, "PlayerState.GetWeeklyBingoFlagsValue");

        [LuaDocs] public bool HasWeeklyBingoJournal { get { var ps = GameStatics.TryPlayerState(); return ps != null && FlagsResolved && ps->HasWeeklyBingoJournal; } }
        [LuaDocs] public bool IsWeeklyBingoExpired { get { var ps = GameStatics.TryPlayerState(); return ps != null && GameStatics.Resolved(PlayerState.MemberFunctionPointers.IsWeeklyBingoExpired, "PlayerState.IsWeeklyBingoExpired") && ps->IsWeeklyBingoExpired(); } }
        [LuaDocs] public uint WeeklyBingoNumSecondChancePoints { get { var ps = GameStatics.TryPlayerState(); return ps != null && FlagsResolved ? ps->WeeklyBingoNumSecondChancePoints : uint.MaxValue; } }
        [LuaDocs] public int WeeklyBingoNumPlacedStickers { get { var ps = GameStatics.TryPlayerState(); return ps == null ? int.MaxValue : ps->WeeklyBingoNumPlacedStickers; } }

        [LuaDocs]
        public object? GetWeeklyBingoOrderDataRow(int wonderousTailsIndex)
        {
            var ps = GameStatics.TryPlayerState();
            return ps == null ? null : parentModule.GetModule<ExcelModule>()?.GetRow("WeeklyBingoOrderData", ps->WeeklyBingoOrderData[wonderousTailsIndex]);
        }

        [LuaDocs]
        public WeeklyBingoTaskStatus GetWeeklyBingoTaskStatus(int wonderousTailsIndex)
        {
            var ps = GameStatics.TryPlayerState();
            if (ps == null || !GameStatics.Resolved(PlayerState.MemberFunctionPointers.GetWeeklyBingoTaskStatus, "PlayerState.GetWeeklyBingoTaskStatus")) return (WeeklyBingoTaskStatus)(-1);
            return ps->GetWeeklyBingoTaskStatus(wonderousTailsIndex);
        }
    }
}
