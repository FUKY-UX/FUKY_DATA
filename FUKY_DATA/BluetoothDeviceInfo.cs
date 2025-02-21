using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace FUKY_DATA.Models
{
    public class BluetoothDeviceInfo
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public string DeviceId { get; set; }
        public bool IsActive { get; set; }
        public List<System.Guid> ServiceUUIDs { get; set; }
    }

    // 用来显示IMU数据
    public class DataDisplayModel : INotifyPropertyChanged
    {
        private string _rawData;
        private string _quaternion;
        private string _acceleration;

        public string RawData
        {
            get => _rawData;
            set { _rawData = value; OnPropertyChanged(); }
        }

        public string Quaternion
        {
            get => _quaternion;
            set { _quaternion = value; OnPropertyChanged(); }
        }

        public string Acceleration
        {
            get => _acceleration;
            set { _acceleration = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
