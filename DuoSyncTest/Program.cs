using System;
using System.Collections.Generic;
using System.Data;

namespace DuoSyncTest
{
    class Program
    {
        static void Main(string[] args)
        {
            List<DuoDevice> allPhones = DuoDevice.GetAllPhones();
            DataTable deviceDt = DbOperation.CreateDeviceDataTable(allPhones);
            DbOperation.MergeDeviceTable(deviceDt);
            DataTable deviceUserDt = DbOperation.CreateDeviceUserDataTable(allPhones);
            DbOperation.MergeDeviceUserTable(deviceUserDt);
            //UpdateDuo updateDuo = DbOperation.GetDeviceNeedToBeAssociateAndDisassociateFromUser();
            //DuoDevice.AssociatePhoneWithUser(updateDuo.ToBeAdded);
            //List<Tuple<string, string, DateTime>> hasBeenDel = DuoDevice.DisassociatePhoneFromUser(updateDuo.ToBeDeleted);
            //DbOperation.DumpDeletedToDeleteTableAndRemoveThemFromDeviceUserTable(hasBeenDel);
        }
    }
}
