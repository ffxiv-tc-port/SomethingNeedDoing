using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using NLua;
using SomethingNeedDoing.Core.Interfaces;
using System.Reflection;

namespace SomethingNeedDoing.LuaMacro.Modules;

public class ExcelModule : LuaModuleBase
{
    public override string ModuleName => "Excel";

    protected override object? MetaIndex(LuaTable table, object key) => GetSheet(key.ToString() ?? string.Empty);

    [LuaFunction]
    public ExcelSheetWrapper? GetSheet(string sheetName)
    {
        var rawType = typeof(Addon).Assembly.GetType($"Lumina.Excel.Sheets.{sheetName}", false, true);
        if (rawType == null) return null;

        MethodInfo? method;
        var isSubRow = false;
        if (rawType.IsAssignableTo(typeof(IExcelSubrow<>).MakeGenericType(rawType)))
        {
            method = typeof(IDataManager).GetMethod(nameof(IDataManager.GetSubrowExcelSheet))?.MakeGenericMethod(rawType);
            isSubRow = true;
        }
        else
        {
            method = typeof(IDataManager).GetMethod(nameof(IDataManager.GetExcelSheet))?.MakeGenericMethod(rawType);
        }

        var sheet = method?.Invoke(Svc.Data, [null, null]);
        return sheet == null ? null : new ExcelSheetWrapper(sheet, isSubRow);
    }

    [LuaFunction]
    public ExcelRowWrapper? GetRow(string sheetName, uint rowId)
    {
        return GetSheet(sheetName)?.GetRow(rowId);
    }

    [LuaFunction]
    public ExcelRowWrapper? GetSubRow(string sheetName, uint rowId, ushort subRowId)
    {
        return GetSheet(sheetName)?.GetSubRow(rowId, subRowId);
    }

    private static uint? GetRowIdFromObject(object obj)
    {
        var prop = obj.GetType().GetProperty("RowId");
        var value = prop?.GetValue(obj);
        return value is uint rowId ? rowId : null;
    }

    private static ExcelRowWrapper? GetRowRefValue(object rowRef)
    {
        var isValid = rowRef.GetType().GetProperty(nameof(RowRef<>.IsValid));
        if (isValid != null && isValid.GetValue(rowRef) is not true)
            return null;

        var property = rowRef.GetType().GetProperty(nameof(RowRef<>.Value));
        var value = property?.GetValue(rowRef);
        return value == null ? null : new ExcelRowWrapper(value, GetRowIdFromObject(rowRef) ?? 0);
    }

    private static bool IsCollection(Type? type)
    {
        var genericArg = type?.GenericTypeArguments.FirstOrDefault();
        if (genericArg == null) return false;
        return type?.IsAssignableTo(typeof(Collection<>).MakeGenericType(genericArg)) == true;
    }

    private static bool IsRowRef(Type? type)
    {
        var sheetType = GetGenericSheetType(type);
        return sheetType != null && type?.IsAssignableTo(typeof(RowRef<>).MakeGenericType(sheetType)) == true;
    }

    private static Type? GetGenericSheetType(Type? type)
    {
        var arg = type?.GenericTypeArguments.FirstOrDefault();
        return arg?.GetCustomAttribute<SheetAttribute>() != null ? arg : null;
    }

    public class ExcelSheetWrapper(object sheet, bool isSubrowSheet) : IWrapper
    {
        [LuaDocs]
        public object? this[uint rowId] => GetRow(rowId);

        [LuaDocs]
        public ExcelRowWrapper? GetRow(uint rowId)
        {
            if (isSubrowSheet)
                return null;
            var method = sheet.GetType().GetMethod(nameof(ExcelSheet<>.GetRowOrDefault));
            var row = method?.Invoke(sheet, [rowId]);
            return row == null ? null : new ExcelRowWrapper(row, rowId);
        }

