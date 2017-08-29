using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DuoSyncTest
{
    public class DbOperation
    {
        private static string conStr = "A";

        public static DataTable CreateDeviceDataTable(List<DuoDevice> allPhones)
        {
            DataTable dt = new DataTable();
            dt.Clear();
            dt.Columns.Add("Activated");
            dt.Columns.Add("LastSeen");
            dt.Columns.Add("Name");
            dt.Columns.Add("Number");
            dt.Columns.Add("Phone_id");
            dt.Columns.Add("Platform");
            dt.Columns.Add("Type");
            dt.Columns.Add("SmsPasscodesSent");
            foreach (var device in allPhones)
            {
                try
                {
                    DateTime myDate;
                    if (!DateTime.TryParse(device.LastSeen, out myDate))
                    {
                        myDate = DateTime.Parse("1900-01-01");
                    }
                    dt.Rows.Add(new object[] { device.Activated, myDate, device.Name,
                device.Number, device.Phone_id, device.Platform, device.Type, device.SmsPasscodesSent});
                }
                catch (Exception)
                {

                }
            }

            return dt;
        }

        public static void MergeDeviceTable(DataTable dt)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(conStr))
                {
                    SqlCommand cmd = new SqlCommand(@"create table #Device(
                                                Activated bit
                                                , LastSeen datetime
                                                , [Name] nvarchar(50)
                                                , [Number] nvarchar(50)
                                                , [Phone_id] nvarchar(50)
                                                , [Platform] nvarchar(20)
                                                , [Type] nvarchar(30)
                                                , [SmsPasscodesSent] bit
                                                )", conn);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                    using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
                    {
                        bulkCopy.DestinationTableName = "#Device";
                        bulkCopy.WriteToServer(dt);
                    }

                    //Now use the merge command to upsert from the temp table to the production table
                    string mergeSql = "merge into duo_device as Target " +
                                      "using #Device as Source " +
                                      "on " +
                                      "Target.Phone_id=Source.Phone_id " +
                                      "when matched then " +
                                      "update set Target.Activated=Source.Activated, " +
                                      "Target.LastSeen=Source.LastSeen, " +
                                      "Target.Name=Source.Name, " +
                                      "Target.Number=Source.Number, " +
                                      "Target.Platform=Source.Platform, " +
                                      "Target.Type=Source.Type, " +
                                      "Target.SmsPasscodesSent=Source.SmsPasscodesSent " +
                                      "when not matched then " +
                                      @"insert (Activated
                                  , LastSeen
                                  , Name
                                  , Number
                                  , Phone_id
                                  , Platform
                                  , Type
                                  , SmsPasscodesSent) values (
                                Source.Activated,Source.LastSeen,Source.Name,
                                Source.Number,Source.Phone_id,Source.Platform,
                                Source.Type,Source.SmsPasscodesSent);";

                    cmd.CommandText = mergeSql;
                    cmd.ExecuteNonQuery();

                    //Clean up the temp table
                    cmd.CommandText = "drop table #Device";
                    cmd.ExecuteNonQuery();
                    conn.Close();
                }
            }
            catch (Exception)
            {

            }
        }
    }
}
