using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Concurrent;
using CSFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace SomethingNeedDoing.Utils;

/// <summary>
/// Non-throwing access to ClientStructs static addresses and member function pointers.
/// </summary>
/// <remarks>
/// Contract: returns null / false when the address was not resolved (signature mismatch after a game update);
/// the generated <c>Instance()</c> and member functions throw in that case. Callers must check the result.
/// Each unresolved address is logged once, so polling accessors do not flood the log.
/// Nothing is cached: every call re-reads the address, and callers must not keep the pointer across frames.
/// </remarks>
internal static unsafe class GameStatics
{
    private static readonly ConcurrentDictionary<string, byte> Reported = new();

    // ── The static address is the object itself: non-null whenever it is resolved. ──

    public static UIState* TryUIState()
    {
        var instance = UIState.StaticAddressPointers.pInstance;
        Resolved(instance, "UIState.Instance");
        return instance;
    }

    public static ContentsFinder* TryContentsFinder()
    {
        var instance = ContentsFinder.StaticAddressPointers.pInstance;
        Resolved(instance, "ContentsFinder.Instance");
        return instance;
    }

    public static Telepo* TryTelepo()
    {
        var instance = Telepo.StaticAddressPointers.pInstance;
        Resolved(instance, "Telepo.Instance");
        return instance;
    }

    public static ActionManager* TryActionManager()
    {
        var instance = ActionManager.StaticAddressPointers.pInstance;
        Resolved(instance, "ActionManager.Instance");
        return instance;
    }

    public static InventoryManager* TryInventoryManager()
    {
        var instance = InventoryManager.StaticAddressPointers.pInstance;
        Resolved(instance, "InventoryManager.Instance");
        return instance;
    }

    public static PlayerState* TryPlayerState()
    {
        var instance = PlayerState.StaticAddressPointers.pInstance;
        Resolved(instance, "PlayerState.Instance");
        return instance;
    }

    public static QuestManager* TryQuestManager()
    {
        var instance = QuestManager.StaticAddressPointers.pInstance;
        Resolved(instance, "QuestManager.Instance");
        return instance;
    }

    public static MonsterNoteManager* TryMonsterNoteManager()
    {
        var instance = MonsterNoteManager.StaticAddressPointers.pInstance;
        Resolved(instance, "MonsterNoteManager.Instance");
        return instance;
    }

    public static GameObjectManager* TryGameObjectManager()
    {
        var instance = GameObjectManager.StaticAddressPointers.pInstance;
        Resolved(instance, "GameObjectManager.Instance");
        return instance;
    }

    public static TargetSystem* TryTargetSystem()
    {
        var instance = TargetSystem.StaticAddressPointers.pInstance;
        Resolved(instance, "TargetSystem.Instance");
        return instance;
    }

    public static GameMain* TryGameMain()
    {
        var instance = GameMain.StaticAddressPointers.pInstance;
        Resolved(instance, "GameMain.Instance");
        return instance;
    }

    // ── The static address is a slot holding the pointer: also null while the game has not created the object. ──

    public static CSFramework* TryFramework()
    {
        var slot = CSFramework.StaticAddressPointers.ppInstance;
        return Resolved(slot, "Framework.Instance") ? *slot : null;
    }

    public static EventFramework* TryEventFramework()
    {
        var slot = EventFramework.StaticAddressPointers.ppInstance;
        return Resolved(slot, "EventFramework.Instance") ? *slot : null;
    }

    public static FateManager* TryFateManager()
    {
        var slot = FateManager.StaticAddressPointers.ppInstance;
        return Resolved(slot, "FateManager.Instance") ? *slot : null;
    }

    public static HousingManager* TryHousingManager()
    {
        var slot = HousingManager.StaticAddressPointers.ppInstance;
        return Resolved(slot, "HousingManager.Instance") ? *slot : null;
    }

    public static AtkStage* TryAtkStage()
    {
        var slot = AtkStage.StaticAddressPointers.ppInstance;
        return Resolved(slot, "AtkStage.Instance") ? *slot : null;
    }

    /// <summary>True when everything <c>UIModule.Instance()</c> and the module getters built on it go through is resolved.</summary>
    public static bool UiModuleResolved()
        => Resolved(CSFramework.StaticAddressPointers.ppInstance, "Framework.Instance")
           && Resolved(CSFramework.MemberFunctionPointers.GetUIModule, "Framework.GetUIModule");

    /// <summary>True when everything <c>AgentModule.Instance()</c> and the generated <c>AgentX.Instance()</c> go through is resolved.</summary>
    public static bool AgentChainResolved()
        => UiModuleResolved() && Resolved(AgentModule.MemberFunctionPointers.GetAgentByInternalId, "AgentModule.GetAgentByInternalId");

    /// <summary>Null when the inventory addresses are unresolved, or when the game has no such container right now.</summary>
    public static InventoryContainer* TryInventoryContainer(InventoryType type)
    {
        var manager = TryInventoryManager();
        if (manager == null || !Resolved(InventoryManager.MemberFunctionPointers.GetInventoryContainer, "InventoryManager.GetInventoryContainer")) return null;
        return manager->GetInventoryContainer(type);
    }

    /// <summary>True when <paramref name="address"/> (a static address or a member function pointer) was resolved.</summary>
    public static bool Resolved(void* address, string name)
    {
        if (address != null) return true;
        if (Reported.TryAdd(name, 0))
            FrameworkLogger.Error($"{name} could not be resolved (game update?). Lua members that need it return their no-data value or do nothing until the plugin is updated.");
        return false;
    }
}
