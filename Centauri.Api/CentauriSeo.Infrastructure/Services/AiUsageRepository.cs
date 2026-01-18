using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Infrastructure.Services
{
    using Amazon.DynamoDBv2;
    using Amazon.DynamoDBv2.Model;
    using CentauriSeo.Core.Diagnostic;
    using System.Text.Json;

    public class AiUsageRepository
    {
        private readonly IAmazonDynamoDB _db;
        private const string TableName = "AiUsage";

        public AiUsageRepository(IAmazonDynamoDB db)
        {
            _db = db;
        }

        public async Task SaveAsync(AiUsageRow row)
        {
            var item = new Dictionary<string, AttributeValue>
            {
                ["CorrelationId"] = new AttributeValue { S = row.CorrelationId },
                ["Id"] = new AttributeValue { S = row.Id },

                ["UserId"] = new AttributeValue { S = row.UserId },
                ["Provider"] = new AttributeValue { S = row.Provider },
                ["Endpoint"] = new AttributeValue { S = row.Endpoint },

                ["Usage"] = new AttributeValue { S = row.Usage },
                ["Request"] = new AttributeValue { S = row.Request },
                ["Response"] = new AttributeValue { S = row.Response },

                ["TimeTakenMs"] = new AttributeValue { N = row.TimeTakenMs.ToString() },
                ["Timestamp"] = new AttributeValue { S = row.Timestamp.ToString("o") }
            };

            await _db.PutItemAsync(TableName, item);
        }

        public async Task<List<AiUsageRow>> GetByCorrelationIdAsync(string correlationId)
        {
            var request = new QueryRequest
            {
                TableName = TableName,
                KeyConditionExpression = "CorrelationId = :cid",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":cid", new AttributeValue { S = correlationId } }
                }
            };
            var response = await _db.QueryAsync(request);
            var results = response.Items.Select(item => new AiUsageRow
            {
                CorrelationId = item["CorrelationId"].S,
                Id = item["Id"].S,
                UserId = item["UserId"].S,
                Endpoint = item["Endpoint"].S,
                Usage = item["Usage"].S,
                Request = item["Request"].S,
                Response = item["Response"].S,
                TimeTakenMs = int.Parse(item["TimeTakenMs"].N),
                Timestamp = DateTime.Parse(item["Timestamp"].S)
            }).ToList();
            return results;
        }
    }

}
