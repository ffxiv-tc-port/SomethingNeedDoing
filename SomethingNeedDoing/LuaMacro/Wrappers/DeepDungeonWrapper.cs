using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using SomethingNeedDoing.Core.Interfaces;
using static FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.InstanceContentDeepDungeon;

namespace SomethingNeedDoing.LuaMacro.Wrappers;

/// <summary>
/// The current deep dungeon floor (Palace of the Dead, Heaven-on-High, Eureka Orthos). Values are read live; outside a deep dungeon IsActive is false, numbers are 0 (room index -1), Pomanders and Magicite have all their slots at 0, and Rooms and Party are empty.
/// </summary>
/// <remarks>
/// Contract: every member re-reads the director (no pointer is kept), and an unresolved EventFramework address reads as "not in a deep dungeon".
/// UsePomander / UseMagicite return false and do nothing when the game function was not resolved (logged as an error).
/// </remarks>
public unsafe class DeepDungeonWrapper : IWrapper
{
    // Upstream dereferences EventFramework.Instance() directly; it is null before the event framework exists (e.g. title screen).
    private static InstanceContentDeepDungeon* DD
    {
        get
        {
            var framework = GameStatics.TryEventFramework();
            if (framework == null || !GameStatics.Resolved(EventFramework.MemberFunctionPointers.GetInstanceContentDirector, "EventFramework.GetInstanceContentDirector")) return null;
            return framework->GetInstanceContentDeepDungeon();
        }
    }

    [LuaDocs(description: "Whether you're in a deep dungeon.")][Changelog("15.15")] public bool IsActive => DD != null;
    [LuaDocs(description: "1 = Palace of the Dead, 2 = Heaven-on-High, 3 = Eureka Orthos.")][Changelog("15.15")] public byte DeepDungeonId { get { var dd = DD; return dd != null ? dd->DeepDungeonId : (byte)0; } }
    [LuaDocs][Changelog("15.15")] public byte Floor { get { var dd = DD; return dd != null ? dd->Floor : (byte)0; } }
    [LuaDocs(description: "Which of the floorset's layouts this floor uses (e.g. 1 or 2 for the two Heaven-on-High layouts).")][Changelog("15.15")] public byte LayoutInitializationType { get { var dd = DD; return dd != null ? dd->LayoutInitializationType : (byte)0; } }
    [LuaDocs][Changelog("15.15")] public byte HoardCount { get { var dd = DD; return dd != null ? dd->HoardCount : (byte)0; } }
    [LuaDocs(description: "The 5x5 room grid, index 0-24 (row * 5 + column, row 0 = north). Known from floor load, no map reveal needed.")][Changelog("15.15")] public List<DeepDungeonRoomWrapper> Rooms { get { var dd = DD; return dd != null ? [.. dd->MapData.ToArray().Select((flags, i) => new DeepDungeonRoomWrapper(i, flags))] : []; } }

    [LuaDocs(description: "Room index (0-24) you're in, or -1.")][Changelog("15.15")]
    public int CurrentRoomIndex
    {
        get
        {
            var dd = DD;
            if (dd == null || Svc.Objects.LocalPlayer is not { } player) return -1;
            var playerId = player.EntityId;
            foreach (var member in dd->Party)
                if (member.EntityId == playerId)
                    return member.RoomIndex;
            return -1;
        }
    }

    [LuaDocs][Changelog("15.15")] public List<DeepDungeonPartyMemberWrapper> Party { get { var dd = DD; return dd != null ? [.. dd->Party.ToArray().Where(p => p.EntityId is not 0 and not 0xE0000000).Select(p => new DeepDungeonPartyMemberWrapper(p))] : []; } }

    [LuaDocs(description: "All 16 pomander slots, indexed by slot (Pomanders[4] is slot 4); empty slots (and every slot outside a deep dungeon) have Count 0.")][Changelog("15.15")]
    public List<DeepDungeonPomanderWrapper> Pomanders
    {
        get
        {
            var dd = DD;
            return dd != null
                ? [.. dd->Items.ToArray().Select((item, slot) => new DeepDungeonPomanderWrapper(slot, item))]
                : [.. Enumerable.Range(0, 16).Select(slot => new DeepDungeonPomanderWrapper(slot, default))];
        }
    }

    [LuaDocs(description: "How many of a pomander you have, by its id (see Pomanders: ItemId).")][Changelog("15.15")]
    public int GetPomanderCount(uint itemId)
    {
        var dd = DD;
        if (dd == null) return 0;
        foreach (var item in dd->Items)
            if (item.ItemId == itemId)
                return item.Count;
        return 0;
    }

