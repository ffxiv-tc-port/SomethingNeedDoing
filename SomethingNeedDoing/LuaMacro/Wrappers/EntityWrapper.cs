using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.LuaMacro.Wrappers;

// 這一層的每個成員都掛 [LuaDocs],使用者巨集叫得到,而巨集的典型寫法就是把包裝物件存進區域
// 變數,然後跨 Sleep/yield 反覆讀它:
//
//     local mob = Entity.Target
//     while mob.CurrentHp > 0 do        -- 這裡的每一次讀取都落在不同的幀
//         yield("/wait 1")
//     end
//
// 🔴 所以「建構時把 GameObject* 凍結下來」這個作法從根本上是錯的。原生物件由遊戲的
//    GameObjectManager 擁有:離開視野、死亡消失、換區、登出都會讓那塊記憶體被回收,或被
//    改配給另一個完全不同的實體。凍結的指標之後再解參考,輕則讀到別人的欄位(靜默給出錯的
//    答案),重則 AccessViolationException —— 那在 .NET Core 屬於 corrupted-state exception,
//    C# 的 try/catch 與 Lua 的 pcall 都攔不到,遊戲當場關閉。
//    ⚠️ Dalamud 的 IGameObject.Address 同樣是建構當下凍結、永不重新解析的;
//       IGameObject.IsValid() 只回報「有沒有登入」,不是「這個物件還在不在」,不能當防護。
//
// ⇒ 這裡改成存「身分」不存「位址」:GameObjectId(ulong),每次屬性存取時重新查物件表。
//   查表走 GameObjectManager.Instance()->Objects.IndexSorted —— 那是單純的欄位讀取,
//   不依賴任何特徵碼。刻意不用 Objects.GetObjectByGameObjectId():它是 [MemberFunction],
//   特徵碼在台服失配時產生器會擲 ThrowNullAddress,等於把「查不到這個實體」變成
//   「每一次屬性存取都爆例外」,而這一層的合約是安靜回中性值。
//   ⚠️ GameObjectManager.Instance() 的 [StaticAddress] 沒有 isPointer,產生器實作是
//      `return pInstance;`(靜態位址本身),特徵碼失配時擲例外、永遠不會回 null ——
//      對它判空是死碼,所以這裡刻意不判。要把這段抄去別的 Instance() 之前,
//      先看那個型別的 [StaticAddress] 有沒有 isPointer(有的話實作是 `*ppInstance`,會是 null)。
//
// 效能:建構是 O(n) —— 掃 819 格做純指標比對(只比較不解參考,見 IdentifyByAddress 的理由)。
//       ⚠️ 這個代價沒有實測過,只是靜態估計:819 次有邊界檢查的 Span 索引 + 指標比較,
//          對巨集掃 Entity[0..599] 那種寫法是每輪 60 萬次比較,量級應該遠低於它自己的
//          600 次 Lua interop;若實機上真的成為瓶頸,再考慮讓呼叫端把已知的槽位傳進來。
//       之後每次屬性存取是 O(1):_index 這個提示讓重查只要一次指標讀 + 一次虛擬函式呼叫,
//       只有物件真的換了槽位才退成掃 819 格。
//       同一次屬性呼叫「內部」一律先把查到的指標存進區域變數(單幀之內安全),
//       但不跨呼叫快取 —— 跨呼叫的快取就是這次要根治的那個 bug。
//
// 失敗語意:查不到實體時一律安靜回中性值,不寫 log(巨集會把這些放進等待迴圈輪詢,每次失敗
//           記一行會把整份 log 洗掉)。要分辨「值就是這樣」與「這個實體已經不在了」請用 Exists。
//           🔴 DistanceTo 是唯一不能回 0 的:回 0 會讓 `if e.DistanceTo < 3` 成立,巨集會以為
//              自己就站在目標身上。回 float.MaxValue 才能保證任何「夠近嗎」的判斷都是 false。
//           ⚠️ HealthPercent 維持原本的 0/0 = NaN(不是 0)。NaN 讓 `< 20` 與 `> 80` 同時為
//              false,是這裡最安全的中性值;回 0 會讓「快死了」的判斷誤觸發。
public unsafe class EntityWrapper : IWrapper
{
    /// <summary>這個包裝物件唯一持有的狀態:實體的 GameObjectId。0 = 建構當下就沒認出實體。</summary>
    private readonly ulong _id;

