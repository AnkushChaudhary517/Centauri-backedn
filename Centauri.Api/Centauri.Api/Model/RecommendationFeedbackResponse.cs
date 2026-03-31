using System;

namespace Centauri_Api.Model
{
    /// <summary>
    /// Response model for recommendation feedback submission
    /// </summary>
    public class RecommendationFeedbackResponse
    {
        /// <summary>
        /// Unique identifier for the stored feedback record
        /// </summary>
        public string FeedbackId { get; set; }

        /// <summary>
        /// Status of the feedback submission
        /// Values: "success", "partial", "error"
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Descriptive message about the feedback submission
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Timestamp when the feedback was recorded
        /// </summary>
        public DateTime SubmittedAt { get; set; }

        /// <summary>
        /// Optional error details if submission failed
        /// </summary>
        public string ErrorDetails { get; set; }

        /// <summary>
        /// Request ID for tracking/audit purposes
        /// </summary>
        public string RequestId { get; set; }

        /// <summary>
        /// Recommendation ID for reference
        /// </summary>
        public string RecommendationId { get; set; }
    }
}
