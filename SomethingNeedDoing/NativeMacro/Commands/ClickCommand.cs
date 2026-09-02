using ECommons.Reflection;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace SomethingNeedDoing.NativeMacro.Commands;
/// <summary>
/// Clicks UI elements in game addons.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ClickCommand"/> class.
/// </remarks>
[GenericDoc(
    "Click a pre-defined button in an addon or window.",
    ["addonName", "methodName", "values"],
    ["/click SelectYesNo_Yes", "/click SelectString[0].Select <errorif.addonnotfound>"]
)]
public class ClickCommand(string text, string addonName, string methodName, string[] values) : MacroCommandBase(text)
{
    /// <inheritdoc/>
    public override bool RequiresFrameworkThread => true;

    /// <inheritdoc/>
    public override async Task Execute(MacroContext context, CancellationToken token)
    {
        await context.RunOnFramework(() =>
        {
            unsafe
            {
                if (!TryGetAddonByName<AtkUnitBase>(addonName, out var addon))
                    throw new MacroException($"Addon {addonName} not found");

                // 與 /callback 同一道閘門(CallbackCommand 用的也是 GenericHelpers.IsAddonReady):
                // 不 ready 就整段跳過而不擲例外 —— 巨集指令之間沒有預設等待,擲例外會在視窗還沒開的
                // 那一瞬間把整支巨集中止。
                //
                // 這道閘門裡真正證得出東西的是 IsVisible,而它的方向是不對稱的:
                //   IsVisible == false ⇒ 這扇窗必定已經被 Close 或 Hide 過(Hide 會同步清掉該位元,
                //                        而 Close 只有在「本來就不可見」時才跳過 Hide)⇒ 不要按。
                //   IsVisible == true  ⇒ 證明不了它還開著。
                // ⇒ 這是硬擋,不是安全保證:「按到正在關閉中的窗」仍然擋不掉。
                if (!IsAddonReady(addon))
                {
                    FrameworkLogger.Info($"Skipping click on {addonName}: addon is not ready (hidden, closing or still loading)");
                    return;
                }

                var type = typeof(AddonMaster).GetNestedType(addonName) ?? throw new NullReferenceException($"Type {addonName} not found");
                var m = Activator.CreateInstance(type, [(nint)addon]) ?? throw new InvalidOperationException($"Could not create instance of type {type}");
                if (methodName.Contains('.'))
                {
                    var splitMethod = methodName.Split('.');
                    var subElement = splitMethod[0];
                    if (subElement.EndsWith(']'))
                    {
                        var index = int.Parse(subElement[(subElement.IndexOf('[') + 1)..^1]);
                        FrameworkLogger.Verbose($"Index: {index}");
                        subElement = subElement[..subElement.IndexOf('[')];
                        FrameworkLogger.Verbose($"SubElement: {subElement}");
                        var element = m.GetFoP<System.Collections.IEnumerable>(subElement).GetEnumerator();
                        for (var i = 0; i <= index; i++)
                            element.MoveNext();
                        m = element.Current;
                    }
                    else
                        m = m.GetFoP(splitMethod[0]);

                    methodName = splitMethod[1];
                }
                if (m.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).TryGetFirst(x => x.Name == methodName && x.GetParameters().Length == values.Length, out var methodInfo))
                {
                    var methodParams = new object[values.Length];
                    for (var i = 0; i < values.Length; i++)
                    {
                        var input = values[i];
                        var param = methodInfo.GetParameters()[i];
                        if (param.ParameterType == input.GetType())
                            methodParams[i] = input;
                        else
                        {
                            var parseMethod = param.ParameterType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [input.GetType()]) ?? throw new InvalidOperationException($"Could not find parse method for {input} ({param.ParameterType}) [{i}]");
                            var parsed = parseMethod.Invoke(null, [input]) ?? throw new NullReferenceException($"Failed to parse {input} with {parseMethod.Name}");
                            methodParams[i] = parsed;
                        }
                    }
                    methodInfo.Invoke(m, methodParams);
                }
                else
                    throw new InvalidOperationException($"Could not find method {methodName} with {values.Length} arguments for {addonName} ");
            }
        });

        await PerformWait(token);
    }
}