    /// <summary>上次看到它的物件表槽位,只是加速用的提示;取用時一定會再驗 GameObjectId。</summary>
    private readonly ushort _index;

    /// <summary>_index 的「不知道」值。819 格的表永遠不會有這個索引,所以會直接落到掃描路徑。</summary>
    private const ushort UnknownIndex = ushort.MaxValue;

    public EntityWrapper(GameObject* obj) => (_id, _index) = IdentifyByAddress(obj);
    public EntityWrapper(nint obj) => (_id, _index) = IdentifyByAddress((GameObject*)obj);
    public EntityWrapper(IGameObject? obj) => (_id, _index) = obj == null ? (0ul, UnknownIndex) : IdentifyByAddress((GameObject*)obj.Address);

    // ⚠️ IPartyMember.Address 指的是 Client::Game::Group::PartyMember 結構,不是 GameObject。
    //    原本的 `(GameObject*)obj.Address` 會拿 PartyMember 的位元組當 GameObject 的欄位讀
    //    (名字讀 +0x30、座標讀 +0xB0),靜默給出垃圾值。正解與 Dalamud 自己的
    //    PartyMember.GameObject 一致:用 EntityId 去物件表查。
    //    空位或不在同一區的隊員 EntityId 是 0 / 0xE0000000,查不到就是一個空的 wrapper。
    public EntityWrapper(IPartyMember? member) => (_id, _index) = member == null || member.EntityId is 0 or 0xE0000000
        ? (0ul, UnknownIndex)
        : IdentifyById(member.EntityId);

    public EntityWrapper(GameObjectId id) => (_id, _index) = IdentifyById(id.Id);

    /// <summary>
    /// 從裸位址認出實體。
    /// 🔴 不可以直接對傳進來的位址呼叫 GetGameObjectId():那是虛擬函式(vtable 槽 1),
    /// 位址若不是真的 GameObject,等於透過一個假的 vtable 指標跳轉 —— 必定崩潰,而且攔不到。
    /// ⇒ 先在物件表裡做「指標 == 指標」的比對(純比較,完全不解參考,對任何輸入都安全),
    /// 確認這個位址真的是遊戲自己持有的物件之後,才敢讀它的身分。
    /// </summary>
    private static (ulong Id, ushort Index) IdentifyByAddress(GameObject* obj)
    {
        if (obj == null) return (0ul, UnknownIndex);

        var objects = GameObjectManager.Instance()->Objects.IndexSorted;
        for (var i = 0; i < objects.Length; i++)
        {
            if (objects[i].Value != obj) continue;
            return (obj->GetGameObjectId().Id, (ushort)i);
        }

        // 不在物件表裡:可能是別種結構的位址,也可能是已經被回收的舊指標。兩種都不能碰。
        return (0ul, UnknownIndex);
    }

    /// <summary>從 GameObjectId 認出實體。查不到時仍然把 id 留著 —— 實體之後可能又進視野,重查會找回來。</summary>
    private static (ulong Id, ushort Index) IdentifyById(ulong id)
    {
        if (id == 0) return (0ul, UnknownIndex);

        var objects = GameObjectManager.Instance()->Objects.IndexSorted;
        for (var i = 0; i < objects.Length; i++)
        {
            var candidate = objects[i].Value;
            if (candidate == null || candidate->GetGameObjectId().Id != id) continue;
            return (id, (ushort)i);
        }

        return (id, UnknownIndex);
    }

