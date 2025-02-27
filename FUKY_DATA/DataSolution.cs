using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FUKY_DATA.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using MessagePack;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;


namespace FUKY_DATA.Services
{


    //ImuData结构体
    public readonly struct ImuData
    {
        // 原始数据
        public short LinAccelX { get; }
        public short LinAccelY { get; }
        public short LinAccelZ { get; }
        public short QuatI { get; }
        public short QuatJ { get; }
        public short QuatK { get; }
        public short QuatW { get; }

        // 在BNO080提供的官方驱动库进行的处理，目的是将int16_t转换为浮点数
        //在硬件驱动中的sh2_SensorValue.c文件有定义，为了方便数据传输，将转换浮点数这一步移动到电脑上处理
        private const int AccelScaleBits = 8;   
        private const int QuatScaleBits = 14;   

        //构造函数
        public ImuData(
            short linAccelX, short linAccelY, short linAccelZ,
            short quatI, short quatJ, short quatK, short quatW) 
        {
            LinAccelX = linAccelX;
            LinAccelY = linAccelY;
            LinAccelZ = linAccelZ;
            QuatI = quatI;
            QuatJ = quatJ;
            QuatK = quatK;
            QuatW = quatW;

        }

        // 缩放计算方法
        private static float Scale(int qFormatBits) => 1.0f / (1 << qFormatBits);

        // 加速度转换浮点数
        public float AccelerationX => LinAccelX * Scale(AccelScaleBits);
        public float AccelerationY => LinAccelY * Scale(AccelScaleBits);
        public float AccelerationZ => LinAccelZ * Scale(AccelScaleBits);

        // 四元数转换浮点数
        public float QuaternionI => QuatI * Scale(QuatScaleBits);
        public float QuaternionJ => QuatJ * Scale(QuatScaleBits);
        public float QuaternionK => QuatK * Scale(QuatScaleBits);
        public float QuaternionW => QuatW * Scale(QuatScaleBits);

        // 格式化输出重载，方便打印调试
        public override string ToString() =>
            $"Accel: ({AccelerationX:F3}g, {AccelerationY:F3}g, {AccelerationZ:F3}g)\n" +
            $"Quat: ({QuaternionI:F5}, {QuaternionJ:F5}, {QuaternionK:F5}, {QuaternionW:F5})\n";
    }


    //命名管道用到的数据格式，MessagePack是通用性和速度都较好的选择，PY C# CPP都有库提供实现
    //如果发现报错，你需要在解决方案管理器中右键FUKY_DATA
    [MessagePackObject]
    public sealed class ImuMessage
    {
        [Key(0)] public DateTime Timestamp { get; set; }
        [Key(1)] public byte[] RawBytes { get; set; }
        [Key(2)] public float AccelX { get; set; }
        [Key(3)] public float AccelY { get; set; }
        [Key(4)] public float AccelZ { get; set; }
        [Key(5)] public float QuatI { get; set; }
        [Key(6)] public float QuatJ { get; set; }
        [Key(7)] public float QuatK { get; set; }
        [Key(8)] public float QuatW { get; set; }
    }

    //
    internal class DataSolution : IDisposable
    {

        private NamedPipeServerStream _pipeServer;
        private bool _isPipeRunning = true;
        private const string PipeName = "FukyPipe"; // 管道名称
        private readonly BluetoothManager _btManager;
        private CancellationTokenSource _cts;
        private GattCharacteristic _dataCharacteristic;
        private bool _isInitialized;

        // 定义需要使用的UUID,这个定义可以在硬件驱动hid_device_le_prf.c中找到
        // 这个文件是负责创建GATT服务的，搜索f233和f666应该能找到
        private static readonly Guid SERVICE_UUID = new Guid("0000f233-0000-1000-8000-00805f9b34fb");
        private static readonly Guid CHARACTERISTIC_UUID = new Guid("0000f666-0000-1000-8000-00805f9b34fb");

