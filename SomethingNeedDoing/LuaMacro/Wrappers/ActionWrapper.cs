using FFXIVClientStructs.FFXIV.Client.Game;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.LuaMacro.Wrappers;
public unsafe class ActionWrapper(uint actionId) : IWrapper
{
    // 位址沒解出(改版後特徵碼對不上)時:AdjustedActionId 回 0(不是任何技能);六個時間回 NaN ——
    // 0 是合法的「冷卻好了」,而 NaN 的大小與相等比較全部不成立;GetActionStatus 回 uint.MaxValue(0 是「可以用」)。
    private bool TryAdjustedActionId(out uint adjusted)
    {
        adjusted = 0;
        var am = GameStatics.TryActionManager();
        if (am == null || !GameStatics.Resolved(ActionManager.MemberFunctionPointers.GetAdjustedActionId, "ActionManager.GetAdjustedActionId")) return false;
        adjusted = am->GetAdjustedActionId(actionId);
        return true;
    }

    private static float Elapsed(uint id)
    {
        var am = GameStatics.TryActionManager();
        if (am == null || !GameStatics.Resolved(ActionManager.MemberFunctionPointers.GetRecastTimeElapsed, "ActionManager.GetRecastTimeElapsed")) return float.NaN;
        return am->GetRecastTimeElapsed(ActionType.Action, id);
    }

    private static float Recast(uint id)
    {
        var am = GameStatics.TryActionManager();
        if (am == null || !GameStatics.Resolved(ActionManager.MemberFunctionPointers.GetRecastTime, "ActionManager.GetRecastTime")) return float.NaN;
        return am->GetRecastTime(ActionType.Action, id);
    }

    [LuaDocs] public uint AdjustedActionId => TryAdjustedActionId(out var adjusted) ? adjusted : 0u;
    [LuaDocs] public float RecastTimeElapsed => TryAdjustedActionId(out var adjusted) ? Elapsed(adjusted) : float.NaN;
    [LuaDocs] public float RealRecastTimeElapsed => Elapsed(actionId);
    [LuaDocs] public float RecastTime => TryAdjustedActionId(out var adjusted) ? Recast(adjusted) : float.NaN;
    [LuaDocs] public float RealRecastTime => Recast(actionId);
    [LuaDocs] public float SpellCooldown => Math.Abs(RecastTime - RecastTimeElapsed);
    [LuaDocs] public float RealSpellCooldown => Math.Abs(RealRecastTime - RealRecastTimeElapsed);

    [LuaDocs(description: "Returns LogMessage id")]
    [Changelog("12.69")]
    public uint GetActionStatus()
    {
        var am = GameStatics.TryActionManager();
        if (am == null || !GameStatics.Resolved(ActionManager.MemberFunctionPointers.GetActionStatus, "ActionManager.GetActionStatus")) return uint.MaxValue;
        return am->GetActionStatus(ActionType.Action, actionId);
    }
}
