﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Web.Script.Serialization;
using System.Web;
using System.Globalization;

namespace DuoSyncTest
{
    public class DuoApi
    {
        public string DEFAULT_AGENT = "DuoAPICSharp/1.0";

        private string ikey;
        private string skey;
        private string host;
        private string url_scheme;
        private string user_agent;

        /// <param name="ikey">Duo integration key</param>
        /// <param name="skey">Duo secret key</param>
        /// <param name="host">Application secret key</param>
        public DuoApi(string ikey, string skey, string host)
            : this(ikey, skey, host, null)
        {
        }

        /// <param name="ikey">Duo integration key</param>
        /// <param name="skey">Duo secret key</param>
        /// <param name="host">Application secret key</param>
        /// <param name="user_agent">HTTP client User-Agent</param>
        public DuoApi(string ikey, string skey, string host, string user_agent)
            : this(ikey, skey, host, user_agent, "https")
        {
        }

        protected DuoApi(string ikey, string skey, string host, string user_agent, string url_scheme)
        {
            this.ikey = ikey;
            this.skey = skey;
            this.host = host;
            this.url_scheme = url_scheme;

            if (String.IsNullOrEmpty(user_agent))
            {
                this.user_agent = FormatUserAgent(DEFAULT_AGENT);
            }
            else
            {
                this.user_agent = user_agent;
            }
        }

        public static string CanonicalizeParams(Dictionary<string, string> parameters)
        {
            var ret = new List<String>();
            foreach (KeyValuePair<string, string> pair in parameters)
            {
                string p = String.Format("{0}={1}",
                                         HttpUtility.UrlEncode(pair.Key),
                                         HttpUtility.UrlEncode(pair.Value));
                // Signatures require upper-case hex digits.
                p = Regex.Replace(p,
                                  "(%[0-9A-Fa-f][0-9A-Fa-f])",
                                  c => c.Value.ToUpperInvariant());
                // Escape only the expected characters.
                p = Regex.Replace(p,
                                  "([!'()*])",
                                  c => "%" + Convert.ToByte(c.Value[0]).ToString("X"));
                p = p.Replace("%7E", "~");
                // UrlEncode converts space (" ") to "+". The
                // signature algorithm requires "%20" instead. Actual
                // + has already been replaced with %2B.
                p = p.Replace("+", "%20");
                ret.Add(p);
            }
            ret.Sort(StringComparer.Ordinal);
            return string.Join("&", ret.ToArray());
        }

        protected string CanonicalizeRequest(string method,
                                             string path,
                                             string canon_params,
                                             string date)
        {
            string[] lines = {
                date,
                method.ToUpperInvariant(),
                this.host.ToLower(),
                path,
                canon_params,
            };
            string canon = String.Join("\n",
                                       lines);
            return canon;
        }

        public string Sign(string method,
                           string path,
                           string canon_params,
                           string date)
        {
            string canon = this.CanonicalizeRequest(method,
                                                    path,
                                                    canon_params,
                                                    date);
            string sig = this.HmacSign(canon);
            string auth = this.ikey + ':' + sig;
            return "Basic " + DuoApi.Encode64(auth);
        }

        public string ApiCall(string method,
                              string path,
                              Dictionary<string, string> parameters)
        {
            HttpStatusCode statusCode;
            return ApiCall(method, path, parameters, 0, DateTime.UtcNow, out statusCode);
        }

        /// <param name="timeout">The request timeout, in milliseconds.
        /// Specify 0 to use the system-default timeout. Use caution if
        /// you choose to specify a custom timeout - some API
        /// calls (particularly in the Auth and Verify APIs) will not
        /// return a response until an out-of-band authentication process
        /// has completed. In some cases, this may take as much as a
        /// small number of minutes.</param>
        public string ApiCall(string method,
                              string path,
                              Dictionary<string, string> parameters,
                              int timeout,
                              out HttpStatusCode statusCode)
        {
            return ApiCall(method, path, parameters, 0, DateTime.UtcNow, out statusCode);
        }