        // 事件用于，向外传递数据，每当接收到完整的一条数据时就会调用
        public event Action<byte[], ImuData> DataReceived;
        public event Action<string> ErrorOccurred;

        //构造函数
        public DataSolution(BluetoothManager bluetoothManager)
        {
            _btManager = bluetoothManager;
            StartMonitoring(); //初始化后立即启动监控
        }

        
        public void StartMonitoring()
        {
            if (_cts != null && !_cts.IsCancellationRequested) return;
            //命名管道的库函数，我只管调用API
            InitializePipeServer();
            PipeServerLoopAsync();
            _cts = new CancellationTokenSource();
            //开个线程专门读数据
            Task.Run(() => ReadDataLoop(_cts.Token));
        }

        public void StopMonitoring()
        {
            _cts?.Cancel();
            _dataCharacteristic?.Service?.Dispose();
            _dataCharacteristic = null;
            _isInitialized = false;
        }

        public void DeviceDisConnect()
        {
            _dataCharacteristic = null;
            _isInitialized = false;
        }

        private async void OnDeviceUpdated(BluetoothDeviceInfo device)
        {
            if (device.DeviceId == _btManager.FukyDevice?.DeviceId)
            {
                await InitializeCharacteristic();
            }
        }

        private async Task InitializeCharacteristic()
        {
            try
            {
                if (_btManager.FukyDevice == null)  return;

                // 获取蓝牙设备实例
                var device = await BluetoothLEDevice.FromIdAsync(_btManager.FukyDevice.DeviceId);

                // 获取目标服务
                var serviceResult = await device.GetGattServicesAsync();
                if (serviceResult.Status != GattCommunicationStatus.Success) { ErrorOccurred?.Invoke("设备上没任何服务，检查连接"); return; };

                var Fuky_Service = serviceResult.Services.First(s => s.Uuid == SERVICE_UUID);

                // 获取GATT服务上的UUID特征
                var characteristicResult = await Fuky_Service.GetCharacteristicsForUuidAsync(CHARACTERISTIC_UUID);
                if (characteristicResult.Status != GattCommunicationStatus.Success) { ErrorOccurred?.Invoke("FUKY上没有233，伤心"); return; }

                _dataCharacteristic = characteristicResult.Characteristics[0];

                // 启用通知(这里理解为订阅了f233服务中的f666数据，BLE会在收到订阅通知后开始发送数据)
                var configResult = await _dataCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);
                if (configResult != GattCommunicationStatus.Success)
                {
                    ErrorOccurred?.Invoke("没有启用通知功能");
                    return;
                }

                _dataCharacteristic.ValueChanged += Characteristic_ValueChanged;
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"初始化特征值失败: {ex.Message}");
            }
        }

        private async Task ReadDataLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    //如果找到设备就初始化
                    if (_btManager.FukyDevice != null && !_isInitialized)
                    {
                        await InitializeCharacteristic();
                    }
                    else if (_btManager.FukyDevice == null && _isInitialized)
                    {
                        // 设备断开时更新标志
                        DeviceDisConnect();
                        continue;
                    }

                    if (_dataCharacteristic == null)
                    {
                        await Task.Delay(1000, token);
                        continue;
                    }

                    // 主动读取数据
                    var readResult = await _dataCharacteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                    if (readResult.Value == null || readResult.Status == GattCommunicationStatus.Success)
                    {
                        var data = ReadBufferToArray(readResult.Value);
                        // 复用解析逻辑
                        if (TryParseImuData(data, out var imuData))
                        {
                            DataReceived?.Invoke(data, imuData);
                        }
                    }

                   // await Task.Delay(500, token); // 500ms读取间隔
                }
                catch (TaskCanceledException)
                {
                    // 正常停止
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"数据读取失败: {ex.Message}");
                }
            }
        }

        private void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                var bytes = ReadBufferToArray(args.CharacteristicValue);
                if (TryParseImuData(bytes, out var imuData))
                {
                    DataReceived?.Invoke(bytes, imuData);
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"通知数据解析失败: {ex.Message}");
            }
        }

        private byte[] ReadBufferToArray(IBuffer buffer)
        {
            var reader = DataReader.FromBuffer(buffer);
            var bytes = new byte[buffer.Length];
            reader.ReadBytes(bytes);
            return bytes;
        }

        public void Dispose()
        {
            StopMonitoring();
            _isPipeRunning = false;
            _pipeServer?.Dispose();
            _cts?.Dispose();
        }

        //通用解析方法
        private bool TryParseImuData(byte[] bytes, out ImuData imuData)
        {
            imuData = default;
            try
            {
                if (bytes.Length != 14)
                {
                    return false;
                }

                var int16Values = new short[7];
                for (int i = 0; i < 7; i++)
                {
                    int offset = i * 2;
                    int16Values[i] = BitConverter.ToInt16(bytes, offset);
                }

                imuData = new ImuData(
                    int16Values[0], 
                    int16Values[1], 
                    int16Values[2],
                    int16Values[3], 
                    int16Values[4], 
                    int16Values[5], 
                    int16Values[6]
                );
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"数据解析异常: {ex.Message}");
                return false;
            }
        }
        
        private static float QScale(int n) => 1.0f / (1 << n);

        // 初始化管道服务器
        private void InitializePipeServer()
        {
            // 创建管道实例（单客户端模式）
            _pipeServer = new NamedPipeServerStream(
                PipeName,
                PipeDirection.Out,
                1, // 只允许1个客户端连接
                PipeTransmissionMode.Message,
                PipeOptions.Asynchronous
            );

        }

        // 管道服务器功能循环
        private void PipeServerLoopAsync()
        {
            Task.Run(async () =>
            {
                while (_isPipeRunning)
                {
                    try
                    {
                        // 等待客户端连接
                        await _pipeServer.WaitForConnectionAsync();
                        Debug.WriteLine($"有管道客户端连接");
                        // 订阅数据事件
                        DataReceived += SendDataThroughPipe;

                        // 监听客户端断开
                        while (_pipeServer.IsConnected)
                        {
                            await Task.Delay(500);
                        }

                        // 客户端断开后取消订阅
                        DataReceived -= SendDataThroughPipe;
                        _pipeServer.Disconnect();
                    }
                    catch (Exception ex)
                    {
                        ErrorOccurred?.Invoke($"管道错误: {ex.Message}");
                        await Task.Delay(1000); // 错误后重试间隔
                    }
                }
            });
        }

        // 通过管道发送数据
        private void SendDataThroughPipe(byte[] rawBytes, ImuData data)
        {
            try
            {
                if (_pipeServer?.IsConnected != true) return;

                var msg = new ImuMessage
                {
                    Timestamp = DateTime.UtcNow,
                    RawBytes = rawBytes,
                    AccelX = data.AccelerationX,
                    AccelY = data.AccelerationY,
                    AccelZ = data.AccelerationZ,
                    QuatI = data.QuaternionI,
                    QuatJ = data.QuaternionJ,
                    QuatK = data.QuaternionK,
                    QuatW = data.QuaternionW
                };

                // 构建数据包
                byte[] payload = MessagePackSerializer.Serialize(msg);
                byte[] header = BitConverter.GetBytes(payload.Length);
                if (!BitConverter.IsLittleEndian) Array.Reverse(header);

                var ms = new MemoryStream(header.Length + payload.Length);
                ms.Write(header, 0, header.Length);
                ms.Write(payload, 0, payload.Length);

                // 异步写入管道
                var buffer = ms.ToArray(); 
                _pipeServer.WriteAsync(buffer, 0, (int)ms.Length);
                _pipeServer.Flush(); // 确保数据立即发送
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"管道写入失败: {ex.Message}");
            }
        }

    }
}