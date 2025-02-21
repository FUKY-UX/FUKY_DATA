
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using FUKY_DATA.Models;
using FUKY_DATA.Services;



namespace FUKY_DATA.Views
{
    public partial class MainWindow : Window
    {
        //蓝牙连接检测
        private readonly BluetoothManager _btManager = new BluetoothManager();
        //数据处理
        private readonly DataSolution _dataSolution;
        //UI
        public ObservableCollection<BluetoothDeviceInfo> Devices => _btManager.Devices;
        private readonly ObservableCollection<DataDisplayModel> _dataDisplay = new ObservableCollection<DataDisplayModel>();//IMU
        private readonly DataDisplayModel _currentData = new DataDisplayModel();

        public MainWindow()
        {
            InitializeComponent();
            DeviceList.ItemsSource = Devices;
            _dataDisplay.Add(_currentData);
            DataView.ItemsSource = _dataDisplay;

            InitializeBluetoothManager();
            DataContext = this;

            _dataSolution = new DataSolution(_btManager);
            _dataSolution.DataReceived += OnDataReceived;
            _dataSolution.ErrorOccurred += OnDataError;
        }

        private void OnDataReceived(byte[] rawData, ImuData data) 
        {


            // 转换为十六进制字符串
            var hexString = BitConverter.ToString(rawData).Replace("-", " ");

            // 格式化四元数
            var quatString = $"I:{data.QuaternionI:F5} J:{data.QuaternionJ:F5} K:{data.QuaternionK:F5} W:{data.QuaternionW:F5}";

            // 格式化加速度
            var accelString = $"X:{data.AccelerationX:F3} Y:{data.AccelerationY:F3} Z:{data.AccelerationZ:F3}";


            Dispatcher.Invoke(() =>
            {
                _currentData.RawData = hexString;
                _currentData.Quaternion = quatString;
                _currentData.Acceleration = accelString;
            });
        }
 

        private void OnDataError(string message)
        {
            Dispatcher.Invoke(() => MessageBox.Show(message));
        }


        private void InitializeBluetoothManager()
        {

            _btManager.Dispatcher = Dispatcher; // 注入 UI 线程的 Dispatcher
            _btManager.Initialize();

            // 注册事件，Dispatcher是WPF的跨线程调度器，用法类似事件，在蓝牙触发的事件时候调用UI线程里的函数(WPF默认把UI单独放一个线程)
            _btManager.DeviceAdded += device =>
                Dispatcher.Invoke(() => DeviceList.Items.Refresh());

            _btManager.DeviceRemoved += deviceId =>
                Dispatcher.Invoke(() => DeviceList.Items.Refresh());

            _btManager.ErrorOccurred += message =>
                Dispatcher.Invoke(() => MessageBox.Show(message));
        }

        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            _btManager.Devices.Clear();
            _btManager.StartScanning();
        }

        protected override void OnClosed(EventArgs e)
        {
            _dataSolution.Dispose();
            _btManager.StopScanning();
            base.OnClosed(e);
        }

    }
}