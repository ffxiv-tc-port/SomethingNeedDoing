using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.ImGuiMethods;
using SomethingNeedDoing.Documentation;
using System.Reflection;

namespace SomethingNeedDoing.Gui;

public class ChangelogWindow : Window
{
    private readonly Dictionary<string, List<ChangelogClassGroup>> _versionedGroups = [];
    private readonly List<string> _sortedVersions = [];

    public ChangelogWindow() : base($"{P.Name} - Changelog###{P.Name}_{nameof(ChangelogWindow)}", ImGuiWindowFlags.NoScrollbar)
    {
        Size = new(600, 400);
        SizeCondition = ImGuiCond.FirstUseEver;
        var asm = typeof(ChangelogWindow).Assembly;
        var allTypes = asm.GetTypes();
        var allEntries = allTypes.SelectMany(type =>
            type.GetCustomAttributes<ChangelogAttribute>().Select(attr => new ChangelogEntry(attr, type.Name, type, "Class"))
            .Concat(type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .SelectMany(member => member.GetCustomAttributes<ChangelogAttribute>()
                    .Select(attr => new ChangelogEntry(attr, type.Name, type, member.MemberType.ToString(), member)))))
            .ToList();
        foreach (var versionGroup in allEntries.GroupBy(e => e.Version))
        {
            var classGroups = versionGroup.GroupBy(e => e.ParentClass)
                .Select(classGroup =>
                {
                    var classType = allTypes.FirstOrDefault(t => t.Name == classGroup.Key);
                    if (classType == null) return null;
                    var members = classGroup.Where(e => e.MemberType != "Class").ToList();
                    return new ChangelogClassGroup
                    {
                        ClassName = classGroup.Key,
                        Members = members.GroupBy(e => e.TargetName)
                            .ToDictionary(g => g.Key, g => new ChangelogMemberEntry
                            {
                                Name = g.Key,
                                ReturnType = g.First().MemberInfo is PropertyInfo pi ? pi.PropertyType : g.First().MemberInfo is MethodInfo mi ? mi.ReturnType : null,
                                Entries = [.. g]
                            })
                    };
                })
                .Where(cg => cg != null)
                .ToList()!;
            _versionedGroups[versionGroup.Key] = classGroups;
        }
        _sortedVersions = [.. _versionedGroups.Keys.OrderByDescending(v => v, new VersionComparer())];

        AddGeneralChangelogs();
        var currentVersion = P.Version;
        var anyChanges = _sortedVersions.Count > 0 && _sortedVersions[0] == currentVersion && _versionedGroups[_sortedVersions[0]].Any(cg => cg.Members.Count > 0);
        if (anyChanges && C.LastSeenVersion != P.Version)
            IsOpen = true;
    }

    private void AddGeneralChangelogs()
    {
        Add("12.8", "修正函式層級觸發事件造成暫存巨集遞迴產生的問題");
        Add("12.9", "新增全部暫停/繼續/停止指令");
        Add("12.10", "新增由 Faye 製作的 lua stub 產生器（輸出於設定目錄，用於腳本檢查）");
        Add("12.10", "修正原生巨集執行時等待未正確套用的問題");
        Add("12.11", "修正 /loop");
        Add("12.11", "修正非製作動作觸發製作跳過的問題");
        Add("12.13", "新增所有列舉的匯入，現在可透過 luanet.enum 呼叫");
        Add("12.14", "為 lua 腳本新增 OnCleanup 函式");
        Add("12.15", "編輯器新增語法高亮");
        Add("12.16", "編輯器新增變更類型的功能");
        Add("12.17", "修正 Craftloop 以及部分動作跳過/等待的問題");
        Add("12.18", "修正 autoretainer 後處理事件");
        Add("12.19", "新增由 Faye 製作的進階 stub 產生器");
        Add("12.24", "修正遠端依賴項並新增檔案快取系統");
        Add("12.25", "修正刪除資料夾的問題，新增重新命名資料夾功能");
        Add("12.35", "新增設定模組，提供巨集可在程式碼外編輯的可設定選項");
        Add("12.41", "新增從網址匯入新巨集的功能");
        Add("12.53", "新增 OnActivePluginsChanged");
        Add("12.64", "將清單索引修飾詞名稱從 listindex 改為 list");
        Add("12.75", "新增副本開始/團滅/完成事件");
        Add("12.76", "修正 git 巨集在更新時重置設定的問題");
    }

