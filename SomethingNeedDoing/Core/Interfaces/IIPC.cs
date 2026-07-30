using ECommons.EzIpcManager;

namespace SomethingNeedDoing.Core.Interfaces;

/// <summary>
/// Interface for all IPC classes to implement.
/// This allows for consistent discovery and handling of IPC classes.
/// </summary>
public interface IIPC
{
    /// <summary>
    /// Gets the name of the IPC interface as it will appear in Lua.
    /// </summary>
    string Name { get; }
    string Repo { get; }
}

public abstract class IPC : IIPC
{
    public abstract string Name { get; }
    public abstract string Repo { get; }
    // Name 同時是 Lua 模組名與 EzIPC 前綴,所以必須等於外掛的 InternalName;
    // 但 InstalledPlugins 的 Name 是「顯示名稱」,兩者不一定相同
    // (例如 TCToolbox 的顯示名稱是「TC Toolbox」),因此兩個都比對。
    public bool IsInstalled => Svc.PluginInterface.InstalledPlugins.Any(p => (p.Name == Name || p.InternalName == Name) && p.IsLoaded);
    public IPC() => EzIPC.Init(this, Name);

    public class Repos
    {
        public const string FirstParty = "";
        public const string TcPort = "https://raw.githubusercontent.com/ffxiv-tc-port/DalamudPluginsTC/main/repo.json";
        public const string Liza = "https://git.carvel.li/liza/";
        public const string Punish = "https://love.puni.sh/ment.json";
        public const string Limiana = "https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json";
        public const string Herc = $"{Dynamis}herc";
        public const string Kawaii = $"{Dynamis}kawaii";
        public const string Veyn = $"{Dynamis}veyn";
        public const string Croizat = $"{Dynamis}croizat";

        private const string Dynamis = "https://puni.sh/api/repository/";
    }
}
