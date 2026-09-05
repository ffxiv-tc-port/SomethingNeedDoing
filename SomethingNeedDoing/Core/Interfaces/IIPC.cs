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

    /// <summary>
    /// 每個 IPC 類別的 <see cref="IIPC.Repo"/> 要填的外掛庫網址。
    /// 🔴 台服艦隊有移植版的一律填 <see cref="TcPort"/>：國際服那些庫裡的外掛內部名與台服版
    ///    完全相同，使用者照著加進去會裝到 API15/net10 的版本，在台服的 API13 Dalamud 上載不
    ///    起來，而且會撞掉同一個已安裝鍵。下面剩下的國際服常數只給「台服沒有移植版」的外掛用。
    /// </summary>
    public class Repos
    {
        /// <summary>官方（第一方）外掛庫，不必另外加自訂庫。</summary>
        public const string FirstParty = "";
        /// <summary>台服艦隊的外掛庫。有台服移植版的一律填這個。</summary>
        public const string TcPort = "https://raw.githubusercontent.com/ffxiv-tc-port/DalamudPluginsTC/main/repo.json";
        /// <summary>沒有任何可用的安裝來源（原本的來源已經死掉，台服也沒有移植版）。</summary>
        public const string Unavailable = "";
        public const string Punish = "https://love.puni.sh/ment.json";
        public const string Limiana = "https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json";
        public const string Herc = $"{Dynamis}herc";
        public const string Kawaii = $"{Dynamis}kawaii";
        public const string Veyn = $"{Dynamis}veyn";
        public const string Croizat = $"{Dynamis}croizat";

        private const string Dynamis = "https://puni.sh/api/repository/";
    }
}
