using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.DynamoDBv2.DataModel;

namespace CentauriSeo.Core.Modules.Notification
{
    // Models/EmailVerificationCode.cs
    // Models/EmailVerificationCode.cs



    [DynamoDBTable("EmailVerificationCodes")]
    public class EmailVerificationCode
    {
        [DynamoDBHashKey]
        public string Email { get; set; }

        public string Code { get; set; }

        public long ExpiryTime { get; set; }

        public bool IsUsed { get; set; }
    }
}
