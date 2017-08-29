using System.Collections.Generic;
using System.Data;

namespace DuoSyncTest
{
    class Program
    {
        static void Main(string[] args)
        {
            List<DuoDevice> allPhones = DuoDevice.GetAllPhones();
            DataTable dt = DbOperation.CreateDeviceDataTable(allPhones);
            DbOperation.MergeDeviceTable(dt);
        }
    }
}
