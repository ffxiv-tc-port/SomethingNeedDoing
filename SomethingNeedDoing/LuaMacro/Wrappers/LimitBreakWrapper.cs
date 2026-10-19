using FFXIVClientStructs.FFXIV.Client.Game.UI;
using SomethingNeedDoing.Core.Interfaces;

namespace SomethingNeedDoing.LuaMacro.Wrappers;
public unsafe class LimitBreakWrapper : IWrapper
{
    // UIState 位址沒解出(改版後特徵碼對不上)時回「量表是空的、而滿格是一個到不了的數字」:
    // CurrentUnits 0、BarUnits 與 BarCount 取型別最大值 ⇒ 任何「極限爆發集滿了嗎」的寫法都不成立。
    [LuaDocs] public ushort CurrentUnits { get { var ui = GameStatics.TryUIState(); return ui == null ? (ushort)0 : ui->LimitBreakController.CurrentUnits; } }
    [LuaDocs] public uint BarUnits { get { var ui = GameStatics.TryUIState(); return ui == null ? uint.MaxValue : ui->LimitBreakController.BarUnits; } }
    [LuaDocs] public byte BarCount { get { var ui = GameStatics.TryUIState(); return ui == null ? byte.MaxValue : ui->LimitBreakController.BarCount; } }
}
