using System.Threading.Tasks;

namespace SomethingNeedDoing.Utils;

/// <summary>
/// 巨集出錯停下時的提示音（設定「錯誤提示音」與其頻率／持續時間／次數）。
/// </summary>
/// <remarks>
/// Console.Beep 會阻塞到響完，一律丟到執行緒池；頻率超出 37～32767 會擲例外，先夾住。
/// </remarks>
internal static class ErrorBeep
{
    public static void TryPlay()
    {
        if (!C.NoisyErrors) return;

        var count = C.BeepCount;
        var duration = C.BeepDuration;
        if (count <= 0 || duration <= 0) return;
        var frequency = Math.Clamp(C.BeepFrequency, 37, 32767);

        _ = Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < count; i++)
                    Console.Beep(frequency, duration);
            }
            catch (Exception ex)
            {
                FrameworkLogger.Warning($"[NoisyErrors] 播放提示音失敗：{ex.Message}");
            }
        });
    }
}
