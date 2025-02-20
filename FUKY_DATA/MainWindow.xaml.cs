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



namespace FUKY_DATA
{
    public partial class MainWindow : Window
    {

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
                }
            });
        }

        // 更新设备列表的核心方法
        private async Task UpdateDeviceList(DeviceInformation device, bool isConnected)
        {
            // 获取详细连接状态
            var isActuallyConnected = await CheckActualConnection(device.Id);

            Application.Current.Dispatcher.Invoke(() =>
            {
                var existing = Devices.FirstOrDefault(d => d.DeviceId == device.Id);

                if (isActuallyConnected)
                {
                    if (existing == null)
                    {
                        Devices.Add(new BluetoothDeviceInfo
                        {
                            Name = device.Name,
                            Status = "Active",
                            DeviceId = device.Id,
                            IsActive = true
                        });
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

        // 修改后的设备信息类
        public class BluetoothDeviceInfo
        {
            public string Name { get; set; }
            public string Status { get; set; }
            public string DeviceId { get; set; }
            public bool IsActive { get; set; }
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
    }
}