        [LuaDocs]
        public ExcelRowWrapper? GetSubRow(uint rowId, ushort subRowId)
        {
            if (!isSubrowSheet)
                return null;
            var method = sheet.GetType().GetMethod(nameof(SubrowExcelSheet<>.GetSubrowOrDefault));
            var row = method?.Invoke(sheet, [rowId, subRowId]);
            return row == null ? null : new ExcelRowWrapper(row, rowId, subRowId);
        }

        public override string ToString()
        {
            return $"{(isSubrowSheet ? nameof(SubrowExcelSheet<>) : nameof(ExcelSheet<>))}<{GetGenericSheetType(sheet.GetType())?.Name}>";
        }
    }

    public class ExcelRowWrapper(object row, uint rowId, int subRowId = -1) : IWrapper
    {
        internal const BindingFlags PropertyFlags = BindingFlags.Public | BindingFlags.Instance |
                                                   BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly;

        // 指到別張表的欄位(RowRef<T>)會被包成另一個 ExcelRowWrapper,但包裝本身沒有把「它指到第幾列」
        // 露出來,所以 Lua 端拿得到那一列的欄位、卻永遠問不出它的 id —— 而「這個道具的
        // EquipSlotCategory / ItemUICategory 是不是某一組之一」這種判斷需要的正是 id。
        // 沒有它就只能改用該列的名稱字串比對,那會隨語言變。
        // ⚠️ 泛型的 RowRef<T> 與非泛型的 RowRef 走的是不同分支:非泛型那條本來就直接回傳 id,
        //    這裡補的是泛型那條。
        [LuaDocs(description: "The row id this wrapper refers to. Mainly useful on a wrapper obtained from a RowRef field, where it is the only way to tell which row the reference points at.")]
        public uint RowId => rowId;

        [LuaDocs] public object? this[string propertyName] => GetPropertyValue(row, propertyName);

        private static object? GetPropertyValue(object? obj, string propertyName)
        {
            var property = obj?.GetType().GetProperty(propertyName, PropertyFlags);
            if (property == null) return null;
            var rawValue = property.GetValue(obj);
            if (rawValue == null) return null;

            var type = rawValue.GetType();
            if (type.IsAssignableTo(typeof(RowRef)))
                return GetRowIdFromObject(rawValue);

            if (rawValue is ReadOnlySeString seString)
                return seString.ExtractText();

            if (IsRowRef(type))
                return GetRowRefValue(rawValue);

            if (IsCollection(type))
                return new ExcelCollectionWrapper(rawValue);

            return rawValue;
        }

        public override string ToString()
        {
            return $"{nameof(ExcelRowWrapper)}<{row.GetType().Name}>({rowId}{(subRowId >= 0 ? $",{subRowId}" : null)})";
        }
    }

    public class ExcelCollectionWrapper(object collection) : IWrapper
    {
        [LuaDocs] public object? this[int index] => GetValue(index);

        private object? GetValue(int index)
        {
            if (!CheckBounds(collection, index))
                return null;

            var indexer = GetIndexer(collection.GetType());
            var value = indexer?.GetValue(collection, [index]);
            if (value == null) return null;

            if (value is ReadOnlySeString seString)
                return seString.ExtractText();

            if (value.GetType().IsAssignableTo(typeof(RowRef)))
                return GetRowIdFromObject(value);

            if (IsRowRef(value.GetType()))
                return GetRowRefValue(value);

            return value;
        }

        public override string ToString()
        {
            return $"{nameof(Collection<>)}<{collection.GetType().GetGenericArguments().FirstOrDefault()?.Name}>";
        }

        private static bool CheckBounds(object collection, int index)
        {
            var prop = collection.GetType().GetProperty(nameof(Collection<>.Count), ExcelRowWrapper.PropertyFlags);
            if (prop == null) return false;
            if (prop.GetValue(collection) is not int count)
                return false;
            return index >= 0 && index < count;
        }

        private static PropertyInfo? GetIndexer(Type type)
            => type.GetProperty("Item", ExcelRowWrapper.PropertyFlags);
    }
}