    private void Add(string version, string description)
    {
        var attr = new ChangelogAttribute(version, ChangelogType.Changed, description);
        var entry = new ChangelogEntry(attr, "General", typeof(ChangelogWindow), "General", null);

        if (!_versionedGroups.TryGetValue(version, out var classGroups))
        {
            classGroups = [];
            _versionedGroups[version] = classGroups;
        }
        var generalGroup = classGroups.FirstOrDefault(g => g.ClassName == "General");
        if (generalGroup == null)
        {
            generalGroup = new ChangelogClassGroup { ClassName = "General" };
            classGroups.Add(generalGroup);
        }
        if (!generalGroup.Members.TryGetValue("General", out var memberEntry))
        {
            memberEntry = new ChangelogMemberEntry { Name = "General" };
            generalGroup.Members["General"] = memberEntry;
        }
        memberEntry.Entries.Add(entry);

        if (!_sortedVersions.Contains(version))
        {
            _sortedVersions.Add(version);
            _sortedVersions.Sort((a, b) => new VersionComparer().Compare(b, a));
        }
    }

    public override void OnOpen()
    {
        C.LastSeenVersion = P.Version;
        C.Save();
        base.OnOpen();
    }

    public override void Draw()
    {
        foreach (var version in _sortedVersions)
        {
            var flags = P.Version == version ? ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.CollapsingHeader : ImGuiTreeNodeFlags.CollapsingHeader;
            using (ImRaii.PushColor(ImGuiCol.Text, EzColor.Green.Vector4, flags.HasFlag(ImGuiTreeNodeFlags.DefaultOpen)))
                if (!ImGui.CollapsingHeader($"Version {version}", flags)) continue;
            var classGroups = _versionedGroups[version];
            var classGroupDict = classGroups.ToDictionary(cg => cg.ClassName, cg => cg);
            var usedAsReturnType = classGroups.SelectMany(cg => cg.Members.Values)
                .Select(m => m.ReturnType?.Name).Where(rt => !string.IsNullOrEmpty(rt)).ToHashSet();
            var rootClassNames = classGroups.Where(cg => !usedAsReturnType.Contains(cg.ClassName) || cg.Members.Count == 0)
                .Select(cg => cg.ClassName).ToList();
            var allReachableTypes = new HashSet<string>();
            foreach (var root in rootClassNames)
            {
                var visited = new HashSet<string>();
                CollectReachableTypes(root, classGroupDict, visited);
                foreach (var t in visited) allReachableTypes.Add(t);
            }
            foreach (var root in rootClassNames)
            {
                if (!classGroupDict.TryGetValue(root, out var classGroup)) continue;
                using var tree = ImRaii.TreeNode(classGroup.ClassName, ImGuiTreeNodeFlags.DefaultOpen);
                if (!tree) continue;
                var visited = new HashSet<string> { classGroup.ClassName };
                foreach (var memberEntry in classGroup.Members.Values)
                    DrawMemberWithReturnTypeRecursive(memberEntry, classGroupDict, visited, allReachableTypes);
            }
            foreach (var classGroup in classGroups)
            {
                if (rootClassNames.Contains(classGroup.ClassName) || allReachableTypes.Contains(classGroup.ClassName) || classGroup.Members.Count == 0) continue;
                using var tree = ImRaii.TreeNode(classGroup.ClassName, ImGuiTreeNodeFlags.DefaultOpen);
                if (!tree) continue;
                var visited = new HashSet<string> { classGroup.ClassName };
                foreach (var memberEntry in classGroup.Members.Values)
                    DrawMemberWithReturnTypeRecursive(memberEntry, classGroupDict, visited, allReachableTypes);
            }
        }
    }

