using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using IndustrialSCADA.Core.Enums;
using IndustrialSCADA.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IndustrialSCADA.Infrastructure.Communication.IEC104;

/// <summary>
/// IEC 60870-5-104 protocol adapter for power system SCADA communication over TCP.
/// Implements APCI frame handling (I-frame, S-frame, U-frame), ASDU parsing,
/// send/receive sequence number management, and a background receive loop.
/// </summary>
public sealed class IEC104ProtocolAdapter : ProtocolAdapterBase, IDisposable
{
    // ──────────────────────────── Constants ────────────────────────────

    /// <summary>APCI start byte.</summary>
    private const byte StartByte = 0x68;

    /// <summary>Default TCP port for IEC 104.</summary>
    private const int DefaultPort = 2404;

    /// <summary>Default timeout in milliseconds.</summary>
    private const int DefaultTimeoutMs = 5000;

    /// <summary>Control field length in bytes.</summary>
    private const int ControlFieldSize = 4;

    /// <summary>Maximum sequence number (15-bit counter, 0..32767).</summary>
    private const int MaxSequenceNumber = 32768;

    /// <summary>Maximum APCI payload length (control field + ASDU).</summary>
    private const int MaxFrameLength = 253;

    // U-frame control byte 1 identifiers
    private const byte UStartDtAct = 0x07;
    private const byte UStartDtCon = 0x0B;
    private const byte UStopDtAct = 0x13;
    private const byte UStopDtCon = 0x23;
    private const byte UTestFrAct = 0x43;
    private const byte UTestFrCon = 0x83;

    // Common ASDU TypeIDs
    private const byte TypeMSpNa1 = 1;   // Single-point information
    private const byte TypeMDpNa1 = 3;   // Double-point information
    private const byte TypeMMeNb1 = 11;  // Measured value, scaled value
    private const byte TypeMMeNc1 = 13;  // Measured value, short floating point
    private const byte TypeCScNa1 = 45;  // Single command
    private const byte TypeCIcNa1 = 100; // Interrogation command

