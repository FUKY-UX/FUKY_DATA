using System;
using System.Collections.Generic;
using System.Linq;
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
}
