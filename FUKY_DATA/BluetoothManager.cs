using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FUKY_DATA.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Input;

namespace FUKY_DATA.Services
{
    internal class BluetoothManager
    {
        // UI线程调度
        public System.Windows.Threading.Dispatcher Dispatcher { get; set; } 
        // 事件定义
        public event Action<BluetoothDeviceInfo> DeviceAdded;
        public event Action<BluetoothDeviceInfo> DeviceUpdated;
        public event Action<string> DeviceRemoved;
        public event Action<string> ErrorOccurred;

        // 常量定义
        private static readonly Guid TARGET_SERVICE_UUID = new Guid("0000f233-0000-1000-8000-00805f9b34fb");

        // 设备管理
        private DeviceWatcher _watcher;
        public BluetoothDeviceInfo FukyDevice { get; private set; }

        public ObservableCollection<BluetoothDeviceInfo> Devices { get; } = new ObservableCollection<BluetoothDeviceInfo>();

        public void Initialize()
        {
            var selector = BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            _watcher = DeviceInformation.CreateWatcher(selector,
                new[] { "System.Devices.Aep.IsConnected" },
                DeviceInformationKind.AssociationEndpoint);

            _watcher.Added += async (s, args) => await HandleDeviceAdded(args);
            _watcher.Updated += async (s, args) => await HandleDeviceUpdated(args);
            _watcher.Removed += HandleDeviceRemoved;
        }

        public void StartScanning() => _watcher?.Start();
        public void StopScanning() => _watcher?.Stop();

        private async Task HandleDeviceAdded(DeviceInformation args)
        {
            try
            {
                var (isConnected, servicesResult) = await CheckDeviceStatus(args.Id);
                if (!isConnected) return;

                var deviceInfo = CreateDeviceInfo(args, servicesResult);

                var existing = Devices.FirstOrDefault(d => d.DeviceId == args.Id);
                if (existing == null)
                {
                    // 通过 Dispatcher 在 UI 线程添加设备
                    Dispatcher?.Invoke(() => Devices.Add(deviceInfo));
                    DeviceAdded?.Invoke(deviceInfo);
                }


                if (HasTargetService(servicesResult))
                {
                    FukyDevice = deviceInfo;
                    Debug.WriteLine($"找到浮奇设备: {deviceInfo.Name}");
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"添加设备失败: {ex.Message}");
            }
        }

        private bool HasTargetService(GattDeviceServicesResult services)
        {
            return services?.Services.Any(s => s.Uuid == TARGET_SERVICE_UUID) ?? false;
        }

        private bool HasTargetService(List<Guid> services)
        {
            return services?.Any(s => s == TARGET_SERVICE_UUID) ?? false;
        }

        private async Task HandleDeviceUpdated(DeviceInformationUpdate args)
        {
            try
            {
                // 获取详细连接状态,获取设备服务信息
                var (isConnected, servicesResult) = await CheckDeviceStatus(args.Id);
                var device = await DeviceInformation.CreateFromIdAsync(args.Id);

                //检查列表还有没有这个要更新的设备信息，
                var DeviceInf = CreateDeviceInfo(device, servicesResult);
                var existing = Devices.FirstOrDefault(d => d.DeviceId == args.Id);
                if (existing != null)
                {
                    //有的话就把处理好的设备状态更新到列表中，然后通知订阅事件，设备信息更新
                    //通过 Dispatcher 在 UI 线程更新设备
                    Dispatcher?.Invoke(() => Devices.Select(d => d = DeviceInf));
                    DeviceUpdated?.Invoke(DeviceInf);
                }
                else
                {
                    //应该不会有没有
                    return;
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"设备更新问题: {ex.Message}");
            }
        }
        private BluetoothDeviceInfo CreateDeviceInfo(DeviceInformation device, GattDeviceServicesResult services)
        {
            return new BluetoothDeviceInfo
            {
                Name = device.Name,
                DeviceId = device.Id,
                Status = "Active",
                IsActive = true,
                ServiceUUIDs = services?.Services.Select(s => s.Uuid).ToList() ?? new List<Guid>()
            };
        }

        private void HandleDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            var existing = Devices.FirstOrDefault(d => d.DeviceId == args.Id);
            if (existing != null)
            {
                //有的话就把设备从列表移除，通过 Dispatcher 在 UI 线程移除设备
                Dispatcher?.Invoke(() => Devices.Remove(existing));
                if (HasTargetService(existing.ServiceUUIDs))
                {
                    Debug.WriteLine($"浮奇设备已拔出: {FukyDevice.Name}");
                    FukyDevice = null;
                }
            }
        }

        private async Task<(bool isConnected, GattDeviceServicesResult services)> CheckDeviceStatus(string deviceId)
        {
            try
            {
                using (var device = await BluetoothLEDevice.FromIdAsync(deviceId))
                {

                    if (device != null && device.ConnectionStatus == BluetoothConnectionStatus.Connected)
                    {
                        var servicesResult = await device.GetGattServicesAsync();
                        if (servicesResult.Status != GattCommunicationStatus.Success) { return (false, servicesResult); };
                        return (true, servicesResult);
                    }
                    else { return (false, null); };
                }
            }
            catch
            {
                return (false, null);
            }
        }

    }

}
