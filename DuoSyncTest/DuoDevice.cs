using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DuoSyncTest
{
    public class DuoDevice
    {
        #region Public Properties

        /// <summary>
        /// Properties for the Duo Device
        /// </summary>
        public bool Activated { get; set; }
        public ArrayList Capabilities { get; set; }
        public string Extension { get; set; }
        public string LastSeen { get; set; }
        public string Name { get; set; }
        public string Number { get; set; }
        public string Phone_id { get; set; }
        public string Platform { get; set; }
        public string Postdelay { get; set; }
        public string Predelay { get; set; }
        public bool SmsPasscodesSent { get; set; }
        public string Type { get; set; }
        public ArrayList Users { get; set; }

        #endregion Public Properties

        #region Connection Variables

        // Duo Authentication parameters
        public static string ikeyAuth = "A";
        public static string skeyAuth = "A";
        public static string hostAuth = "A";

        #endregion Connection Variables

        #region Duo Methods

        /// <summary>
        /// Method returns a list of all Duo Phones
        /// </summary>
        /// <returns>List of all DuoDevices</returns>
        public static List<DuoDevice> GetAllPhones()
        {
            List<DuoDevice> phones = new List<DuoDevice>();
            DateTime outDate = DateTime.Now;

            try
            {

                var client = new DuoApi(ikeyAuth, skeyAuth, hostAuth);
                var parameters = new Dictionary<string, string>();

                var resp = client.JSONApiCall<ArrayList>("GET", "/admin/v1/phones", parameters);

                foreach (object o in resp)
                {
                    Dictionary<string, object> currentDevice = (Dictionary<string, object>)(o);
                    DuoDevice curDevice = new DuoDevice
                    {
                        Activated = (bool)currentDevice["activated"],
                        Capabilities = (ArrayList)currentDevice["capabilities"],
                        Extension = currentDevice["extension"].ToString(),
                        LastSeen = currentDevice["last_seen"].ToString(),
                        Name = currentDevice["name"].ToString(),
                        Number = currentDevice["number"].ToString(),
                        Phone_id = currentDevice["phone_id"].ToString(),
                        Platform = currentDevice["platform"].ToString(),
                        Predelay = currentDevice["predelay"].ToString(),
                        Postdelay = currentDevice["postdelay"].ToString(),
                        SmsPasscodesSent = (bool)currentDevice["sms_passcodes_sent"],
                        Type = currentDevice["type"].ToString(),
                        Users = (ArrayList)currentDevice["users"],
                    };

                    phones.Add(curDevice);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Duo Method: GetAllUsers: " + ex.Message);
            }

            return phones;
        }

        #endregion Duo Methods

    }
}
