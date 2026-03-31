using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Centauri_Api.Interface;
using Centauri_Api.Model;
using CentauriSeo.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Centauri_Api.Impl
{
    /// <summary>
    /// Implementation for handling recommendation feedback
    /// Stores and retrieves feedback data from DynamoDB
    /// </summary>
    public class RecommendationFeedbackService : IRecommendationFeedbackService
    {
        private readonly DynamoDBContext _context;
        private readonly ILogger<RecommendationFeedbackService> _logger;
        private readonly ILlmLogger _llmLogger;

        public RecommendationFeedbackService(
            DynamoDBContext context,
            ILogger<RecommendationFeedbackService> logger,
            ILlmLogger llmLogger
        )
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _llmLogger = llmLogger ?? throw new ArgumentNullException(nameof(llmLogger));
        }

        /// <summary>
        /// Submit feedback for a specific recommendation
        /// </summary>
        public async Task<RecommendationFeedbackResponse> SubmitFeedbackAsync(
            string userId,
            RecommendationFeedbackRequest request,
            string ipAddress = null,
            string userAgent = null
        )
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Validate request
                if (request == null)
                {
                    throw new ArgumentNullException(nameof(request), "Feedback request cannot be null");
                }

                if (string.IsNullOrWhiteSpace(userId))
                {
                    throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
                }

                if (string.IsNullOrWhiteSpace(request.RecommendationId))
                {
                    throw new ArgumentException("RecommendationId is required", nameof(request.RecommendationId));
                }

                if (string.IsNullOrWhiteSpace(request.RequestId))
                {
                    throw new ArgumentException("RequestId is required", nameof(request.RequestId));
                }

                _llmLogger.LogInfo($"📧 Receiving feedback submission for recommendation: {request.RecommendationId}");

                // Create feedback entity
                var feedbackId = Guid.NewGuid().ToString();
                var feedback = new RecommendationFeedback
                {
                    FeedbackId = feedbackId,
                    UserId = userId,
                    RecommendationId = request.RecommendationId,
                    RequestId = request.RequestId,
                    Feedback = request.Feedback?.ToLower() ?? "unknown",
                    Issue = request.Issue ?? string.Empty,
                    WhatToChange = request.WhatToChange ?? string.Empty,
                    Priority = request.Priority ?? "Medium",
                    Improves = request.Improves ?? new List<string>(),
                    OriginalArticle = request.OriginalArticle ?? string.Empty,
                    UpdatedArticle = request.UpdatedArticle ?? string.Empty,
                    PrimaryKeyword = request.PrimaryKeyword ?? string.Empty,
                    SubmittedAt = DateTime.UtcNow,
                    IpAddress = ipAddress ?? "unknown",
                    UserAgent = userAgent ?? "unknown",
                    Status = "submitted",
                    AdminNotes = string.Empty
                };

                // Save to DynamoDB
                await _context.SaveAsync(feedback);

                stopwatch.Stop();
                _llmLogger.LogApiCall(
                    "RecommendationFeedbackService",
                    "SubmitFeedback",
                    stopwatch.ElapsedMilliseconds,
                    true
                );

                _logger.LogInformation(
                    "Feedback submitted successfully. FeedbackId: {FeedbackId}, RecommendationId: {RecommendationId}, UserId: {UserId}, Rating: {Rating}",
                    feedbackId,
                    request.RecommendationId,
                    userId,
                    request.Feedback
                );

                return new RecommendationFeedbackResponse
                {
                    FeedbackId = feedbackId,
                    Status = "success",
                    Message = "Feedback submitted successfully",
                    SubmittedAt = DateTime.UtcNow,
                    RequestId = request.RequestId,
                    RecommendationId = request.RecommendationId
                };
            }
            catch (ArgumentException ex)
            {
                stopwatch.Stop();
                _llmLogger.LogError("Validation error in feedback submission", ex);
                _logger.LogWarning("Feedback validation error: {Message}", ex.Message);

                return new RecommendationFeedbackResponse
                {
                    Status = "error",
                    Message = "Validation error",
                    ErrorDetails = ex.Message,
                    SubmittedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _llmLogger.LogError("Error submitting feedback", ex);
                _logger.LogError(ex, "Error submitting feedback for recommendation: {RecommendationId}", request?.RecommendationId);

                return new RecommendationFeedbackResponse
                {
                    Status = "error",
                    Message = "Failed to submit feedback",
                    ErrorDetails = ex.Message,
                    SubmittedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Retrieve feedback for a specific recommendation
        /// </summary>
        public async Task<List<RecommendationFeedback>> GetFeedbackByRecommendationIdAsync(string recommendationId)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (string.IsNullOrWhiteSpace(recommendationId))
                {
                    throw new ArgumentException($"RecommendationId cannot be null or empty {nameof(recommendationId)}");
                }

                _llmLogger.LogInfo($"📧 Receiving feedback submission for recommendation: {recommendationId}");

                var conditions = new List<ScanCondition>
                {
                    new ScanCondition("RecommendationId", ScanOperator.Equal, recommendationId)
                };

                var results = await _context.ScanAsync<RecommendationFeedback>(conditions).GetRemainingAsync();

                stopwatch.Stop();
                _llmLogger.LogApiCall(
                    "RecommendationFeedbackService",
                    "GetFeedbackByRecommendationId",
                    stopwatch.ElapsedMilliseconds,
                    true
                );

                _logger.LogInformation(
                    "Retrieved {Count} feedback records for recommendation: {RecommendationId}",
                    results.Count,
                    recommendationId
                );

                return results;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _llmLogger.LogError("Error retrieving feedback by recommendation ID", ex);
                _logger.LogError(ex, "Error retrieving feedback for recommendation: {RecommendationId}", recommendationId);
                return new List<RecommendationFeedback>();
            }
        }

        /// <summary>
        /// Retrieve all feedback submitted by a specific user
        /// </summary>
        public async Task<List<RecommendationFeedback>> GetUserFeedbackAsync(string userId)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (string.IsNullOrWhiteSpace(userId))
                {
                    throw new ArgumentException("UserId cannot be null or empty", nameof(userId));
                }

                _llmLogger.LogInfo($"📧 Receiving feedback submission for recommendation: {userId}");

                var results = await _context.QueryAsync<RecommendationFeedback>(userId).GetRemainingAsync();

                stopwatch.Stop();
                _llmLogger.LogApiCall(
                    "RecommendationFeedbackService",
                    "GetUserFeedback",
                    stopwatch.ElapsedMilliseconds,
                    true
                );

                _logger.LogInformation("Retrieved {Count} feedback records for user: {UserId}", results.Count, userId);

                return results;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _llmLogger.LogError("Error retrieving user feedback", ex);
                _logger.LogError(ex, "Error retrieving feedback for user: {UserId}", userId);
                return new List<RecommendationFeedback>();
            }
        }

        /// <summary>
        /// Retrieve feedback for a specific request
        /// </summary>
        public async Task<List<RecommendationFeedback>> GetFeedbackByRequestIdAsync(string requestId)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (string.IsNullOrWhiteSpace(requestId))
                {
                    throw new ArgumentException("RequestId cannot be null or empty", nameof(requestId));
                }

                _llmLogger.LogInfo($"📧 Receiving feedback submission for recommendation: {requestId}");

                var conditions = new List<ScanCondition>
                {
                    new ScanCondition("RequestId", ScanOperator.Equal, requestId)
                };

                var results = await _context.ScanAsync<RecommendationFeedback>(conditions).GetRemainingAsync();

                stopwatch.Stop();
                _llmLogger.LogApiCall(
                    "RecommendationFeedbackService",
                    "GetFeedbackByRequestId",
                    stopwatch.ElapsedMilliseconds,
                    true
                );

                _logger.LogInformation("Retrieved {Count} feedback records for request: {RequestId}", results.Count, requestId);

                return results;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _llmLogger.LogError("Error retrieving feedback by request ID", ex);
                _logger.LogError(ex, "Error retrieving feedback for request: {RequestId}", requestId);
                return new List<RecommendationFeedback>();
            }
        }

        /// <summary>
        /// Update the status of a feedback record
        /// </summary>
        public async Task<RecommendationFeedback> UpdateFeedbackStatusAsync(
            string feedbackId,
            string userId,
            string newStatus,
            string adminNotes = null
        )
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (string.IsNullOrWhiteSpace(feedbackId) || string.IsNullOrWhiteSpace(userId))
                {
                    throw new ArgumentException("FeedbackId and UserId are required");
                }

                _llmLogger.LogInfo($"✏️ Updating feedback status. FeedbackId: {feedbackId}");

                // Retrieve the feedback
                var feedback = await _context.LoadAsync<RecommendationFeedback>(feedbackId, userId);

                if (feedback == null)
                {
                    throw new KeyNotFoundException($"Feedback not found: {feedbackId} newStatus:{newStatus}");
                }

                // Update status and notes
                feedback.Status = newStatus??feedback.Status;
                feedback.AdminNotes = adminNotes ?? feedback.AdminNotes;

                // Save updated feedback
                await _context.SaveAsync(feedback);

                stopwatch.Stop();
                _llmLogger.LogApiCall(
                    "RecommendationFeedbackService",
                    "UpdateFeedbackStatus",
                    stopwatch.ElapsedMilliseconds,
                    true
                );

                _logger.LogInformation($"Feedback status updated. FeedbackId: {feedbackId}");

                return feedback;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _llmLogger.LogError("Error updating feedback status", ex);
                _logger.LogError(ex, "Error updating feedback status for ID: {FeedbackId}", feedbackId);
                throw;
            }
        }

        /// <summary>
        /// Get feedback statistics for analytics
        /// </summary>
        public async Task<FeedbackStatistics> GetFeedbackStatisticsAsync()
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _llmLogger.LogInfo("📊 Calculating feedback statistics");

                var allFeedback = await _context.ScanAsync<RecommendationFeedback>(new List<ScanCondition>()).GetRemainingAsync();

                var stats = new FeedbackStatistics
                {
                    TotalFeedback = allFeedback.Count,
                    PositiveFeedback = allFeedback.Count(f => f.Feedback == "up"),
                    NegativeFeedback = allFeedback.Count(f => f.Feedback == "down"),
                    CommonIssues = allFeedback
                        .Where(f => !string.IsNullOrWhiteSpace(f.Issue))
                        .GroupBy(f => f.Issue)
                        .ToDictionary(g => g.Key, g => g.Count())
                        .OrderByDescending(x => x.Value)
                        .Take(10)
                        .ToDictionary(x => x.Key, x => x.Value),
                    FeedbackByPriority = allFeedback
                        .Where(f => !string.IsNullOrWhiteSpace(f.Priority))
                        .GroupBy(f => f.Priority)
                        .ToDictionary(g => g.Key, g => g.Count())
                };

                stats.PositivePercentage = stats.TotalFeedback > 0
                    ? Math.Round((double)stats.PositiveFeedback / stats.TotalFeedback * 100, 2)
                    : 0;

                stopwatch.Stop();
                _llmLogger.LogApiCall(
                    "RecommendationFeedbackService",
                    "GetFeedbackStatistics",
                    stopwatch.ElapsedMilliseconds,
                    true
                );

                _logger.LogInformation(
                    "Feedback statistics calculated. Total: {Total}, Positive: {Positive}%, Negative: {Negative}%",
                    stats.TotalFeedback,
                    stats.PositivePercentage,
                    100 - stats.PositivePercentage
                );

                return stats;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _llmLogger.LogError("Error calculating feedback statistics", ex);
                _logger.LogError(ex, "Error calculating feedback statistics");
                return new FeedbackStatistics { TotalFeedback = 0 };
            }
        }

        /// <summary>
        /// Delete a feedback record
        /// </summary>
        public async Task<bool> DeleteFeedbackAsync(string feedbackId, string userId)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (string.IsNullOrWhiteSpace(feedbackId) || string.IsNullOrWhiteSpace(userId))
                {
                    throw new ArgumentException("FeedbackId and UserId are required");
                }

                _llmLogger.LogInfo($"📧 Receiving feedback submission for recommendation: {feedbackId}");

                await _context.DeleteAsync<RecommendationFeedback>(feedbackId, userId);

                stopwatch.Stop();
                _llmLogger.LogApiCall(
                    "RecommendationFeedbackService",
                    "DeleteFeedback",
                    stopwatch.ElapsedMilliseconds,
                    true
                );

                _logger.LogInformation("Feedback deleted. FeedbackId: {FeedbackId}", feedbackId);

                return true;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _llmLogger.LogError("Error deleting feedback", ex);
                _logger.LogError(ex, "Error deleting feedback: {FeedbackId}", feedbackId);
                return false;
            }
        }
    }
}
