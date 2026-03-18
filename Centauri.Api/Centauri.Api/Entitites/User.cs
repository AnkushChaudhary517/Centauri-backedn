using Amazon.DynamoDBv2.DataModel;

namespace Centauri_Api.Entitites
{


    [DynamoDBTable("CentauriUser")]
    public class CentauriUser
    {
        [DynamoDBHashKey]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        [DynamoDBRangeKey] // Sort key
                           //[DynamoDBProperty]
        public string Email { get; set; }
        [DynamoDBProperty]
        public string FirstName { get; set; } = string.Empty;
        [DynamoDBProperty]
        public string LastName { get; set; } = string.Empty;
        [DynamoDBProperty]
        public string PasswordHash { get; set; } = string.Empty;
        [DynamoDBProperty]
        public bool EmailVerified { get; set; } = false;


        public string? VerificationToken { get; set; }
        public string? ResetToken { get; set; }
        public DateTime? ResetTokenExpiry { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string Plan { get; set; } = "Free";
        public DateTime TrialEndsAt { get; set; } = DateTime.UtcNow.AddDays(14);
        public DateTime SubscriptionEndsAt { get; set; }
        public int CreditsAdded { get; set; }
        public string ContactNumber { get; set; }
        public string Company { get; set; }
    }

    [DynamoDBTable("CentauriArticle")]
    public class CentauriArticle
    {
        [DynamoDBHashKey]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        [DynamoDBRangeKey] 
        public string UserId { get; set; } = Guid.NewGuid().ToString();
        public string FirstName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    [DynamoDBTable("CentauriPayment")]
    public class CentauriPayment
    {
        [DynamoDBHashKey]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        [DynamoDBRangeKey] 
        public string UserId { get; set; } = Guid.NewGuid().ToString();
        public double Amount { get; set; }
        public string Type { get; set; }
        public int CreditsAdded { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