    private static void CollectReachableTypes(string className, Dictionary<string, ChangelogClassGroup> classGroupDict, HashSet<string> visited)
    {
        if (!classGroupDict.TryGetValue(className, out var classGroup) || !visited.Add(className)) return;
        foreach (var member in classGroup.Members.Values)
            if (member.ReturnType is { Name.Length: > 0, Name: var name } && classGroupDict.ContainsKey(name))
                CollectReachableTypes(name, classGroupDict, visited);
    }

    private static void DrawMemberWithReturnTypeRecursive(ChangelogMemberEntry memberEntry, Dictionary<string, ChangelogClassGroup> classGroupDict, HashSet<string> visited, HashSet<string> allReachableTypes)
    {
        var returnType = memberEntry.ReturnType;
        var returnTypeName = returnType?.Name;
        var hasReturnTypeData = returnType != null && classGroupDict.ContainsKey(returnTypeName) && classGroupDict[returnTypeName].Members.Count > 0 && !visited.Contains(returnTypeName);
        var label = memberEntry.Name + (returnType != null ? $" → {LuaTypeConverter.GetLuaType(returnType).TypeName}" : "");

        // I can't wait for something to legitimately be named general and mess this up
        if (memberEntry.Name == "General" && classGroupDict.TryGetValue("General", out var cg) && cg.Members["General"] == memberEntry)
        {
            foreach (var entry in memberEntry.Entries)
                if (!string.IsNullOrEmpty(entry.Description))
                    using (ImRaii.TextWrapPos(0f))
                        ImGui.TextUnformatted($"{entry.Description}");
            return;
        }

        if (hasReturnTypeData)
        {
            using var tree = ImRaii.TreeNode(label, ImGuiTreeNodeFlags.DefaultOpen);
            if (tree)
            {
                foreach (var entry in memberEntry.Entries)
                    if (!string.IsNullOrEmpty(entry.Description))
                        using (ImRaii.TextWrapPos(0f))
                            ImGui.TextUnformatted($"{entry.ChangeType} {entry.Description}");
                visited.Add(returnTypeName!);
                foreach (var subMember in classGroupDict[returnTypeName!].Members.Values)
                    DrawMemberWithReturnTypeRecursive(subMember, classGroupDict, visited, allReachableTypes);
                visited.Remove(returnTypeName!);
            }
        }
        else
        {
            ImGui.BulletText(label);
            foreach (var entry in memberEntry.Entries)
                if (!string.IsNullOrEmpty(entry.Description))
                    using (ImRaii.TextWrapPos(0f))
                        ImGui.TextUnformatted($"{entry.ChangeType} {entry.Description}");
        }
    }

    private class ChangelogClassGroup
    {
        public string ClassName = string.Empty;
        public Dictionary<string, ChangelogMemberEntry> Members = [];
    }

    private class ChangelogMemberEntry
    {
        public string Name = string.Empty;
        public Type? ReturnType;
        public List<ChangelogEntry> Entries = [];
    }

    private class ChangelogEntry(ChangelogAttribute attr, string parentClass, Type declaringType, string memberType, MemberInfo? memberInfo = null)
    {
        public string Version = attr.Version;
        public ChangelogType ChangeType = attr.ChangeType;
        public string ParentClass = parentClass;
        public string TargetName = memberInfo?.Name ?? parentClass;
        public string? Description = attr.Description;
        public string MemberType = memberType;
        public Type? DeclaringType = declaringType;
        public MemberInfo? MemberInfo = memberInfo;
    }

    private class VersionComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var xParts = x.Split('.');
            var yParts = y.Split('.');

            var maxLength = Math.Max(xParts.Length, yParts.Length);

            for (var i = 0; i < maxLength; i++)
            {
                var xPart = i < xParts.Length ? xParts[i] : "0";
                var yPart = i < yParts.Length ? yParts[i] : "0";

                if (int.TryParse(xPart, out var xNum) && int.TryParse(yPart, out var yNum))
                {
                    if (xNum != yNum)
                        return xNum.CompareTo(yNum);
                }
                else
                {
                    // If parsing fails, fall back to string comparison
                    var stringCompare = string.Compare(xPart, yPart, StringComparison.Ordinal);
                    if (stringCompare != 0)
                        return stringCompare;
                }
            }

            return 0;
        }
    }
}
