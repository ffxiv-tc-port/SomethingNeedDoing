using FFXIVClientStructs.FFXIV.Client.Game.UI;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.LuaMacro.Wrappers;

// UIState 位址沒解出(改版後特徵碼對不上)時,本檔成員一律回「沒有資料」值、不擲例外:
// 清單回空、字串回空字串、bool 回 false;數值回該型別的最大值(byte 255、uint 4294967295),
// 不與任何合法值相等。TimeLeft 回 NaN:0 是合法的「沒有召喚」,而 NaN 的大小比較全部不成立。
public unsafe class BuddyWrapper : IWrapper
{
    [LuaDocs]
    [Changelog("12.22")]
    public List<BuddyMemberWrapper> BuddyMember
    {
        get
        {
            var ui = GameStatics.TryUIState();
            if (ui == null) return [];
            return [.. ui->Buddy.BattleBuddies.ToArray().Select(b => new BuddyMemberWrapper(b))];
        }
    }

    [LuaDocs][Changelog("12.22")] public CompanionInfoWrapper CompanionInfo => new();
    [LuaDocs][Changelog("12.22")] public PetInfoWrapper PetInfo => new();
}

public class BuddyMemberWrapper(Buddy.BuddyMember buddy) : IWrapper
{
    [LuaDocs][Changelog("12.22")] public uint EntityId => buddy.EntityId;
    [LuaDocs][Changelog("12.22")] public uint CurrentHealth => buddy.CurrentHealth;
    [LuaDocs][Changelog("12.22")] public uint MaxHealth => buddy.MaxHealth;
    [LuaDocs][Changelog("12.22")] public byte DataId => buddy.DataId;
    [LuaDocs][Changelog("12.22")] public byte Synced => buddy.Synced;
    [LuaDocs][Changelog("12.22")] public List<StatusWrapper> Status => [.. buddy.StatusManager.Status.ToArray().Select(s => new StatusWrapper(s))];
}

public unsafe class CompanionInfoWrapper : IWrapper
{
    // 契約:取不到回 null。指到的是 UIState 靜態區塊裡的內嵌欄位,只在當次呼叫內使用。
    private static CompanionInfo* Info()
    {
        var ui = GameStatics.TryUIState();
        return ui == null ? null : &ui->Buddy.CompanionInfo;
    }

    [LuaDocs][Changelog("12.22")] public float TimeLeft { get { var c = Info(); return c == null ? float.NaN : c->TimeLeft; } }
    [LuaDocs][Changelog("12.22")] public byte BardingHead { get { var c = Info(); return c == null ? byte.MaxValue : c->BardingHead; } }
    [LuaDocs][Changelog("12.22")] public byte BardingChest { get { var c = Info(); return c == null ? byte.MaxValue : c->BardingChest; } }
    [LuaDocs][Changelog("12.22")] public byte BardingFeet { get { var c = Info(); return c == null ? byte.MaxValue : c->BardingFeet; } }
    [LuaDocs][Changelog("12.22")] public uint CurrentXP { get { var c = Info(); return c == null ? uint.MaxValue : c->CurrentXP; } }
    [LuaDocs][Changelog("12.22")] public byte Rank { get { var c = Info(); return c == null ? byte.MaxValue : c->Rank; } }
    [LuaDocs][Changelog("12.22")] public byte Stars { get { var c = Info(); return c == null ? byte.MaxValue : c->Stars; } }
    [LuaDocs][Changelog("12.22")] public byte SkillPoints { get { var c = Info(); return c == null ? byte.MaxValue : c->SkillPoints; } }
    [LuaDocs][Changelog("12.22")] public byte DefenderLevel { get { var c = Info(); return c == null ? byte.MaxValue : c->DefenderLevel; } }
    [LuaDocs][Changelog("12.22")] public byte AttackerLevel { get { var c = Info(); return c == null ? byte.MaxValue : c->AttackerLevel; } }
    [LuaDocs][Changelog("12.22")] public byte HealerLevel { get { var c = Info(); return c == null ? byte.MaxValue : c->HealerLevel; } }
    [LuaDocs][Changelog("12.22")] public byte ActiveCommand { get { var c = Info(); return c == null ? byte.MaxValue : c->ActiveCommand; } }
    [LuaDocs][Changelog("12.22")] public byte FavoriteFeed { get { var c = Info(); return c == null ? byte.MaxValue : c->FavoriteFeed; } }
    [LuaDocs][Changelog("12.22")] public byte CurrentColorStainId { get { var c = Info(); return c == null ? byte.MaxValue : c->CurrentColorStainId; } }
    [LuaDocs][Changelog("12.22")] public bool Mounted { get { var c = Info(); return c != null && c->Mounted; } }
    [LuaDocs][Changelog("12.22")] public string Name { get { var c = Info(); return c == null ? string.Empty : c->NameString; } }

    [LuaDocs]
    [Changelog("12.22")]
    public bool IsBuddyEquipUnlocked(uint buddyEquipId)
    {
        var c = Info();
        if (c == null || !GameStatics.Resolved(FFXIVClientStructs.FFXIV.Client.Game.UI.CompanionInfo.MemberFunctionPointers.IsBuddyEquipUnlocked, "CompanionInfo.IsBuddyEquipUnlocked")) return false;
        return c->IsBuddyEquipUnlocked(buddyEquipId);
    }
}

public unsafe class PetInfoWrapper : IWrapper
{
    private static PetInfo* Info()
    {
        var ui = GameStatics.TryUIState();
        return ui == null ? null : &ui->Buddy.PetInfo;
    }

    [LuaDocs][Changelog("12.22")] public byte Order { get { var p = Info(); return p == null ? byte.MaxValue : p->Order; } }
    [LuaDocs][Changelog("12.22")] public byte Stance { get { var p = Info(); return p == null ? byte.MaxValue : p->Stance; } }
}