        /// <param name="date">The current date and time, used to authenticate
        /// the API request. Typically, you should specify DateTime.UtcNow,
        /// but if you do not wish to rely on the system-wide clock, you may
        /// determine the current date/time by some other means.</param>
        /// <param name="timeout">The request timeout, in milliseconds.
        /// Specify 0 to use the system-default timeout. Use caution if
        /// you choose to specify a custom timeout - some API
        /// calls (particularly in the Auth and Verify APIs) will not
        /// return a response until an out-of-band authentication process
        /// has completed. In some cases, this may take as much as a
        /// small number of minutes.</param>
        public string ApiCall(string method,
                              string path,
                              Dictionary<string, string> parameters,
                              int timeout,
                              DateTime date,
                              out HttpStatusCode statusCode)
        {
            string canon_params = DuoApi.CanonicalizeParams(parameters);
            string query = "";
            if (!method.Equals("POST") && !method.Equals("PUT"))
            {
                if (parameters.Count > 0)
                {
                    query = "?" + canon_params;
                }
            }
            string url = string.Format("{0}://{1}{2}{3}",
                                       this.url_scheme,
                                       this.host,
                                       path,
                                       query);

            string date_string = DuoApi.DateToRFC822(date);
            string auth = this.Sign(method, path, canon_params, date_string);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Accept = "application/json";
            request.Headers.Add("Authorization", auth);
            request.Headers.Add("X-Duo-Date", date_string);
            request.UserAgent = this.user_agent;

            if (method.Equals("POST") || method.Equals("PUT"))
            {
                byte[] data = Encoding.UTF8.GetBytes(canon_params);
                request.ContentType = "application/x-www-form-urlencoded";
                request.ContentLength = data.Length;
                using (Stream requestStream = request.GetRequestStream())
                {
                    requestStream.Write(data, 0, data.Length);
                }
            }
            if (timeout > 0)
            {
                request.Timeout = timeout;
            }

            // Do the request and process the result.
            HttpWebResponse response;
            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (WebException ex)
            {
                response = (HttpWebResponse)ex.Response;
                if (response == null)
                {
                    throw;
                }
            }
            StreamReader reader
                = new StreamReader(response.GetResponseStream());
            statusCode = response.StatusCode;
            return reader.ReadToEnd();
        }

        public T JSONApiCall<T>(string method,
                                string path,
                                Dictionary<string, string> parameters)
            where T : class
        {
            return JSONApiCall<T>(method, path, parameters, 0, DateTime.UtcNow);
        }

        /// <param name="timeout">The request timeout, in milliseconds.
        /// Specify 0 to use the system-default timeout. Use caution if
        /// you choose to specify a custom timeout - some API
        /// calls (particularly in the Auth and Verify APIs) will not
        /// return a response until an out-of-band authentication process
        /// has completed. In some cases, this may take as much as a
        /// small number of minutes.</param>
        public T JSONApiCall<T>(string method,
                                string path,
                                Dictionary<string, string> parameters,
                                int timeout)
            where T : class
        {
            return JSONApiCall<T>(method, path, parameters, timeout, DateTime.UtcNow);
        }

        /// <param name="date">The current date and time, used to authenticate
        /// the API request. Typically, you should specify DateTime.UtcNow,
        /// but if you do not wish to rely on the system-wide clock, you may
        /// determine the current date/time by some other means.</param>
        /// <param name="timeout">The request timeout, in milliseconds.
        /// Specify 0 to use the system-default timeout. Use caution if
        /// you choose to specify a custom timeout - some API
        /// calls (particularly in the Auth and Verify APIs) will not
        /// return a response until an out-of-band authentication process
        /// has completed. In some cases, this may take as much as a
        /// small number of minutes.</param>
        public T JSONApiCall<T>(string method,
                                string path,
                                Dictionary<string, string> parameters,
                                int timeout,
                                DateTime date)
            where T : class
        {
            HttpStatusCode statusCode;
            string res = this.ApiCall(method, path, parameters, timeout, date, out statusCode);

            var jss = new JavaScriptSerializer();

            try
            {
                var dict = jss.Deserialize<Dictionary<string, object>>(res);
                if (dict["stat"] as string == "OK")
                {
                    return dict["response"] as T;
                }
                else
                {
                    int? check = dict["code"] as int?;
                    int code;
                    if (check.HasValue)
                    {
                        code = check.Value;
                    }
                    else
                    {
                        code = 0;
                    }
                    String message_detail = "";
                    if (dict.ContainsKey("message_detail"))
                    {
                        message_detail = dict["message_detail"] as string;
                    }
                    throw new ApiException(code,
                                           (int)statusCode,
                                           dict["message"] as string,
                                           message_detail);
                }
            }
            catch (ApiException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new BadResponseException((int)statusCode, e);
            }
        }

