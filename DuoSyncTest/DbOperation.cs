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

        public static DataTable CreateDeviceUserDataTable(List<DuoDevice> allPhones)
        {
            DataTable dt = new DataTable();
            dt.Clear();
            dt.Columns.Add("duoDeviceID");
            dt.Columns.Add("duoUserID");
            dt.Columns.Add("Username");
            foreach (var device in allPhones)
            {
                try
                {
                    foreach (object user in device.Users)
                    {
                        Dictionary<string, object> currentUser = (Dictionary<string, object>)(user);
                        string duoUserID = currentUser["user_id"].ToString();
                        string username = currentUser["username"].ToString();
                        dt.Rows.Add(new object[] { device.Phone_id, duoUserID, username });
                    }
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
                                      "when not matched by target then " +
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
                                Source.Type,Source.SmsPasscodesSent) " +
                                    "when not matched by source then delete;";

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

        public static void MergeDeviceUserTable(DataTable dt)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(conStr))
                {
                    SqlCommand cmd = new SqlCommand(@"create table #DeviceUser(
                                                [duoDeviceID] nvarchar(50)
                                                , [duoUserID] nvarchar(50)
                                                , [Username] nvarchar(50)
                                                , [disassociationFlag] bit
                                                )", conn);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                    using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
                    {
                        bulkCopy.DestinationTableName = "#DeviceUser";
                        bulkCopy.WriteToServer(dt);
                    }

                    //Now use the merge command to upsert from the temp table to the production table
                    string mergeSql = "merge into duo_user_device as Target " +
                                      "using #DeviceUser as Source " +
                                      "on " +
                                      "Target.duoDeviceID=Source.duoDeviceID and " +
                                      "Target.duoUserID=Source.duoUserID " +
                                      "when matched then " +
                                      "update set Target.Username=Source.Username " +
                                      "when not matched by target then " +
                                      @"insert ([duoDeviceID]
                                          ,[duoUserID]
                                          ,[Username]
                                          ,[disassociationFlag]) values (
                                Source.duoDeviceID,Source.duoUserID,Source.Username,0)" +
                                "when not matched by source then update set Target.disassociationFlag=1;";

                    cmd.CommandText = mergeSql;
                    cmd.ExecuteNonQuery();

                    //Clean up the temp table
                    cmd.CommandText = "drop table #DeviceUser";
                    cmd.ExecuteNonQuery();
                    conn.Close();
                }
            }
            catch (Exception)
            {

            }
        }

        public static UpdateDuo GetDeviceNeedToBeAssociateAndDisassociateFromUser()
        {
            List<Tuple<string, string>> toBeAdded = new List<Tuple<string, string>>();
            List<Tuple<string, string>> toBeDeleted = new List<Tuple<string, string>>();
            try
            {
                using (SqlConnection con = new SqlConnection(conStr))
                {
                    con.Open();

                    string query = @"
select dud.duoDeviceID, du.Username, du.duoUserid,max(cast(disassociationFlag as int)) as fll from
(SELECT duoDeviceID, duo_user.Username, duo_user.PrincipalUsername, disassociationFlag
  FROM [ECIPortalDev].[dbo].[duo_user_device]
  inner join duo_user
  on duo_user_device.Username=duo_user.Username) dud
  inner join duo_user du
  on dud.PrincipalUsername=du.PrincipalUsername
  group by dud.duoDeviceID, du.Username, du.duoUserid
  except
  select duoDeviceID, Username, duouserid, disassociationFlag as fll
  from duo_user_device where disassociationFlag=0
  except
  select duoDeviceID, Username, duouserid, disassociationFlag as fll
  from duo_user_device
  where disassociationFlag=1
  order by fll,username
 ";

                    using (SqlCommand cmd = new SqlCommand(query, con))
                    {
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader != null)
                            {
                                while (reader.Read())
                                {
                                    try
                                    {
                                        string duoDeviceID = (string)reader["duoDeviceID"];
                                        string duoUseridduoUserid = (string)reader["duoUserid"];
                                        int disassociationFlag = (int)reader["fll"];
                                        if (disassociationFlag == 0)
                                        {
                                            toBeAdded.Add(new Tuple<string, string>(duoDeviceID, duoUseridduoUserid));
                                        }
                                        else
                                        {
                                            toBeDeleted.Add(new Tuple<string, string>(duoDeviceID, duoUseridduoUserid));
                                        }
                                    }
                                    catch (Exception)
                                    {

                                    }
                                }
                            }
                        }
                    }

                    con.Close(); // reader closed and disposed up here

                    UpdateDuo updateDuo = new UpdateDuo();
                    updateDuo.ToBeAdded = toBeAdded;
                    updateDuo.ToBeDeleted = toBeDeleted;

                    return updateDuo;
                } //connection closed and disposed here
            }
            catch (SqlException ex)
            {
                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
    }

    public class UpdateDuo
    {
        public List<Tuple<string, string>> ToBeAdded { get; set; }
        public List<Tuple<string, string>> ToBeDeleted { get; set; }
    }
}
