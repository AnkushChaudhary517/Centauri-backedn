using Centauri_Api.Model;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Centauri_Api.Interface
{
    /// <summary>
    /// Interface for handling recommendation feedback operations
    /// Manages storage, retrieval, and analysis of user feedback
    /// </summary>
    public interface IRecommendationFeedbackService
    {
        /// <summary>
        /// Submit feedback for a specific recommendation
        /// </summary>
        /// <param name="userId">User ID of the person submitting feedback</param>
        /// <param name="request">The feedback request data</param>
        /// <param name="ipAddress">IP address of the request</param>
        /// <param name="userAgent">User agent/browser info</param>
        /// <returns>Response containing feedback ID and status</returns>
        Task<RecommendationFeedbackResponse> SubmitFeedbackAsync(
            string userId,
            RecommendationFeedbackRequest request,
            string ipAddress = null,
            string userAgent = null
        );

        /// <summary>
        /// Retrieve feedback for a specific recommendation
        /// </summary>
        /// <param name="recommendationId">ID of the recommendation</param>
        /// <returns>List of feedback records for the recommendation</returns>
        Task<List<RecommendationFeedback>> GetFeedbackByRecommendationIdAsync(string recommendationId);

        /// <summary>
        /// Retrieve all feedback submitted by a specific user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>List of all feedback submitted by the user</returns>
        Task<List<RecommendationFeedback>> GetUserFeedbackAsync(string userId);

        /// <summary>
        /// Retrieve feedback for a specific request
        /// </summary>
        /// <param name="requestId">Original SEO analysis request ID</param>
        /// <returns>List of feedback records for the request</returns>
        Task<List<RecommendationFeedback>> GetFeedbackByRequestIdAsync(string requestId);

        /// <summary>
        /// Update the status of a feedback record (admin operation)
        /// </summary>
        /// <param name="feedbackId">ID of the feedback to update</param>
        /// <param name="userId">User ID (for DynamoDB composite key)</param>
        /// <param name="newStatus">New status value</param>
        /// <param name="adminNotes">Optional admin notes</param>
        /// <returns>Updated feedback record</returns>
        Task<RecommendationFeedback> UpdateFeedbackStatusAsync(
            string feedbackId,
            string userId,
            string newStatus,
            string adminNotes = null
        );

        /// <summary>
        /// Get feedback statistics for analytics purposes
        /// </summary>
        /// <returns>Aggregated feedback statistics</returns>
        Task<FeedbackStatistics> GetFeedbackStatisticsAsync();

        /// <summary>
        /// Delete a feedback record (admin operation)
        /// </summary>
        /// <param name="feedbackId">ID of the feedback to delete</param>
        /// <param name="userId">User ID (for DynamoDB composite key)</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> DeleteFeedbackAsync(string feedbackId, string userId);
    }

    /// <summary>
    /// Aggregated feedback statistics for analytics
    /// </summary>
    public class FeedbackStatistics
    {
        /// <summary>
        /// Total number of feedback records
        /// </summary>
        public int TotalFeedback { get; set; }

        /// <summary>
        /// Number of positive feedback (up)
        /// </summary>
        public int PositiveFeedback { get; set; }

        /// <summary>
        /// Number of negative feedback (down)
        /// </summary>
        public int NegativeFeedback { get; set; }

        /// <summary>
        /// Positive feedback percentage
        /// </summary>
        public double PositivePercentage { get; set; }

        /// <summary>
        /// Most frequently mentioned issues
        /// </summary>
        public Dictionary<string, int> CommonIssues { get; set; }

        /// <summary>
        /// Feedback counts by priority level
        /// </summary>
        public Dictionary<string, int> FeedbackByPriority { get; set; }
    }
}