    [LuaDocs(description: "Magicite (Heaven-on-High) or demiclone (Eureka Orthos) counts, one per slot (0-2); all 0 outside a deep dungeon.")][Changelog("15.15")] public byte[] Magicite { get { var dd = DD; return dd != null ? dd->Magicite.ToArray() : new byte[3]; } }

    [LuaDocs(description: "Uses the pomander in a slot (0-15, see Pomanders). Returns false (and does nothing) outside a deep dungeon, for other slots, or when the game function could not be found after a game update. The game itself also ignores it while you're animation locked.")][Changelog("15.15")]
    public bool UsePomander(uint slot)
    {
        var dd = DD;
        if (dd == null || slot >= 16) return false;
        if (!GameStatics.Resolved(InstanceContentDeepDungeon.MemberFunctionPointers.UsePomander, "InstanceContentDeepDungeon.UsePomander"))
        {
            FrameworkLogger.Error("DeepDungeon.UsePomander is unavailable; nothing was done");
            return false;
        }
        dd->UsePomander(slot);
        return true;
    }

    [LuaDocs(description: "Uses the magicite or demiclone in a slot (0-2). Returns false (and does nothing) outside a deep dungeon, for other slots, or when the game function could not be found after a game update. The game itself also ignores it while you're animation locked.")][Changelog("15.15")]
    public bool UseMagicite(uint slot)
    {
        var dd = DD;
        if (dd == null || slot >= 3) return false;
        if (!GameStatics.Resolved(InstanceContentDeepDungeon.MemberFunctionPointers.UseStone, "InstanceContentDeepDungeon.UseStone"))
        {
            FrameworkLogger.Error("DeepDungeon.UseMagicite is unavailable; nothing was done");
            return false;
        }
        dd->UseStone(slot);
        return true;
    }

    public class DeepDungeonRoomWrapper(int index, RoomFlags flags) : IWrapper
    {
        [LuaDocs][Changelog("15.15")] public int Index => index;
        [LuaDocs(description: "Raw room flags; 0 means no room in this grid cell.")][Changelog("15.15")] public ushort Flags => (ushort)flags;
        [LuaDocs][Changelog("15.15")] public bool Exists => flags != RoomFlags.None;
        [LuaDocs][Changelog("15.15")] public bool ConnectionNorth => flags.HasFlag(RoomFlags.ConnectionN);
        [LuaDocs][Changelog("15.15")] public bool ConnectionSouth => flags.HasFlag(RoomFlags.ConnectionS);
        [LuaDocs][Changelog("15.15")] public bool ConnectionWest => flags.HasFlag(RoomFlags.ConnectionW);
        [LuaDocs][Changelog("15.15")] public bool ConnectionEast => flags.HasFlag(RoomFlags.ConnectionE);
        [LuaDocs][Changelog("15.15")] public bool HasReturn => flags.HasFlag(RoomFlags.Return);
        [LuaDocs][Changelog("15.15")] public bool HasPassage => flags.HasFlag(RoomFlags.Passage);
        [LuaDocs][Changelog("15.15")] public bool IsHome => flags.HasFlag(RoomFlags.Home);
        [LuaDocs][Changelog("15.15")] public bool IsRevealed => flags.HasFlag(RoomFlags.Revealed);
    }

    public class DeepDungeonPartyMemberWrapper(DeepDungeonPartyInfo info) : IWrapper
    {
        [LuaDocs][Changelog("15.15")] public uint EntityId => info.EntityId;
        [LuaDocs][Changelog("15.15")] public int RoomIndex => info.RoomIndex;
    }

    public class DeepDungeonPomanderWrapper(int slot, DeepDungeonItemInfo item) : IWrapper
    {
        [LuaDocs][Changelog("15.15")] public int Slot => slot;
        [LuaDocs(description: "Pomander id. Palace of the Dead and Heaven-on-High share one numbering (Heaven-on-High has more); Eureka Orthos protomanders have their own.")][Changelog("15.15")] public byte ItemId => item.ItemId;
        [LuaDocs][Changelog("15.15")] public byte Count => item.Count;
        [LuaDocs][Changelog("15.15")] public bool IsUsable => item.IsUsable;
        [LuaDocs][Changelog("15.15")] public bool IsActive => item.IsActive;
    }
}
