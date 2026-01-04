using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Centauri_Api.Entitites;
using Centauri_Api.Interface;
using static System.Net.Mime.MediaTypeNames;

namespace Centauri_Api.Impl
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
                email,                   // GSI partition key value
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

        public async Task DeleteUserAsync(string userId)
        {
            await _context.DeleteAsync<CentauriUser>(userId);
            _logger.LogInformation("User deleted: {UserId}", userId);
        }
    }
}
