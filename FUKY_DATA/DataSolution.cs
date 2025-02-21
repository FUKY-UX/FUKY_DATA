using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Markup;
using FUKY_DATA.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace FUKY_DATA.Services
{
    internal class DataSolution : IDisposable
    {
        private readonly BluetoothManager _btManager;
        private CancellationTokenSource _cts;
        private GattCharacteristic _dataCharacteristic;
        private bool _isInitialized;

        // 定义需要使用的UUID
        private static readonly Guid SERVICE_UUID = new Guid("0000f233-0000-1000-8000-00805f9b34fb");
        private static readonly Guid CHARACTERISTIC_UUID = new Guid("0000f666-0000-1000-8000-00805f9b34fb");

        // 事件用于向外传递数据
        public event Action<byte[]> DataReceived;
        public event Action<string> ErrorOccurred;

        public DataSolution(BluetoothManager bluetoothManager)
        {
            _btManager = bluetoothManager;
            StartMonitoring(); //初始化后立即启动监控

        }

        public void StartMonitoring()
        {
            if (_cts != null && !_cts.IsCancellationRequested) return;

            _cts = new CancellationTokenSource();
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

                // 获取特征值
                var characteristicResult = await Fuky_Service.GetCharacteristicsForUuidAsync(CHARACTERISTIC_UUID);
                if (characteristicResult.Status != GattCommunicationStatus.Success) { ErrorOccurred?.Invoke("FUKY上没有233特征，盗版？"); return; }

                _dataCharacteristic = characteristicResult.Characteristics[0];

                // 启用通知
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
                    if (readResult.Status == GattCommunicationStatus.Success)
                    {
                        var data = ReadBufferToArray(readResult.Value);
                        DataReceived?.Invoke(data);
                    }

                    await Task.Delay(500, token); // 500ms读取间隔
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
            // 处理通知数据
            var data = ReadBufferToArray(args.CharacteristicValue);
            DataReceived?.Invoke(data);
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
            _cts?.Dispose();
        }
    }
}