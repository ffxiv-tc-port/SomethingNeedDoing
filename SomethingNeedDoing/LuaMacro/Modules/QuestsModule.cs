using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace SomethingNeedDoing.LuaMacro.Modules;
public unsafe class QuestsModule : LuaModuleBase
{
    public override string ModuleName => "Quests";

    // 位址沒解出(改版後特徵碼對不上)時回「這個任務不在任務記錄裡」的那組值:三個 bool 回 false、
    // GetQuestSequence 回 0(255 是合法的「最後一步」,不能拿來當沒有資料);GetMonsterNoteRankInfo 回歸零的結構。
    [LuaFunction]
    public bool IsQuestAccepted(ushort id)
    {
        var qm = GameStatics.TryQuestManager();
        return qm != null && GameStatics.Resolved(QuestManager.MemberFunctionPointers.IsQuestAccepted, "QuestManager.IsQuestAccepted") && qm->IsQuestAccepted(id);
    }

    [LuaFunction] public List<uint> GetAcceptedQuests() => [.. Svc.Data.GetExcelSheet<Quest>(Svc.ClientState.ClientLanguage)!.Where(x => IsQuestAccepted((ushort)x.RowId)).Select(x => x.RowId)];
    [LuaFunction] public bool IsQuestComplete(ushort id) => GameStatics.Resolved(QuestManager.MemberFunctionPointers.IsQuestComplete, "QuestManager.IsQuestComplete") && QuestManager.IsQuestComplete(id);
    [LuaFunction] public byte GetQuestSequence(ushort id) => GameStatics.Resolved(QuestManager.MemberFunctionPointers.GetQuestSequence, "QuestManager.GetQuestSequence") ? QuestManager.GetQuestSequence(id) : (byte)0;

    [LuaFunction]
    public bool IsLeveAccepted(ushort id)
    {
        var qm = GameStatics.TryQuestManager();
        return qm != null && qm->LeveQuests.ToArray().Any(q => q.LeveId == id);
    }

    [LuaFunction]
    public MonsterNoteRankInfo GetMonsterNoteRankInfo(int index)
    {
        var manager = GameStatics.TryMonsterNoteManager();
        return manager == null ? default : manager->RankData[index];
    }
}