    /// <summary>
    /// 每次存取都重新解析出來的原生指標。呼叫端一律先存進區域變數再用,不要在同一個屬性裡讀兩次。
    /// ⚠️ GameObjectId 對非網路物件(EntityId == 0xE0000000)可能取自 BaseId,理論上會與同 BaseId
    /// 的兄弟物件撞號;_index 的快路徑會先命中原本那一格,只有它真的換過槽位才可能解析到兄弟。
    /// 這個取捨是刻意的:換成「可能指到長得一樣的鄰居」也遠比「解參考已回收的記憶體」安全。
    /// </summary>
    private GameObject* Obj
    {
        get
        {
            if (_id == 0) return null;

            var objects = GameObjectManager.Instance()->Objects.IndexSorted;

            // 快路徑:物件多半還在建構當下那個槽位。仍然要驗身分,槽位是會被別人接手的。
            if (_index < objects.Length)
            {
                var hinted = objects[_index].Value;
                if (hinted != null && hinted->GetGameObjectId().Id == _id) return hinted;
            }

            // 慢路徑:換槽了(或建構時就沒找到)。掃一次整張表。
            for (var i = 0; i < objects.Length; i++)
            {
                var candidate = objects[i].Value;
                if (candidate != null && candidate->GetGameObjectId().Id == _id) return candidate;
            }

            return null;
        }
    }

    private IGameObject? DalamudObj => Svc.Objects.CreateObjectReference((nint)Obj);

    private Character* Character
    {
        get
        {
            var obj = Obj;
            return obj != null && obj->IsCharacter() ? (Character*)obj : null;
        }
    }

    private BattleChara* BattleChara
    {
        get
        {
            var obj = Obj;
            return obj != null && obj->ObjectKind == ObjectKind.BattleNpc ? (BattleChara*)obj : null;
        }
    }

    [LuaDocs(description: "Whether this entity can still be resolved in the object table right now. Check this before trusting the other properties: when it is false they return neutral values (0 / empty string / false, and DistanceTo returns a huge number so no proximity check can pass).")]
    [Changelog(ChangelogAttribute.Unreleased)]
    public bool Exists => Obj != null;

    [LuaDocs(description: "The GameObjectId this wrapper was built from. Stays the same even while the entity is out of range, so it can be stored and re-resolved later.")]
    [Changelog(ChangelogAttribute.Unreleased)]
    public ulong Id => _id;

    // ObjectKind 有明確的 None = 0,直接拿它當「沒有這個實體」。
    [LuaDocs] public ObjectKind Type { get { var obj = Obj; return obj == null ? ObjectKind.None : obj->ObjectKind; } }
    [LuaDocs] public string Name { get { var obj = Obj; return obj == null ? string.Empty : obj->NameString; } }
    [LuaDocs] public Vector3 Position { get { var obj = Obj; return obj == null ? default : obj->Position; } }

    // 🔴 這個不能回 0,理由見檔頭。
    [LuaDocs] public float DistanceTo { get { var obj = Obj; return obj == null ? float.MaxValue : Player.DistanceTo(obj->Position); } }

    [LuaDocs] public ulong ContentId { get { var chr = Character; return chr == null ? 0ul : chr->ContentId; } }
    [LuaDocs] public ulong AccountId { get { var chr = Character; return chr == null ? 0ul : chr->AccountId; } }
    [LuaDocs] public ushort CurrentWorld { get { var chr = Character; return chr == null ? (ushort)0 : chr->CurrentWorld; } }
    [LuaDocs] public ushort HomeWorld { get { var chr = Character; return chr == null ? (ushort)0 : chr->HomeWorld; } }

    [LuaDocs] public uint CurrentHp { get { var chr = Character; return chr == null ? 0u : chr->Health; } }
    [LuaDocs] public uint MaxHp { get { var chr = Character; return chr == null ? 0u : chr->MaxHealth; } }

