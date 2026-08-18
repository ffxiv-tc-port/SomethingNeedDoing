using Dalamud.Game.ClientState.Objects.SubKinds;
using NLua;
using SomethingNeedDoing.LuaMacro.Wrappers;

namespace SomethingNeedDoing.LuaMacro.Modules;
public unsafe class EntityModule : LuaModuleBase
{
    public override string ModuleName => "Entity";
    protected override object? MetaIndex(LuaTable table, object key) => Svc.Objects[int.Parse(key.ToString() ?? string.Empty)] is { } obj ? new EntityWrapper(obj) : null;

    [LuaFunction] public EntityWrapper? Player => Svc.Objects.LocalPlayer is { } player ? new(player) : null;
    [LuaFunction] public EntityWrapper? Target => Svc.Targets.Target is { } target ? new(target) : null;
    [LuaFunction] public EntityWrapper? FocusTarget => Svc.Targets.FocusTarget is { } target ? new(target) : null;
    [LuaFunction] public EntityWrapper? NearestDeadCharacter => Svc.Objects.OfType<IPlayerCharacter>().OrderBy(ECommons.GameHelpers.Player.DistanceTo).FirstOrDefault(o => o.IsDead) is { } obj ? new(obj) : null;
    [LuaFunction] public EntityWrapper? NearestOtherCharacter => Svc.Objects.LocalPlayer == null ? null : Svc.Objects.OfType<IPlayerCharacter>().OrderBy(ECommons.GameHelpers.Player.DistanceTo).FirstOrDefault(o => o.Name.ToString() != Svc.Objects.LocalPlayer.Name.ToString()) is { } obj ? new(obj) : null;
    [LuaFunction] public EntityWrapper? GetPartyMember(int index) => Svc.Party.GetPartyMemberAddress(index) is { } member ? new(member) : null;
    [LuaFunction] public EntityWrapper? GetAllianceMember(int index) => Svc.Party.GetAllianceMemberAddress(index) is { } member ? new(member) : null;
    [LuaFunction] public EntityWrapper? GetEntityByName(string name) => Svc.Objects.FirstOrDefault(o => o.Name.TextValue.Equals(name, StringComparison.InvariantCultureIgnoreCase)) is { } obj ? new(obj) : null;

    [LuaFunction(description: "Gets all game objects within the given range (yalms) of the player, ordered by distance (includes the player itself). Returns an empty list when there is no local player. One call replaces scanning Entity[0..599] index by index from Lua.")]
    public List<EntityWrapper> GetNearbyObjects(float range) => Svc.Objects.LocalPlayer == null
        ? []
        : [.. Svc.Objects
            .Where(o => ECommons.GameHelpers.Player.DistanceTo(o) <= range)
            .OrderBy(ECommons.GameHelpers.Player.DistanceTo)
            .Select(o => new EntityWrapper(o))];
}
