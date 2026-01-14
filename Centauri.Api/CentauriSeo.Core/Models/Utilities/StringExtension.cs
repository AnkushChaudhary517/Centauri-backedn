using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Utilities
{
    public static class StringExtension
    {
        public static string DecodeBase64(this string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            try
            {
                byte[] bytes = Convert.FromBase64String(value);
                string decoded = Encoding.UTF8.GetString(bytes);
                return decoded;
            }
            catch (Exception ex)
            {
                return value;
            }
            
        }
    }
}