    // 一次解析算完兩個值,不要讓 CurrentHp / MaxHp 各查一次物件表(而且中間可能查到不同狀態)。
    // 取不到角色時回 NaN,與原本的 0/0 完全一致 —— 理由見檔頭。
    [LuaDocs]
    public float HealthPercent
    {
        get
        {
            var chr = Character;
            return chr == null ? float.NaN : (float)chr->Health / chr->MaxHealth * 100f;
        }
    }

    [LuaDocs] public uint CurrentMp { get { var chr = Character; return chr == null ? 0u : chr->Mana; } }
    [LuaDocs] public uint MaxMp { get { var chr = Character; return chr == null ? 0u : chr->MaxMana; } }

    [LuaDocs] public EntityWrapper? Target => DalamudObj?.TargetObject is { } target ? new(target) : null;
    [LuaDocs] public bool IsCasting { get { var chr = Character; return chr != null && chr->IsCasting; } }
    [LuaDocs] public bool IsTargetable { get { var obj = Obj; return obj != null && obj->GetIsTargetable(); } }

    // GetCastInfo() 對某些 Character 子類(例如 Companion)回 null,CS 的註解逐字寫著這件事。
    [LuaDocs]
    public bool IsCastInterruptible
    {
        get
        {
            var chr = Character;
            if (chr == null) return false;
            var cast = chr->GetCastInfo();
            return cast != null && cast->Interruptible;
        }
    }

    [LuaDocs] public bool IsInCombat { get { var chr = Character; return chr != null && chr->InCombat; } }

    [LuaDocs]
    public byte HuntRank
    {
        get
        {
            var obj = Obj;
            if (obj == null) return 0;
            var baseId = obj->BaseId;
            return FindRow<NotoriousMonster>(x => x.BNpcBase.Value!.RowId == baseId)?.Rank ?? 0;
        }
    }

    [LuaDocs]
    [Changelog("12.15")]
    public bool IsMounted
    {
        get
        {
            var obj = Obj;
            if (obj == null || obj->ObjectKind != ObjectKind.Pc) return false;
            if (obj->ObjectIndex + 1 > Svc.Objects.Length) return false;
            return Svc.Objects[obj->ObjectIndex + 1] is { ObjectKind: Dalamud.Game.ClientState.Objects.Enums.ObjectKind.MountType };
        }
    }

    // GetStatusManager() 是虛擬函式(槽 77),不是每個 Character 子類都有狀態管理器。
    // 讀不到就與「這不是 BattleNpc」回同一個值(null),巨集的判斷式不必多一種分支。
    [LuaDocs]
    [Changelog("12.22")]
    public List<StatusWrapper>? Status
    {
        get
        {
            var chara = BattleChara;
            if (chara == null) return null;
            var manager = chara->GetStatusManager();
            if (manager == null) return null;
            return [.. manager->Status.ToArray().Select(x => new StatusWrapper(x))];
        }
    }

    [LuaDocs][Changelog("12.22")] public ushort FateId { get { var chara = BattleChara; return chara == null ? (ushort)0 : chara->FateId; } }

    // 解析不到實體時這三個是 no-op。原本寫成 `Svc.Targets.Target = DalamudObj;`,實體不在時
    // 那是把當前目標「清掉」—— 對一個叫做「設為目標」的函式來說是最糟的失敗方式。
    [LuaDocs] public void SetAsTarget() { if (DalamudObj is { } obj) Svc.Targets.Target = obj; }
    [LuaDocs] public void SetAsFocusTarget() { if (DalamudObj is { } obj) Svc.Targets.FocusTarget = obj; }
    [LuaDocs] public void ClearTarget() => Svc.Targets.Target = null;
    [LuaDocs] public void Interact() => Game.Interact(DalamudObj); // Game.Interact 自己擋 null
}
