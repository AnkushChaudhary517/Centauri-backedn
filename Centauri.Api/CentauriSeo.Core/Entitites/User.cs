using Amazon.DynamoDBv2.DataModel;

namespace CentauriSeo.Core.Entitites
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
        public string Subscription { get; set; }
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

    // Map this class to your DynamoDB table
    [DynamoDBTable("CentauriUserSubscriptions")]
    public class CentauriUserSubscription
    {
        // Primary key (Partition Key)
        [DynamoDBHashKey]
        public string UserId { get; set; }

        // Sort Key (if you have one; otherwise omit)
        //[DynamoDBRangeKey]
        //public string SubscriptionId { get; set; }

        // User email
        [DynamoDBProperty]
        public string Email { get; set; }

        // User first name
        [DynamoDBProperty]
        public string FirstName { get; set; }
        

        // Subscription start date
        [DynamoDBProperty]
        public DateTime TrialStartAt { get; set; }
        [DynamoDBProperty]
        public DateTime Reminder48hAt { get; set; }

        // Subscription end date
        [DynamoDBProperty]
        public DateTime TrialEndsAt { get; set; }

        // Status of subscription (active, expired, etc.)
        [DynamoDBProperty]
        public string Status { get; set; }

        // Indicates whether mid-trial email was sent
        [DynamoDBProperty]
        public bool MidEmailSent { get; set; }
        [DynamoDBProperty]
        public bool Reminder48hSent { get; set; }

        // Indicates whether 48-hour reminder email was sent
        [DynamoDBProperty]
        public bool TrialEndedEmailSent { get; set; }
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
