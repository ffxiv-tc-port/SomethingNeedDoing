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

    [LuaFunction] public void ExecuteAction(uint actionID, ActionType actionType = ActionType.Action) => ActionManager.Instance()->UseAction(actionType, actionID);
    [LuaFunction] public void ExecuteGeneralAction(uint actionID) => ActionManager.Instance()->UseAction(ActionType.GeneralAction, actionID);
    [LuaFunction]
    public void Teleport(uint aetheryteId)
    {
        // 傳給非傳送點的 id 不會報錯,只會靜默什麼都不做
        if (GetRow<Sheets.Aetheryte>(aetheryteId) is not { IsAetheryte: true })
            throw new InvalidOperationException($"{aetheryteId} is not a valid aetheryte id");
        Telepo.Instance()->Teleport(aetheryteId, 0);
    }
    [LuaFunction] public void CancelCast() => UIState.Instance()->Hotbar.CancelCast();

    [LuaFunction(description: "Returns LogMessage id")]
    [Changelog("12.69")]
    public uint GetActionStatus(ActionType actionType, uint actionId) => ActionManager.Instance()->GetActionStatus(actionType, actionId);

    [LuaFunction] public LimitBreakWrapper LimitBreak => new();
    [LuaFunction] public ActionWrapper GetActionInfo(uint actionId) => new(actionId);
}
