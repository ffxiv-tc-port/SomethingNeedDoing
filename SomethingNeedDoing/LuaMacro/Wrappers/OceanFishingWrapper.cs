using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using SomethingNeedDoing.Core.Interfaces;
using static FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.InstanceContentOceanFishing;

namespace SomethingNeedDoing.LuaMacro.Wrappers;
public unsafe class OceanFishingWrapper : IWrapper
{
    // 本包裝層的每個屬性都掛 [LuaDocs],代表任何巨集在任何時間點都叫得到——包含人根本不在
    // 海釣副本裡的時候。FFXIVClientStructs 的 EventFramework.GetInstanceContentDirector<T>()
    // 在「沒有 director」與「director 不是海釣」兩種情況下都明確回 null,所以原本 12 處
    // ...GetInstanceContentOceanFishing()->欄位 全部是直接對 null 解參考。
    //
    // 🔴 對 null 解參考是 AccessViolationException,在 .NET Core 屬於 corrupted-state
    //    exception,try/catch 與 ECommons 的 HookSafety.ExecuteSafe 都攔不到。
    //    唯一有效的作法是「解參考之前判空」,也就是下面這個入口——不是把它包進 try/catch。
    //
    // 因此所有屬性一律先走 GetDirectorOrNull(),拿不到就回安全預設值(數值 0、bool false)。
    // 人在海釣副本裡的時候 director 必定非 null,取值路徑與改動前逐位元相同。
    private static InstanceContentOceanFishing* GetDirectorOrNull()
    {
        // EventFramework 是 [StaticAddress(..., isPointer: true)],指標本身也可能是 null
        // (遊戲自己的取用點就帶 test rax,rax / jz),所以兩半都要擋。
        var framework = EventFramework.Instance();
        return framework == null ? null : framework->GetInstanceContentOceanFishing();
    }

    // AgentIKDResult.Data 是 0x28 上的指標,結算資料還沒產生前是 null,
    // 原本的 ->Data->Score 一樣是無防護解參考。
    private static AgentIKDResult.ResultData* GetResultDataOrNull()
    {
        var module = AgentModule.Instance();
        if (module == null) return null;
        var agent = module->GetAgentIKDResult();
        return agent == null ? null : agent->Data;
    }

    // Mission*Type 在不在海釣時已經回 0,但 IKDPlayerMissionCondition 第 0 列是存在的,
    // 直接拿 0 去查會回一個「看起來合法」的目標值。所以這裡再確認一次 director,
    // 不在海釣就回 0,不要給出貌似有效的數字。
    private static byte MissionGoal(uint missionType)
        => GetDirectorOrNull() == null ? (byte)0 : GetRow<IKDPlayerMissionCondition>(missionType)?.Unknown1 ?? 0;

    [LuaDocs] public uint CurrentRoute { get { var d = GetDirectorOrNull(); return d == null ? 0u : d->CurrentRoute; } }

    [LuaDocs]
    public byte TimeOfDay
    {
        get
        {
            if (GetDirectorOrNull() == null) return 0;
            // Lumina 的 ExcelSheet.GetRow 查不到列時會擲例外,安全版本是 GetRowOrDefault。
            // Time 的索引用 CurrentZone(CS 註解標明是 0/1/2),取值前仍驗上下界;
            // RowRef 也改用 ValueNullable,指不到的參照回 null 而不是擲例外。
            var route = Svc.Data.GetExcelSheet<IKDRoute>()?.GetRowOrDefault(CurrentRoute);
            if (route == null) return 0;
            var times = route.Value.Time;
            var zone = CurrentZone;
            if (zone < 0 || zone >= times.Count) return 0;
            return times[zone].ValueNullable?.Unknown0 ?? 0;
        }
    }

    [LuaDocs] public OceanFishingStatus Status { get { var d = GetDirectorOrNull(); return d == null ? OceanFishingStatus.WaitingForPlayers : d->Status; } }
    [LuaDocs][Changelog("12.54", ChangelogType.Changed, "Changed name")] public int CurrentZone { get { var d = GetDirectorOrNull(); return d == null ? 0 : (int)d->CurrentZone; } }

    [LuaDocs]
    public float TimeLeft
    {
        get
        {
            // 原本讀的是 GetInstanceContentDirector()(不分內容類型)。這裡先確認人真的在海釣裡,
            // 因為要減掉的 TimeOffset 只在海釣 director 上有意義——在別的副本裡拿那個副本的
            // 剩餘時間再減 0,回報的是誤導性的數字。
            // 在海釣內時兩者是同一個物件(泛型版就是驗過 InstanceContentType 之後的轉型),
            // 所以副本內的數值逐位元不變。
            var framework = EventFramework.Instance();
            if (framework == null || framework->GetInstanceContentOceanFishing() == null) return 0f;
            var director = framework->GetInstanceContentDirector();
            return director == null ? 0f : director->ContentDirector.ContentTimeLeft - TimeOffset;
        }
    }

    [LuaDocs] public uint TimeOffset { get { var d = GetDirectorOrNull(); return d == null ? 0u : d->TimeOffset; } }
    [LuaDocs] public uint WeatherId { get { var d = GetDirectorOrNull(); return d == null ? 0u : d->WeatherId; } }
    [LuaDocs] public bool SpectralCurrentActive { get { var d = GetDirectorOrNull(); return d != null && d->SpectralCurrentActive; } }
    [LuaDocs] public uint Mission1Type { get { var d = GetDirectorOrNull(); return d == null ? 0u : d->Mission1Type; } }
    [LuaDocs] public uint Mission2Type { get { var d = GetDirectorOrNull(); return d == null ? 0u : d->Mission2Type; } }
    [LuaDocs] public uint Mission3Type { get { var d = GetDirectorOrNull(); return d == null ? 0u : d->Mission3Type; } }
    [LuaDocs] public byte Mission1Goal => MissionGoal(Mission1Type);
    [LuaDocs] public byte Mission2Goal => MissionGoal(Mission2Type);
    [LuaDocs] public byte Mission3Goal => MissionGoal(Mission3Type);
    [LuaDocs] public uint Mission1Progress { get { var d = GetDirectorOrNull(); return d == null ? 0u : d->Mission1Progress; } }
    [LuaDocs] public uint Mission2Progress { get { var d = GetDirectorOrNull(); return d == null ? 0u : d->Mission2Progress; } }
    [LuaDocs] public uint Mission3Progress { get { var d = GetDirectorOrNull(); return d == null ? 0u : d->Mission3Progress; } }

    [LuaDocs] public uint Points { get { var m = AgentModule.Instance(); if (m == null) return 0u; var a = m->GetAgentIKDFishingLog(); return a == null ? 0u : a->Points; } }
    [LuaDocs] public uint Score { get { var d = GetResultDataOrNull(); return d == null ? 0u : d->Score; } }
    [LuaDocs] public uint TotalScore { get { var d = GetResultDataOrNull(); return d == null ? 0u : d->TotalScore; } }
}
