using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using CentauriSeo.Core.Entitites;
using Microsoft.Extensions.Logging;
using static System.Net.Mime.MediaTypeNames;

namespace CentauriSeo.Infrastructure.Services
{
    public class DynamoDbService : IDynamoDbService
    {
        private readonly DynamoDBContext _context;
        private readonly ILogger<DynamoDbService> _logger;

        public DynamoDbService(IAmazonDynamoDB dynamoDb, ILogger<DynamoDbService> logger)
        {
            _context = new DynamoDBContext(dynamoDb);
            _logger = logger;
        }

        public async Task CreateUserSubscription(CentauriUserSubscription centauriUserSubscription)
        {
            // Create new subscription
            centauriUserSubscription.UserId = centauriUserSubscription.UserId; // Ensure UserId is set
            await _context.SaveAsync(centauriUserSubscription);
            _logger.LogInformation("User subscription created: {UserId}", centauriUserSubscription.UserId);
        }
        public async Task CreateUserAsync(CentauriUser user)
        {
            user.Id = Guid.NewGuid().ToString();
            await _context.SaveAsync(user);
            _logger.LogInformation("User created: {UserId}", user.Id);
        }

        public async Task<CentauriUser?> GetUserAsync(string userId)
        {

            var conditions = new List<ScanCondition>
            {
                new ScanCondition("Id", ScanOperator.Equal, userId)
            };

            var results = await _context.ScanAsync<CentauriUser>(conditions).GetRemainingAsync();
            var user = results.FirstOrDefault();
            return user;
        }

        public async Task<CentauriUser?> GetUserByEmail(string email)
        {

            var queryConfig = new DynamoDBOperationConfig
            {
                IndexName = "Email-Index"
            };

            var results = await _context.QueryAsync<CentauriUser>(
                email?.ToLower(),                   // GSI partition key value
                queryConfig
            ).GetRemainingAsync();

            var user = results.FirstOrDefault();
            return user;
        }

        public async Task<List<CentauriUser>> GetAllUsersAsync()
        {

            var conditions = new List<ScanCondition>();
            var users = await _context.ScanAsync<CentauriUser>(conditions).GetRemainingAsync();
            return users;
        }

        public async Task UpdateUserAsync(CentauriUser user)
        {
            await _context.SaveAsync(user);
            _logger.LogInformation("User updated: {UserId}", user.Id);
        }

        public async Task DeleteUserAsync(string userId, string email)
        {
            await _context.DeleteAsync<CentauriUser>(userId,email);
            _logger.LogInformation("User deleted: {UserId}", userId);
        }

        public async Task<string> GetPrompt(string promptName)
        {
            try
            {
                var conditions = new List<ScanCondition>
            {
                new ScanCondition("PromptName", ScanOperator.Equal, promptName)
            };

                var results = await _context.ScanAsync<CentauriPrompt>(conditions).GetRemainingAsync();
                var prompt = results.FirstOrDefault();
                return prompt?.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving prompt: {PromptName}", promptName);
                return null;
            }
            
        }
    }
}
