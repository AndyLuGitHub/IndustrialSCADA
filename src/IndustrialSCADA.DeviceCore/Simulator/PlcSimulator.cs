using IndustrialSCADA.Core.Interfaces;
using IndustrialSCADA.Infrastructure.Communication.Simulator;
using Microsoft.Extensions.Logging;

namespace IndustrialSCADA.DeviceCore.Simulator;

/// <summary>
/// PLC 模拟器，在后台线程中按照预设波形周期性更新模拟寄存器值。
/// 生成以下模拟信号：
/// <list type="bullet">
///   <item>Temperature: 正弦波 (20-80 度C, 周期 30s)</item>
///   <item>Pressure: 三角波 (0-10 bar, 周期 20s)</item>
///   <item>MotorState: 方波 (bool, 周期 10s)</item>
///   <item>Counter: 递增计数</item>
/// </list>
/// </summary>
public sealed class PlcSimulator : IDisposable
{
    private readonly SimulatorProtocolAdapter _adapter;
    private readonly ILogger<PlcSimulator> _logger;

    private CancellationTokenSource? _cts;
    private Task? _simTask;

    /// <summary>模拟更新间隔（毫秒）。</summary>
    private const int UpdateIntervalMs = 200;

    /// <summary>
    /// 初始化 <see cref="PlcSimulator"/> 的新实例。
    /// </summary>
    /// <param name="adapter">模拟器协议适配器（来自 Infrastructure 层的单例）。</param>
    /// <param name="logger">日志记录器。</param>
    public PlcSimulator(SimulatorProtocolAdapter adapter, ILogger<PlcSimulator> logger)
    {
        _adapter = adapter;
        _logger = logger;
    }

    /// <summary>
    /// 启动模拟器后台更新任务。
    /// </summary>
    public void Start()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _logger.LogWarning("PLC 模拟器已在运行");
            return;
        }

        _cts = new CancellationTokenSource();
        _simTask = SimulateLoopAsync(_cts.Token);
        _logger.LogInformation("PLC 模拟器已启动");
    }

    /// <summary>
    /// 停止模拟器后台更新任务。
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts == null) return;

        _cts.Cancel();

        if (_simTask != null)
        {
            try
            {
                await _simTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 预期的取消异常
            }
        }

        _cts.Dispose();
        _cts = null;
        _simTask = null;
        _logger.LogInformation("PLC 模拟器已停止");
    }

    /// <summary>
    /// 模拟循环，按预设波形更新各寄存器值。
    /// </summary>
    private async Task SimulateLoopAsync(CancellationToken ct)
    {
        var counter = 0L;
        var startTime = DateTime.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;

            // Temperature: 正弦波 20-80 degC, 周期 30s
            var temperature = 50.0 + 30.0 * Math.Sin(2.0 * Math.PI * elapsed / 30.0);
            _adapter.SetRegister("Temperature", Math.Round(temperature, 2));

            // Pressure: 三角波 0-10 bar, 周期 20s
            var period = 20.0;
            var phase = (elapsed % period) / period; // 0..1
            var pressure = phase < 0.5
                ? 10.0 * (phase * 2.0)
                : 10.0 * (2.0 - phase * 2.0);
            _adapter.SetRegister("Pressure", Math.Round(pressure, 2));

            // MotorState: 方波 bool, 周期 10s
            var motorState = (long)(elapsed / 5.0) % 2 == 0;
            _adapter.SetRegister("MotorState", motorState);

            // Counter: 递增
            _adapter.SetRegister("Counter", Interlocked.Increment(ref counter));

            try
            {
                await Task.Delay(UpdateIntervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// 释放资源，停止模拟。
    /// </summary>
    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
