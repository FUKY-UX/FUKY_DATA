
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
        public ObservableCollection<BluetoothDeviceInfo> Devices => _btManager.Devices;

        public MainWindow()
        {
            InitializeComponent();
            DeviceList.ItemsSource = Devices;
            InitializeBluetoothManager();
            DataContext = this;

            _dataSolution = new DataSolution(_btManager);
            _dataSolution.DataReceived += OnDataReceived;
            _dataSolution.ErrorOccurred += OnDataError;
        }

        private void OnDataReceived(byte[] data) {

            Debug.WriteLine($"接收数据: {data}");
            //Dispatcher.Invoke(() => {
            //    // 更新UI显示数据
            //});
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