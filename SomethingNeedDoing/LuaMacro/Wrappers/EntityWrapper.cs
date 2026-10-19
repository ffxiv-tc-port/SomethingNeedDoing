using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.LuaMacro.Wrappers;

// 🔴 存「身分」不存「位址」:只存 GameObjectId,每次屬性存取時重新查物件表。巨集慣於把包裝物件存進區域變數、
//    跨 Sleep/yield 反覆讀;原生物件由遊戲擁有,離開視野、換區、登出都會讓那塊記憶體被回收或改配給別的實體,
//    保存的 GameObject* 再解參考輕則靜默讀到別人的欄位,重則是攔不到的 AccessViolationException。
//    ⚠️ Dalamud 的 IGameObject 包裝同樣不能跨幀持有;IGameObject.IsValid() 只回報「有沒有登入」,不能當防護。

// 查表走 GameObjectManager.Instance()->Objects.IndexSorted(單純的欄位讀取)。刻意不用 GetObjectByGameObjectId():
//    它是 [MemberFunction],特徵碼在台服失配時會擲例外,等於把「查不到這個實體」變成「每一次屬性存取都爆例外」。
//    管理器走 GameStatics.TryGameObjectManager():位址沒解出(改版後特徵碼對不上)時回 null,當作「查不到這個實體」。
// 效能:建構是掃整張物件表做純指標比對(只比較不解參考);之後每次存取靠 _index 提示是 O(1),物件換了槽位才退成掃表。

// 失敗語意:查不到實體時一律安靜回中性值,不寫 log(巨集會把這些放進等待迴圈輪詢);要分辨請用 Exists。
//    🔴 DistanceTo 是唯一不能回 0 的:回 0 會讓 `if e.DistanceTo < 3` 成立,巨集會以為自己就站在目標身上;
//       回 float.MaxValue 才能保證任何「夠近嗎」的判斷都是 false。
//    ⚠️ HealthPercent 取不到時是 NaN(不是 0):NaN 讓 `< 20` 與 `> 80` 同時為 false,回 0 會讓「快死了」的判斷誤觸發。
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

    // ⚠️ IPartyMember.Address 指的是 Client::Game::Group::PartyMember 結構,不是 GameObject;
    //    直接轉型會拿 PartyMember 的位元組當 GameObject 的欄位讀,靜默給出垃圾值。
    //    正解與 Dalamud 自己的 PartyMember.GameObject 一致:用 EntityId 去物件表查。
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

        var manager = GameStatics.TryGameObjectManager();
        if (manager == null) return (0ul, UnknownIndex);
        var objects = manager->Objects.IndexSorted;
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

        var manager = GameStatics.TryGameObjectManager();
        if (manager == null) return (id, UnknownIndex);
        var objects = manager->Objects.IndexSorted;
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

            var manager = GameStatics.TryGameObjectManager();
            if (manager == null) return null;
            var objects = manager->Objects.IndexSorted;

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
    // 取不到角色時回 NaN —— 理由見檔頭。
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

    // 解析不到實體時 SetAsTarget / SetAsFocusTarget 是 no-op:把 null 指派給 Svc.Targets.Target 是把當前目標「清掉」——
    // 對一個叫做「設為目標」的函式來說是最糟的失敗方式。
    [LuaDocs] public void SetAsTarget() { if (DalamudObj is { } obj) Svc.Targets.Target = obj; }
    [LuaDocs] public void SetAsFocusTarget() { if (DalamudObj is { } obj) Svc.Targets.FocusTarget = obj; }
    [LuaDocs] public void ClearTarget() => Svc.Targets.Target = null;
    [LuaDocs(description: "Issues an interaction with this entity. There is no return value: the underlying game call reports no status, so success can only be judged from the resulting game state (an addon opening, a target being engaged, etc).")]
    public void Interact() => Game.Interact(DalamudObj); // Game.Interact 自己擋 null
}