    // ──────────────────────────── Fields ────────────────────────────

    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);

    /// <summary>Cache of most-recently received values, keyed by IOA.</summary>
    private readonly ConcurrentDictionary<int, object> _valueCache = new();

    /// <summary>Pending read requests waiting for a specific IOA value.</summary>
    private readonly ConcurrentDictionary<int, TaskCompletionSource<object>> _pendingReads = new();

    /// <summary>Send sequence number V(S), range 0..32767.</summary>
    private int _sendSequence;

    /// <summary>Receive sequence number V(R), range 0..32767.</summary>
    private int _receiveSequence;

    /// <summary>Common address of ASDU (station address).</summary>
    private ushort _stationAddress = 0x0001;

    /// <summary>TCS completed when a U-frame response arrives (e.g., STARTDT con).</summary>
    private volatile TaskCompletionSource<byte>? _uFrameResponseTcs;

    /// <summary>TCS completed when a command acknowledgment I-frame arrives.</summary>
    private volatile TaskCompletionSource<(bool success, int ioa)>? _writeAckTcs;

    private bool _disposed;

    // ──────────────────────────── Constructor ────────────────────────────

    /// <summary>
    /// Initializes a new instance of <see cref="IEC104ProtocolAdapter"/>.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public IEC104ProtocolAdapter(ILogger<IEC104ProtocolAdapter> logger)
        : base(logger)
    {
    }

    // ──────────────────────────── Properties ────────────────────────────

    /// <inheritdoc />
    public override string ProtocolName => "IEC 104";

    /// <inheritdoc />
    public override Core.Enums.ProtocolType ProtocolType => Core.Enums.ProtocolType.IEC104;

    // ──────────────────────────── Connect / Disconnect ────────────────────────────

    /// <summary>
    /// Establishes a TCP connection, starts the background receive loop,
    /// sends STARTDT act, and waits for STARTDT con.
    /// </summary>
    protected override async Task ConnectCoreAsync(ConnectionConfig config, CancellationToken ct)
    {
        var host = config.Host;
        var port = config.Port > 0 ? config.Port : DefaultPort;
        var timeout = config.Timeout > 0 ? config.Timeout : DefaultTimeoutMs;

        // Read optional station address from parameters
        if (config.Parameters.TryGetValue("StationAddress", out var addrObj) &&
            addrObj is IConvertible convertible)
        {
            _stationAddress = Convert.ToUInt16(convertible);
        }

        Logger.LogDebug("[IEC 104] Connecting to {Host}:{Port} (station address={Station})",
            host, port, _stationAddress);

        // 1. TCP connect
        _tcpClient = new TcpClient();
        _tcpClient.ReceiveTimeout = timeout;
        _tcpClient.SendTimeout = timeout;

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(timeout);

        try
        {
            await _tcpClient.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _tcpClient.Dispose();
            _tcpClient = null;
            throw new TimeoutException(
                $"TCP connection to {host}:{port} timed out after {timeout}ms.");
        }

        _stream = _tcpClient.GetStream();

        // 2. Start background receive loop
        _receiveCts = new CancellationTokenSource();
        _receiveTask = Task.Run(
            () => ReceiveLoopAsync(_receiveCts.Token),
            _receiveCts.Token);

        // 3. Send STARTDT act and wait for STARTDT con
        _uFrameResponseTcs = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);

        await SendUFrameAsync(UStartDtAct).ConfigureAwait(false);
        Logger.LogDebug("[IEC 104] Sent STARTDT act, waiting for STARTDT con");

        using var startdtCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        startdtCts.CancelAfter(timeout);

        var completedTask = await Task.WhenAny(
            _uFrameResponseTcs.Task,
            Task.Delay(Timeout.Infinite, startdtCts.Token)).ConfigureAwait(false);

        if (completedTask != _uFrameResponseTcs.Task)
        {
            await CleanupConnectionAsync().ConfigureAwait(false);
            throw new TimeoutException("Did not receive STARTDT con within timeout.");
        }

        var responseCtrl = await _uFrameResponseTcs.Task.ConfigureAwait(false);
        if (responseCtrl != UStartDtCon)
        {
            await CleanupConnectionAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Expected STARTDT con (0x{UStartDtCon:X2}) but received 0x{responseCtrl:X2}.");
        }

        Logger.LogInformation("[IEC 104] STARTDT con received, data transfer started");
    }

    /// <summary>
    /// Sends STOPDT act, cancels the receive loop, and closes the TCP connection.
    /// </summary>
    protected override async Task DisconnectCoreAsync()
    {
        Logger.LogDebug("[IEC 104] Disconnecting");

        // Best-effort STOPDT act
        try
        {
            if (_stream is { CanWrite: true })
            {
                await SendUFrameAsync(UStopDtAct).ConfigureAwait(false);
                Logger.LogDebug("[IEC 104] Sent STOPDT act");
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "[IEC 104] Failed to send STOPDT act (non-critical)");
        }

        await CleanupConnectionAsync().ConfigureAwait(false);
    }

    // ──────────────────────────── Read ────────────────────────────

    /// <summary>
    /// Reads a value from the remote station by sending a station interrogation
    /// (C_IC_NA_1, QOI=20) and waiting for the requested IOA to appear in the cache.
    /// Address format: "IOA" or "IOA:TYPE" (e.g., "100" or "100:M_SP_NA_1").
    /// </summary>
    protected override async Task<T> ReadCoreAsync<T>(string address, CancellationToken ct)
    {
        EnsureConnected();

        int ioa = ParseAddressIoa(address);
        var timeout = GetTimeoutMs();

        Logger.LogDebug("[IEC 104] Read request for IOA {IOA} (address={Address})", ioa, address);

        // Register a pending read so the receive loop can satisfy it immediately
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingReads[ioa] = tcs;

        try
        {
            // Send station interrogation C_IC_NA_1
            var asdu = BuildInterrogationAsdu();
            await SendIFrameAsync(asdu).ConfigureAwait(false);

            // Wait for the value with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            var completedTask = await Task.WhenAny(
                tcs.Task,
                Task.Delay(Timeout.Infinite, timeoutCts.Token)).ConfigureAwait(false);

            if (completedTask != tcs.Task)
            {
                throw new TimeoutException(
                    $"Timed out waiting for IOA {ioa} after {timeout}ms.");
            }

            var rawValue = await tcs.Task.ConfigureAwait(false);
            return ConvertValue<T>(rawValue);
        }
        finally
        {
            _pendingReads.TryRemove(ioa, out _);
        }
    }

    // ──────────────────────────── Write ────────────────────────────

    /// <summary>
    /// Writes a single command (C_SC_NA_1, TypeID=45) to the remote station.
    /// Address format: "IOA" or "IOA:CMD". Value should be <c>bool</c> or convertible to bool.
    /// </summary>
    protected override async Task WriteCoreAsync<T>(string address, T value, CancellationToken ct)
    {
        EnsureConnected();

        int ioa = ParseAddressIoa(address);
        var timeout = GetTimeoutMs();
        bool commandValue = Convert.ToBoolean(value);

        Logger.LogDebug("[IEC 104] Write command to IOA {IOA}, value={Value}", ioa, commandValue);

        // Prepare acknowledgment TCS
        _writeAckTcs = new TaskCompletionSource<(bool, int)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Build and send C_SC_NA_1
        var asdu = BuildSingleCommandAsdu(ioa, commandValue);
        await SendIFrameAsync(asdu).ConfigureAwait(false);

        // Wait for acknowledgment
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        var completedTask = await Task.WhenAny(
            _writeAckTcs.Task,
            Task.Delay(Timeout.Infinite, timeoutCts.Token)).ConfigureAwait(false);

        if (completedTask != _writeAckTcs.Task)
        {
            _writeAckTcs = null;
            throw new TimeoutException(
                $"Timed out waiting for command acknowledgment on IOA {ioa} after {timeout}ms.");
        }

        var (success, ackIoa) = await _writeAckTcs.Task.ConfigureAwait(false);
        _writeAckTcs = null;

        if (!success)
        {
            throw new InvalidOperationException(
                $"Command to IOA {ioa} was not activated by the remote station.");
        }

        Logger.LogDebug("[IEC 104] Command to IOA {IOA} acknowledged successfully", ioa);
    }

    // ──────────────────────────── Background Receive Loop ────────────────────────────

    /// <summary>
    /// Continuously reads APCI frames from the network stream and dispatches them.
    /// Runs as a background task for the lifetime of the connection.
    /// </summary>
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        Logger.LogDebug("[IEC 104] Receive loop started");

        try
        {
            while (!ct.IsCancellationRequested && _stream is { CanRead: true })
            {
                // Read start byte — may block until data arrives
                byte startByte;
                try
                {
                    startByte = await ReadSingleByteAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (EndOfStreamException)
                {
                    Logger.LogWarning("[IEC 104] Connection closed by remote end");
                    break;
                }

                if (startByte != StartByte)
                {
                    Logger.LogWarning("[IEC 104] Discarding unexpected byte 0x{Byte:X2} (expected 0x68)",
                        startByte);
                    continue;
                }

                // Read length
                byte length = await ReadSingleByteAsync(ct).ConfigureAwait(false);
                if (length < ControlFieldSize || length > MaxFrameLength)
                {
                    Logger.LogWarning("[IEC 104] Invalid frame length {Length}, skipping", length);
                    continue;
                }

                // Read the payload (control field + optional ASDU)
                byte[] payload = await ReadExactBytesAsync(length, ct).ConfigureAwait(false);

                // Dispatch based on control field
                byte ctrl1 = payload[0];
                try
                {
                    if ((ctrl1 & 0x01) == 0)
                    {
                        // I-frame: bit 0 of ctrl1 == 0
                        HandleIFrame(payload);
                    }
                    else if ((ctrl1 & 0x03) == 0x01)
                    {
                        // S-frame: bits [1:0] == 01
                        HandleSFrame(payload);
                    }
                    else if ((ctrl1 & 0x03) == 0x03)
                    {
                        // U-frame: bits [1:0] == 11
                        HandleUFrame(payload);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "[IEC 104] Error processing frame (ctrl1=0x{Ctrl:X2})", ctrl1);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "[IEC 104] Receive loop terminated unexpectedly");
        }

        _isConnected = false;
        Logger.LogDebug("[IEC 104] Receive loop ended");
    }

    // ──────────────────────────── Frame Handlers ────────────────────────────

    /// <summary>
    /// Handles an incoming I-frame: extracts sequence numbers, sends S-frame ack,
    /// and parses the ASDU payload.
    /// </summary>
    private void HandleIFrame(byte[] payload)
    {
        // Decode V(S) and V(R) from control field
        int remoteVs = ((payload[0] & 0xFE) >> 1) | (payload[1] << 7);
        int remoteVr = ((payload[2] & 0xFE) >> 1) | (payload[3] << 7);

        Logger.LogTrace("[IEC 104] I-frame received: V(S)={Vs}, V(R)={Vr}", remoteVs, remoteVr);

        // Update our receive sequence counter
        _receiveSequence = (remoteVs + 1) % MaxSequenceNumber;

        // Send S-frame to acknowledge receipt
        _ = Task.Run(async () =>
        {
            try
            {
                await SendSFrameAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[IEC 104] Failed to send S-frame acknowledgment");
            }
        });

        // Parse ASDU if present (payload bytes after the 4-byte control field)
        if (payload.Length > ControlFieldSize)
        {
            byte[] asdu = new byte[payload.Length - ControlFieldSize];
            Buffer.BlockCopy(payload, ControlFieldSize, asdu, 0, asdu.Length);
            ParseAsdu(asdu);
        }
    }

    /// <summary>
    /// Handles an incoming S-frame: updates our send sequence tracking.
    /// </summary>
    private void HandleSFrame(byte[] payload)
    {
        int remoteVr = ((payload[2] & 0xFE) >> 1) | (payload[3] << 7);
        Logger.LogTrace("[IEC 104] S-frame received: N(R)={Vr}", remoteVr);
        // The remote end acknowledges receipt up to V(R)-1.
        // In a full implementation we would retire acknowledged I-frames from a send buffer.
    }

    /// <summary>
    /// Handles an incoming U-frame: responds to TESTFR act, signals STARTDT/STOPDT con.
    /// </summary>
    private void HandleUFrame(byte[] payload)
    {
        byte ctrl1 = payload[0];

        switch (ctrl1)
        {
            case UStartDtCon:
                Logger.LogDebug("[IEC 104] Received STARTDT con");
                _uFrameResponseTcs?.TrySetResult(ctrl1);
                break;

            case UStopDtCon:
                Logger.LogDebug("[IEC 104] Received STOPDT con");
                _uFrameResponseTcs?.TrySetResult(ctrl1);
                break;

            case UTestFrAct:
                Logger.LogDebug("[IEC 104] Received TESTFR act, sending TESTFR con");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SendUFrameAsync(UTestFrCon).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "[IEC 104] Failed to send TESTFR con");
                    }
                });
                break;

            case UTestFrCon:
                Logger.LogDebug("[IEC 104] Received TESTFR con");
                _uFrameResponseTcs?.TrySetResult(ctrl1);
                break;

            default:
                Logger.LogDebug("[IEC 104] Unhandled U-frame ctrl1=0x{Ctrl:X2}", ctrl1);
                break;
        }
    }

    // ──────────────────────────── ASDU Parser ────────────────────────────

    /// <summary>
    /// Parses an ASDU (Application Service Data Unit), extracting IOA values
    /// and storing them in the value cache. Also handles command and interrogation
    /// acknowledgments.
    /// </summary>
    private void ParseAsdu(byte[] asdu)
    {
        if (asdu.Length < 6)
        {
            Logger.LogWarning("[IEC 104] ASDU too short ({Length} bytes)", asdu.Length);
            return;
        }

        byte typeId = asdu[0];
        byte vsq = asdu[1];
        bool sq = (vsq & 0x80) != 0;          // SQ bit: 1 = sequential addresses
        int numberOfObjects = vsq & 0x7F;

        // Cause of transmission (bits 5..0 of byte 2)
        byte cot = (byte)(asdu[2] & 0x3F);

        // Originator address
        // byte originator = asdu[3];

        // Common address of ASDU (little-endian)
        // ushort remoteStation = (ushort)(asdu[4] | (asdu[5] << 8));

        Logger.LogTrace("[IEC 104] ASDU: TypeID={TypeId}, VSQ=0x{Vsq:X2}, Objects={Count}, COT={Cot}",
            typeId, vsq, numberOfObjects, cot);

        // Handle command acknowledgment
        if (typeId == TypeCScNa1)
        {
            HandleCommandAcknowledgment(asdu, cot);
            return;
        }

        // Handle interrogation confirmation
        if (typeId == TypeCIcNa1)
        {
            Logger.LogDebug("[IEC 104] Interrogation confirmation received, COT={Cot}", cot);
            return;
        }

        // Parse information objects
        int offset = 6;
        int currentIoa = 0;

        for (int i = 0; i < numberOfObjects; i++)
        {
            if (offset + 3 > asdu.Length) break;

            // Read IOA (3 bytes, little-endian) when SQ=0 or for the first object
            if (!sq || i == 0)
            {
                currentIoa = asdu[offset] | (asdu[offset + 1] << 8) | (asdu[offset + 2] << 16);
                offset += 3;
            }
            else
            {
                currentIoa++;
            }

            int ioa = currentIoa;

            switch (typeId)
            {
                case TypeMSpNa1: // M_SP_NA_1: single-point (SIQ, 1 byte)
                {
                    if (offset >= asdu.Length) return;
                    bool value = (asdu[offset] & 0x01) != 0;
                    StoreValue(ioa, value);
                    offset += 1;
                    break;
                }

                case TypeMDpNa1: // M_DP_NA_1: double-point (DIQ, 1 byte)
                {
                    if (offset >= asdu.Length) return;
                    int value = asdu[offset] & 0x03;
                    StoreValue(ioa, value);
                    offset += 1;
                    break;
                }

                case TypeMMeNb1: // M_ME_NB_1: scaled value (2 bytes signed + QDS 1 byte)
                {
                    if (offset + 2 >= asdu.Length) return;
                    short value = (short)(asdu[offset] | (asdu[offset + 1] << 8));
                    StoreValue(ioa, value);
                    offset += 3; // 2 value + 1 QDS
                    break;
                }

                case TypeMMeNc1: // M_ME_NC_1: short floating point (4 bytes float + QDS 1 byte)
                {
                    if (offset + 4 >= asdu.Length) return;
                    float value = BitConverter.ToSingle(asdu, offset);
                    StoreValue(ioa, value);
                    offset += 5; // 4 float + 1 QDS
                    break;
                }

                default:
                    Logger.LogDebug("[IEC 104] Unsupported TypeID {TypeId}, stopping ASDU parse", typeId);
                    return;
            }
        }
    }

    /// <summary>
    /// Stores a parsed value in the cache and satisfies any pending read request for the IOA.
    /// </summary>
    private void StoreValue(int ioa, object value)
    {
        _valueCache[ioa] = value;

        if (_pendingReads.TryRemove(ioa, out var tcs))
        {
            tcs.TrySetResult(value);
        }

        Logger.LogTrace("[IEC 104] Cached IOA {IOA} = {Value} ({Type})",
            ioa, value, value.GetType().Name);
    }

    /// <summary>
    /// Processes a C_SC_NA_1 command acknowledgment and signals the write TCS.
    /// </summary>
    private void HandleCommandAcknowledgment(byte[] asdu, byte cot)
    {
        if (asdu.Length < 10) return;

        int ioa = asdu[6] | (asdu[7] << 8) | (asdu[8] << 16);

        // COT 7 = activation confirmation, COT 8 = deactivation confirmation
        bool success = cot is 7 or 8;

        Logger.LogDebug("[IEC 104] Command ack for IOA {IOA}: COT={Cot}, success={Success}",
            ioa, cot, success);

        _writeAckTcs?.TrySetResult((success, ioa));
    }

    // ──────────────────────────── Frame Builders ────────────────────────────

    /// <summary>
    /// Builds a U-frame with the given control byte 1.
    /// Structure: [0x68][0x04][ctrl1][0x00][0x00][0x00]
    /// </summary>
    private static byte[] BuildUFrame(byte controlByte1)
    {
        return [StartByte, 0x04, controlByte1, 0x00, 0x00, 0x00];
    }

    /// <summary>
    /// Builds an S-frame acknowledging received I-frames up to V(R).
    /// Structure: [0x68][0x04][0x01][0x00][V(R) low][V(R) high]
    /// </summary>
    private byte[] BuildSFrame()
    {
        int vr = _receiveSequence;
        byte ctrl3 = (byte)((vr << 1) & 0xFE);
        byte ctrl4 = (byte)((vr >> 7) & 0xFF);
        return [StartByte, 0x04, 0x01, 0x00, ctrl3, ctrl4];
    }

    /// <summary>
    /// Builds an I-frame with the current send sequence number and the given ASDU.
    /// Structure: [0x68][length][V(S) ctrl][V(R) ctrl][ASDU...]
    /// </summary>
    private byte[] BuildIFrame(byte[] asdu)
    {
        int vs = _sendSequence;
        int vr = _receiveSequence;

        byte ctrl1 = (byte)((vs << 1) & 0xFE);  // bit 0 = 0 marks I-frame
        byte ctrl2 = (byte)((vs >> 7) & 0xFF);
        byte ctrl3 = (byte)((vr << 1) & 0xFE);
        byte ctrl4 = (byte)((vr >> 7) & 0xFF);

        byte length = (byte)(ControlFieldSize + asdu.Length);

        byte[] frame = new byte[2 + length]; // start + length + payload
        frame[0] = StartByte;
        frame[1] = length;
        frame[2] = ctrl1;
        frame[3] = ctrl2;
        frame[4] = ctrl3;
        frame[5] = ctrl4;
        Buffer.BlockCopy(asdu, 0, frame, 6, asdu.Length);

        // Advance send sequence
        _sendSequence = (_sendSequence + 1) % MaxSequenceNumber;

        return frame;
    }

    /// <summary>
    /// Builds the ASDU for a station interrogation command (C_IC_NA_1, TypeID=100, QOI=20).
    /// </summary>
    private byte[] BuildInterrogationAsdu()
    {
        byte[] asdu = new byte[10];
        asdu[0] = TypeCIcNa1;              // TypeID = 100
        asdu[1] = 0x01;                    // VSQ: SQ=0, number of objects = 1
        asdu[2] = 0x06;                    // COT: 6 = activation (station interrogation)
        asdu[3] = 0x00;                    // Originator address
        asdu[4] = (byte)(_stationAddress & 0xFF);
        asdu[5] = (byte)((_stationAddress >> 8) & 0xFF);
        asdu[6] = 0x00;                    // IOA byte 0 (0 for interrogation)
        asdu[7] = 0x00;                    // IOA byte 1
        asdu[8] = 0x00;                    // IOA byte 2
        asdu[9] = 0x14;                    // QOI = 20 (station interrogation)
        return asdu;
    }

    /// <summary>
    /// Builds the ASDU for a single command (C_SC_NA_1, TypeID=45).
    /// </summary>
    /// <param name="ioa">Information Object Address.</param>
    /// <param name="commandValue">Command state (true=on, false=off).</param>
    private byte[] BuildSingleCommandAsdu(int ioa, bool commandValue)
    {
        byte[] asdu = new byte[10];
        asdu[0] = TypeCScNa1;              // TypeID = 45
        asdu[1] = 0x01;                    // VSQ: SQ=0, number of objects = 1
        asdu[2] = 0x06;                    // COT: 6 = activation
        asdu[3] = 0x00;                    // Originator address
        asdu[4] = (byte)(_stationAddress & 0xFF);
        asdu[5] = (byte)((_stationAddress >> 8) & 0xFF);
        asdu[6] = (byte)(ioa & 0xFF);      // IOA byte 0
        asdu[7] = (byte)((ioa >> 8) & 0xFF);
        asdu[8] = (byte)((ioa >> 16) & 0xFF);
        // SCO: bit 0 = command state, bits 5..7 = 000 (no additional definition),
        //      bit 4 = S/E (0=execute, 1=select). We use execute mode.
        asdu[9] = (byte)(commandValue ? 0x01 : 0x00);
        return asdu;
    }

    // ──────────────────────────── Send Helpers ────────────────────────────

    /// <summary>
    /// Sends a U-frame (unnumbered control frame) to the remote station.
    /// </summary>
    private async Task SendUFrameAsync(byte controlByte1)
    {
        var frame = BuildUFrame(controlByte1);
        await SendRawFrameAsync(frame).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends an S-frame to acknowledge received I-frames.
    /// </summary>
    private async Task SendSFrameAsync()
    {
        var frame = BuildSFrame();
        await SendRawFrameAsync(frame).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds and sends an I-frame containing the given ASDU.
    /// </summary>
    private async Task SendIFrameAsync(byte[] asdu)
    {
        var frame = BuildIFrame(asdu);
        await SendRawFrameAsync(frame).ConfigureAwait(false);
    }

    /// <summary>
    /// Thread-safe write of a raw byte frame to the network stream.
    /// Uses a semaphore to serialize concurrent send operations.
    /// </summary>
    private async Task SendRawFrameAsync(byte[] frame)
    {
        if (_stream is null || !_stream.CanWrite)
        {
            throw new InvalidOperationException("[IEC 104] Network stream is not available.");
        }

        await _sendSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(frame).ConfigureAwait(false);
            await _stream.FlushAsync().ConfigureAwait(false);

            Logger.LogTrace("[IEC 104] Sent {Length} bytes: {Hex}",
                frame.Length, BitConverter.ToString(frame));
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }

    // ──────────────────────────── Stream Read Helpers ────────────────────────────

    /// <summary>
    /// Reads a single byte from the network stream.
    /// Throws <see cref="EndOfStreamException"/> if the connection is closed.
    /// </summary>
    private async Task<byte> ReadSingleByteAsync(CancellationToken ct)
    {
        byte[] buffer = new byte[1];
        int read = await _stream!.ReadAsync(buffer, ct).ConfigureAwait(false);
        if (read == 0)
        {
            throw new EndOfStreamException("[IEC 104] Connection closed by remote host.");
        }
        return buffer[0];
    }

    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes from the network stream.
    /// Handles partial reads by looping until all bytes are received.
    /// </summary>
    private async Task<byte[]> ReadExactBytesAsync(int count, CancellationToken ct)
    {
        byte[] buffer = new byte[count];
        int totalRead = 0;

        while (totalRead < count)
        {
            int read = await _stream!.ReadAsync(
                buffer.AsMemory(totalRead, count - totalRead), ct).ConfigureAwait(false);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    $"[IEC 104] Connection closed while reading (got {totalRead}/{count} bytes).");
            }

            totalRead += read;
        }

        return buffer;
    }

    // ──────────────────────────── Address & Value Helpers ────────────────────────────

    /// <summary>
    /// Parses the IOA (Information Object Address) from an address string.
    /// Supports formats: "IOA" or "IOA:TYPE" (e.g., "100" or "100:M_SP_NA_1").
    /// </summary>
    private static int ParseAddressIoa(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("Address cannot be null or empty.", nameof(address));
        }

        var colonIndex = address.IndexOf(':');
        var ioaPart = colonIndex >= 0 ? address[..colonIndex] : address;

        if (!int.TryParse(ioaPart, out int ioa) || ioa < 0 || ioa > 0xFFFFFF)
        {
            throw new ArgumentException(
                $"Invalid IOA in address '{address}'. Expected a non-negative integer (0..16777215).",
                nameof(address));
        }

        return ioa;
    }

    /// <summary>
    /// Returns the configured timeout in milliseconds.
    /// </summary>
    private int GetTimeoutMs()
    {
        return _config?.Timeout > 0 ? _config.Timeout : DefaultTimeoutMs;
    }

    /// <summary>
    /// Converts a cached value to the requested type <typeparamref name="T"/>.
    /// </summary>
    private static T ConvertValue<T>(object value)
    {
        if (value is T typed)
        {
            return typed;
        }

        // Handle common numeric conversions
        try
        {
            if (typeof(T) == typeof(bool))
            {
                return (T)(object)(Convert.ToInt32(value) != 0);
            }

            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch (Exception ex)
        {
            throw new InvalidCastException(
                $"Cannot convert cached value of type {value.GetType().Name} to {typeof(T).Name}.", ex);
        }
    }

    /// <summary>
    /// Throws if the adapter is not connected.
    /// </summary>
    private void EnsureConnected()
    {
        if (!_isConnected || _stream is null)
        {
            throw new InvalidOperationException("[IEC 104] Not connected. Call ConnectAsync first.");
        }
    }

    // ──────────────────────────── Cleanup ────────────────────────────

    /// <summary>
    /// Cancels the receive loop and disposes TCP resources.
    /// </summary>
    private async Task CleanupConnectionAsync()
    {
        // Cancel receive loop
        if (_receiveCts is not null)
        {
            await _receiveCts.CancelAsync().ConfigureAwait(false);
        }

        // Wait briefly for the receive task to finish
        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch
            {
                // Timeout or cancellation — acceptable during cleanup
            }
        }

        // Dispose stream and client
        try { _stream?.Dispose(); } catch { /* best effort */ }
        try { _tcpClient?.Dispose(); } catch { /* best effort */ }

        _stream = null;
        _tcpClient = null;
        _receiveCts = null;
        _receiveTask = null;

        // Fail any pending operations
        _uFrameResponseTcs?.TrySetCanceled();
        _writeAckTcs?.TrySetCanceled();

        foreach (var kvp in _pendingReads)
        {
            kvp.Value.TrySetCanceled();
        }
        _pendingReads.Clear();

        // Reset sequence counters
        _sendSequence = 0;
        _receiveSequence = 0;
    }

    // ──────────────────────────── IDisposable ────────────────────────────

    /// <summary>
    /// Releases all resources used by the adapter.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _receiveCts?.Cancel();

        try { _stream?.Dispose(); } catch { /* best effort */ }
        try { _tcpClient?.Dispose(); } catch { /* best effort */ }
        _sendSemaphore.Dispose();
        _receiveCts?.Dispose();

        _uFrameResponseTcs?.TrySetCanceled();
        _writeAckTcs?.TrySetCanceled();

        foreach (var kvp in _pendingReads)
        {
            kvp.Value.TrySetCanceled();
        }
    }
}