        /// Helper to format a User-Agent string with some information about
        /// the operating system / .NET runtime
        /// <param name="product_name">e.g. "FooClient/1.0"</param>
        public static string FormatUserAgent(string product_name)
        {
            return String.Format(
                 "{0} ({1}; .NET {2})", product_name, System.Environment.OSVersion,
                 System.Environment.Version);
        }

        private string HmacSign(string data)
        {
            byte[] key_bytes = ASCIIEncoding.ASCII.GetBytes(this.skey);
            HMACSHA1 hmac = new HMACSHA1(key_bytes);

            byte[] data_bytes = ASCIIEncoding.ASCII.GetBytes(data);
            hmac.ComputeHash(data_bytes);

            string hex = BitConverter.ToString(hmac.Hash);
            return hex.Replace("-", "").ToLower();
        }

        private static string Encode64(string plaintext)
        {
            byte[] plaintext_bytes = ASCIIEncoding.ASCII.GetBytes(plaintext);
            string encoded = System.Convert.ToBase64String(plaintext_bytes);
            return encoded;
        }

        private static string DateToRFC822(DateTime date)
        {
            // Can't use the "zzzz" format because it adds a ":"
            // between the offset's hours and minutes.
            string date_string = date.ToString(
                "ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            int offset = TimeZone.CurrentTimeZone.GetUtcOffset(date).Hours;
            string zone;
            // + or -, then 0-pad, then offset, then more 0-padding.
            if (offset < 0)
            {
                offset *= -1;
                zone = "-";
            }
            else
            {
                zone = "+";
            }
            zone += offset.ToString(CultureInfo.InvariantCulture).PadLeft(2, '0');
            date_string += " " + zone.PadRight(5, '0');
            return date_string;
        }
    }

    [Serializable]
    public class DuoException : Exception
    {
        public int HttpStatus { get; private set; }

        public DuoException(int http_status, string message, Exception inner)
            : base(message, inner)
        {
            this.HttpStatus = http_status;
        }

        protected DuoException(System.Runtime.Serialization.SerializationInfo info,
                               System.Runtime.Serialization.StreamingContext ctxt)
            : base(info, ctxt)
        { }
    }

    [Serializable]
    public class ApiException : DuoException
    {
        public int Code { get; private set; }
        public string ApiMessage { get; private set; }
        public string ApiMessageDetail { get; private set; }

        public ApiException(int code,
                            int http_status,
                            string api_message,
                            string api_message_detail)
            : base(http_status, FormatMessage(code, api_message, api_message_detail), null)
        {
            this.Code = code;
            this.ApiMessage = api_message;
            this.ApiMessageDetail = api_message_detail;
        }

        protected ApiException(System.Runtime.Serialization.SerializationInfo info,
                               System.Runtime.Serialization.StreamingContext ctxt)
            : base(info, ctxt)
        { }

        private static string FormatMessage(int code,
                                            string api_message,
                                            string api_message_detail)
        {
            return String.Format(
                "Duo API Error {0}: '{1}' ('{2}')", code, api_message, api_message_detail);
        }
    }

    [Serializable]
    public class BadResponseException : DuoException
    {
        public BadResponseException(int http_status, Exception inner)
            : base(http_status, FormatMessage(http_status, inner), inner)
        { }

        protected BadResponseException(System.Runtime.Serialization.SerializationInfo info,
                                       System.Runtime.Serialization.StreamingContext ctxt)
            : base(info, ctxt)
        { }

        private static string FormatMessage(int http_status, Exception inner)
        {
            string inner_message = "(null)";
            if (inner != null)
            {
                inner_message = String.Format("'{0}'", inner.Message);
            }
            return String.Format(
                "Got error {0} with HTTP Status {1}", inner_message, http_status);
        }
    }
}
