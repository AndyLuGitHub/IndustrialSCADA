namespace IndustrialSCADA.Core.Enums;

/// <summary>
/// 通信协议类型
/// </summary>
public enum ProtocolType
{
    /// <summary>西门子 S7</summary>
    S7 = 0,
    /// <summary>Modbus TCP</summary>
    ModbusTcp = 1,
    /// <summary>OPC UA</summary>
    OpcUa = 2,
    /// <summary>Beckhoff EtherCAT/ADS</summary>
    EtherCat = 3,
    /// <summary>IEC 60870-5-104</summary>
    IEC104 = 4,
    /// <summary>DL/T 645 电表协议</summary>
    DL645 = 5,
    /// <summary>内置模拟器</summary>
    Simulator = 99
}
