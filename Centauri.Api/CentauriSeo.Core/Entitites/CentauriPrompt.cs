using Amazon.DynamoDBv2.DataModel;

namespace CentauriSeo.Core.Entitites
{
    [DynamoDBTable("CentauriPrompts")]
    public class CentauriPrompt
    {
        [DynamoDBHashKey]
        public string PromptName { get; set; }
        public string Value { get; set; }
    }

    [DynamoDBTable("CentauriPastAnalysis")]
    public class CentauriPastAnalysis
    {
        [DynamoDBHashKey]
        public string UserId { get; set; }
        [DynamoDBRangeKey]
        public string RequestId { get; set; }
        public string Responses { get; set; }
    }
    

    [DynamoDBTable("CentauriCachedContent")]
    public class CentauriCachedContent
    {
        [DynamoDBHashKey]
        public string Name { get; set; }
        public string Value { get; set; }
        public DateTime Expiry { get; set; }
    }
}
