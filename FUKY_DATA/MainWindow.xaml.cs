using FUKY_DATA;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Linq;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using System.Collections.Generic;
using Windows.Devices.Bluetooth.GenericAttributeProfile;



namespace FUKY_DATA
{
    public partial class MainWindow : Window
    {
        private static readonly Guid TARGET_SERVICE_UUID = new Guid("0000f233-0000-1000-8000-00805f9b34fb");
        public BluetoothDeviceInfo FUKY_DEVICE;
        public ObservableCollection<BluetoothDeviceInfo> Devices { get; } = new ObservableCollection<BluetoothDeviceInfo>();
        // 添加Windows运行时设备观察器
        private DeviceWatcher bluetoothWatcher;

        public MainWindow()
        {
            InitializeComponent();
            DeviceList.ItemsSource = Devices;
            InitializeBluetoothWatcher();
        }

        // 初始化蓝牙设备观察器
        private void InitializeBluetoothWatcher()
        {
            // 使用更精确的设备选择器
            string selector = BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);

            bluetoothWatcher = DeviceInformation.CreateWatcher(selector,
                new string[] { "System.Devices.Aep.IsConnected" },
                DeviceInformationKind.AssociationEndpoint);

            // 添加设备更新事件
            bluetoothWatcher.Added += DeviceWatcher_Added;
            bluetoothWatcher.Updated += DeviceWatcher_Updated;
            bluetoothWatcher.Removed += DeviceWatcher_Removed;
        }

        // 设备添加事件
        private async void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
        {
            await UpdateDeviceList(args, isConnected: true);
        }

        // 设备更新事件
        private async void DeviceWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            var device = await DeviceInformation.CreateFromIdAsync(args.Id);
            await UpdateDeviceList(device, isConnected: true);
        }

        // 设备移除事件
        private void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var existing = Devices.FirstOrDefault(d => d.DeviceId == args.Id);
                if (existing != null)
                {
                    Devices.Remove(existing);
                    if(existing.ServiceUUIDs.Any(service => service == TARGET_SERVICE_UUID))
                    {
                        Debug.WriteLine($"浮奇设备已拔出: {FUKY_DEVICE.Name}");
                        FUKY_DEVICE = null;
                    }
                }
            });
        }

        // 更新设备列表的核心方法
        private async Task UpdateDeviceList(DeviceInformation device, bool isConnected)
        {
            // 获取详细连接状态
            var isActuallyConnected = await CheckActualConnection(device.Id);

            // 获取设备服务信息
            var hasTargetService = await CheckDeviceServices(device.Id);

            Application.Current.Dispatcher.Invoke(() =>
            {
                var existing = Devices.FirstOrDefault(d => d.DeviceId == device.Id);

                if (isActuallyConnected)
                {
                    if (existing == null)
                    {

                        // 从 hasTargetService 中提取 Services
                        var services = hasTargetService.Item2.Services.Select(service => service.Uuid).ToList();
                        var newDevice = new BluetoothDeviceInfo
                        {
                            Name = device.Name,
                            Status = "Active",
                            DeviceId = device.Id,
                            IsActive = true,
                            ServiceUUIDs = services // 添加服务UUID集合
                        };

                        Devices.Add(newDevice);

                        if (hasTargetService.Item1)
                        {
                            // 保存到专用变量
                            FUKY_DEVICE = newDevice;
                            Debug.WriteLine($"找到浮奇设备: {FUKY_DEVICE.Name}");
                        }
                    }
                    else
                    {
                        existing.Status = "Active";
                        existing.IsActive = true;
                    }
                }
                else if (existing != null)
                {
                    Devices.Remove(existing);
                }
            });
        }

        // 实际检查设备连接状态
        private async Task<bool> CheckActualConnection(string deviceId)
        {
            try
            {
                using (var device = await BluetoothLEDevice.FromIdAsync(deviceId))
                {
                    return device != null && device.ConnectionStatus == BluetoothConnectionStatus.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        // 设备信息类
        public class BluetoothDeviceInfo
        {
            public string Name { get; set; }
            public string Status { get; set; }
            public string DeviceId { get; set; }
            public bool IsActive { get; set; }
            public List<Guid> ServiceUUIDs { get; set; } // 存储服务UUID列表
        }

        // 启动/停止观察器
        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            Devices.Clear();

            if (bluetoothWatcher.Status == DeviceWatcherStatus.Started)
            {
                bluetoothWatcher.Stop();
            }

            bluetoothWatcher.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            bluetoothWatcher?.Stop();

            base.OnClosed(e);
        }

        // 新增服务检查方法
        private async Task<(bool,GattDeviceServicesResult)> CheckDeviceServices(string deviceId)
        {
            try
            {
                using (var device = await BluetoothLEDevice.FromIdAsync(deviceId))
                {
                    if (device == null) return (false,null);

                    // 获取所有GATT服务
                    var servicesResult = await device.GetGattServicesAsync();
                    if (servicesResult.Status != GattCommunicationStatus.Success) return (false,servicesResult);

                    // 检查目标服务是否存在
                    return (servicesResult.Services.Any(service =>service.Uuid == TARGET_SERVICE_UUID), servicesResult);
                }
            }
            catch
            {
                return (false, null);
            }
        }


    }
}