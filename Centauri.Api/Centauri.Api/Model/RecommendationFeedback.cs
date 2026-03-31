using Amazon.DynamoDBv2.DataModel;
using System;
using System.Collections.Generic;

namespace Centauri_Api.Model
{
    /// <summary>
    /// DynamoDB entity for storing user feedback on recommendations
    /// </summary>
    [DynamoDBTable("RecommendationFeedback")]
    public class RecommendationFeedback
    {
        /// <summary>
        /// Partition Key: Unique identifier for the feedback record
        /// </summary>
        [DynamoDBHashKey("FeedbackId")]
        public string FeedbackId { get; set; }

        /// <summary>
        /// Sort Key: UserId to group feedback by user
        /// </summary>
        [DynamoDBRangeKey("UserId")]
        public string UserId { get; set; }

        /// <summary>
        /// ID of the recommendation that was provided
        /// </summary>
        [DynamoDBProperty]
        public string RecommendationId { get; set; }

        /// <summary>
        /// Original request ID associated with this recommendation
        /// </summary>
        [DynamoDBProperty]
        public string RequestId { get; set; }

        /// <summary>
        /// Feedback rating: "up" (helpful/positive), "down" (not helpful/negative)
        /// </summary>
        [DynamoDBProperty]
        public string Feedback { get; set; }

        /// <summary>
        /// Optional: Issue or problem with the recommendation
        /// </summary>
        [DynamoDBProperty]
        public string Issue { get; set; }

        /// <summary>
        /// Description of what needs to be changed (if feedback is down)
        /// </summary>
        [DynamoDBProperty]
        public string WhatToChange { get; set; }

        /// <summary>
        /// Priority level of the recommendation: High, Medium, Low
        /// </summary>
        [DynamoDBProperty]
        public string Priority { get; set; }

        /// <summary>
        /// Array of scores that this recommendation improves
        /// Examples: "SimplicityScore", "TechnicalClarityScore", "ReadabilityScore"
        /// </summary>
        [DynamoDBProperty]
        public List<string> Improves { get; set; }

        /// <summary>
        /// The original article content before any changes
        /// </summary>
        [DynamoDBProperty]
        public string OriginalArticle { get; set; }

        /// <summary>
        /// The updated article content after applying the recommendation
        /// </summary>
        [DynamoDBProperty]
        public string UpdatedArticle { get; set; }

        /// <summary>
        /// Primary keyword for the article being analyzed
        /// </summary>
        [DynamoDBProperty]
        public string PrimaryKeyword { get; set; }

        /// <summary>
        /// Timestamp when the feedback was submitted
        /// </summary>
        [DynamoDBProperty]
        public DateTime SubmittedAt { get; set; }

        /// <summary>
        /// Email of the user providing feedback
        /// </summary>
        [DynamoDBProperty]
        public string UserEmail { get; set; }

        /// <summary>
        /// IP address from where the feedback was submitted
        /// </summary>
        [DynamoDBProperty]
        public string IpAddress { get; set; }

        /// <summary>
        /// User agent/browser information
        /// </summary>
        [DynamoDBProperty]
        public string UserAgent { get; set; }

        /// <summary>
        /// Status of the feedback: "submitted", "reviewed", "processed"
        /// </summary>
        [DynamoDBProperty]
        public string Status { get; set; }

        /// <summary>
        /// Admin notes or comments on this feedback
        /// </summary>
        [DynamoDBProperty]
        public string AdminNotes { get; set; }
    }
}
