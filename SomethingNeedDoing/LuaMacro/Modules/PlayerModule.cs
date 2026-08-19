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

    private PlayerState* Ps => Instance();

    [LuaFunction] public byte GrandCompany => Ps->GrandCompany;
    [LuaFunction] public byte GCRankMaelstrom { get => Ps->GCRankMaelstrom; set => Ps->GCRankMaelstrom = value; }
    [LuaFunction] public byte GCRankImmortalFlames { get => Ps->GCRankImmortalFlames; set => Ps->GCRankImmortalFlames = value; }
    [LuaFunction] public byte GCRankTwinAdders { get => Ps->GCRankTwinAdders; set => Ps->GCRankTwinAdders = value; }

    [LuaFunction] public uint FishingBait => Ps->FishingBait;

    [LuaFunction] public EntityWrapper Entity => new(Player.Object);
    [LuaFunction] public ushort TerritoryType => Svc.ClientState.TerritoryType;

    /// <summary>房屋所在的房區（Ward），不在房屋範圍內時為 -1。</summary>
    [LuaFunction] public sbyte HousingWard => HousingManager.Instance() != null ? HousingManager.Instance()->GetCurrentWard() : (sbyte)-1;
    /// <summary>房屋所在的地皮（Plot），不在房屋範圍內時為 -1。</summary>
    [LuaFunction] public sbyte HousingPlot => HousingManager.Instance() != null ? HousingManager.Instance()->GetCurrentPlot() : (sbyte)-1;
    /// <summary>室內房間編號（例如公寓樓層/房號），不在室內時為 0。</summary>
    [LuaFunction] public short HousingRoom => HousingManager.Instance() != null ? HousingManager.Instance()->GetCurrentRoom() : (short)0;
    [LuaFunction] public FreeCompanyWrapper FreeCompany => new();

    [LuaFunction] public JobWrapper Job => new(Player.JobId);
    [LuaFunction] public JobWrapper GetJob(uint classJobId) => new(classJobId);
    [LuaFunction][Changelog("12.21")] public GearsetWrapper Gearset { get { var module = RaptureGearsetModule.Instance(); return new(module == null ? GearsetWrapper.NoGearset : module->CurrentGearsetIndex); } }
    [LuaFunction][Changelog("12.21")] public GearsetWrapper GetGearset(int id) => new(id);
    [LuaFunction][Changelog("12.21")] public List<GearsetWrapper> Gearsets { get { var module = RaptureGearsetModule.Instance(); return module == null ? [] : [.. module->Entries.ToArray().Select((g, i) => new GearsetWrapper(i))]; } }
    public class GearsetWrapper(int id) : IWrapper
    {
        /// <summary>取不到裝備組時用的哨兵索引：所有存取都會走 null 分支。</summary>
        internal const int NoGearset = -1;

        /// <summary>
        /// 裝備組的數量上限，取自 <c>RaptureGearsetModule._entries</c> 的 <c>FixedSizeArray100</c>。
        /// Lua 這一側的 id 是巨集作者自己填的，越界時不能把它交給原生函式。
        /// </summary>
        private const int MaxGearsets = 100;

        /// <summary>模組指標不跨呼叫保存：每次存取重取，取不到就回 null 讓上層走保守分支。</summary>
        private static RaptureGearsetModule* Module => RaptureGearsetModule.Instance();

        /// <summary>條目指標同樣每次重取。模組為 null、id 越界、或原生查詢回 null 時一律回 null。</summary>
        private RaptureGearsetModule.GearsetEntry* Entry
        {
            get
            {
                var module = Module;
                return module == null || id is < 0 or >= MaxGearsets ? null : module->GetGearset(id);
            }
        }

        /// <summary>這個包裝物件目前指得到真的裝備組嗎（未登入／索引越界時為 false）。</summary>
        [LuaDocs] public bool Exists => Entry != null;

        [LuaDocs][Changelog("12.21")] public bool IsValid { get { var module = Module; return module != null && id is >= 0 and < MaxGearsets && module->IsValidGearset(id); } }
        [LuaDocs][Changelog("12.21")] public byte ClassJob { get { var entry = Entry; return entry == null ? (byte)0 : entry->ClassJob; } }
        [LuaDocs][Changelog("12.21")] public byte GlamourSetLink { get { var entry = Entry; return entry == null ? (byte)0 : entry->GlamourSetLink; } }
        [LuaDocs][Changelog("12.21")] public short ItemLevel { get { var entry = Entry; return entry == null ? (short)0 : entry->ItemLevel; } }
        [LuaDocs][Changelog("12.21")] public byte BannerIndex { get { var entry = Entry; return entry == null ? (byte)0 : entry->BannerIndex; } }
        [LuaDocs][Changelog("12.21")] public string Name { get { var entry = Entry; return entry == null ? string.Empty : entry->NameString; } }
        [LuaDocs][Changelog("12.21")] public List<InventoryItemWrapper> Items { get { var entry = Entry; return entry == null ? [] : [.. entry->Items.ToArray().Select(i => new InventoryItemWrapper(i.ItemId))]; } }
        [LuaDocs][Changelog("12.21")] public void Equip() { var module = Module; if (module != null && id is >= 0 and < MaxGearsets) module->EquipGearset(id); }
        [LuaDocs][Changelog("12.21")] public void Update() { var module = Module; if (module != null && id is >= 0 and < MaxGearsets) module->UpdateGearset(id); }
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
        private PlayerState* Ps => Instance();

        [LuaDocs] public bool HasWeeklyBingoJournal => Ps->HasWeeklyBingoJournal;
        [LuaDocs] public bool IsWeeklyBingoExpired => Ps->IsWeeklyBingoExpired();
        [LuaDocs] public uint WeeklyBingoNumSecondChancePoints => Ps->WeeklyBingoNumSecondChancePoints;
        [LuaDocs] public int WeeklyBingoNumPlacedStickers => Ps->WeeklyBingoNumPlacedStickers;
        [LuaDocs] public object? GetWeeklyBingoOrderDataRow(int wonderousTailsIndex) => parentModule.GetModule<ExcelModule>()?.GetRow("WeeklyBingoOrderData", Ps->WeeklyBingoOrderData[wonderousTailsIndex]);
        [LuaDocs] public WeeklyBingoTaskStatus GetWeeklyBingoTaskStatus(int wonderousTailsIndex) => Ps->GetWeeklyBingoTaskStatus(wonderousTailsIndex);
    }
}
