using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Diagnostic
{
    public class AiUsageRow
    {
        public string Id { get; set; }              // per API call GUID
        public string CorrelationId { get; set; }   // per frontend request GUID

        public string Usage { get; set; }           // JSON
        public string Request { get; set; }         // JSON
        public string Response { get; set; }        // JSON

        public long TimeTakenMs { get; set; }
        public string UserId { get; set; }
        public string Endpoint { get; set; }
        public string Provider { get; set; }

        public DateTime Timestamp { get; set; }
    }


}
