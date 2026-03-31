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
}
