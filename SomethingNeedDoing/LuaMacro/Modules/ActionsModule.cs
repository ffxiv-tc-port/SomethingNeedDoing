using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using SomethingNeedDoing.LuaMacro.Wrappers;

namespace SomethingNeedDoing.LuaMacro.Modules;
/// <summary>
/// Module for executing actions or info about actions
/// </summary>
public unsafe class ActionsModule : LuaModuleBase
{
    public override string ModuleName => "Actions";

    // 位址沒解出(改版後特徵碼對不上)時:四個動作型記一行錯誤後不執行;
    // GetActionStatus 回 uint.MaxValue —— 0 是「可以用」,回 0 會讓「狀態是 0 就施放」的巨集去按技能。
    [LuaFunction]
    public void ExecuteAction(uint actionID, ActionType actionType = ActionType.Action)
    {
        var am = GameStatics.TryActionManager();
        if (am == null || !GameStatics.Resolved(ActionManager.MemberFunctionPointers.UseAction, "ActionManager.UseAction"))
        {
            FrameworkLogger.Error("ExecuteAction is unavailable; nothing was done");
            return;
        }
        am->UseAction(actionType, actionID);
    }

    [LuaFunction]
    public void ExecuteGeneralAction(uint actionID)
    {
        var am = GameStatics.TryActionManager();
        if (am == null || !GameStatics.Resolved(ActionManager.MemberFunctionPointers.UseAction, "ActionManager.UseAction"))
        {
            FrameworkLogger.Error("ExecuteGeneralAction is unavailable; nothing was done");
            return;
        }
        am->UseAction(ActionType.GeneralAction, actionID);
    }

    [LuaFunction]
    public void Teleport(uint aetheryteId)
    {
        // 傳給非傳送點的 id 不會報錯,只會靜默什麼都不做
        if (GetRow<Sheets.Aetheryte>(aetheryteId) is not { IsAetheryte: true })
            throw new InvalidOperationException($"{aetheryteId} is not a valid aetheryte id");
        var telepo = GameStatics.TryTelepo();
        if (telepo == null || !GameStatics.Resolved(Telepo.MemberFunctionPointers.Teleport, "Telepo.Teleport"))
        {
            FrameworkLogger.Error("Teleport is unavailable; nothing was done");
            return;
        }
        telepo->Teleport(aetheryteId, 0);
    }

    [LuaFunction]
    public void CancelCast()
    {
        var ui = GameStatics.TryUIState();
        if (ui == null || !GameStatics.Resolved(Hotbar.MemberFunctionPointers.CancelCast, "Hotbar.CancelCast"))
        {
            FrameworkLogger.Error("CancelCast is unavailable; nothing was done");
            return;
        }
        ui->Hotbar.CancelCast();
    }

    [LuaFunction(description: "Returns LogMessage id")]
    [Changelog("12.69")]
    public uint GetActionStatus(ActionType actionType, uint actionId)
    {
        var am = GameStatics.TryActionManager();
        if (am == null || !GameStatics.Resolved(ActionManager.MemberFunctionPointers.GetActionStatus, "ActionManager.GetActionStatus")) return uint.MaxValue;
        return am->GetActionStatus(actionType, actionId);
    }

    [LuaFunction] public LimitBreakWrapper LimitBreak => new();
    [LuaFunction] public ActionWrapper GetActionInfo(uint actionId) => new(actionId);
}
