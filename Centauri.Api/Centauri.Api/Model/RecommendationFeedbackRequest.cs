using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Centauri_Api.Model
{
    /// <summary>
    /// Request model for submitting recommendation feedback
    /// Maps to the feedback data sent from the frontend
    /// </summary>
    public class RecommendationFeedbackRequest
    {
        /// <summary>
        /// ID of the recommendation that is being reviewed
        /// Required
        /// </summary>
        [Required(ErrorMessage = "RecommendationId is required")]
        public string RecommendationId { get; set; }

        /// <summary>
        /// Original request ID associated with this recommendation
        /// Required
        /// </summary>
        [Required(ErrorMessage = "RequestId is required")]
        public string RequestId { get; set; }

        /// <summary>
        /// User feedback rating
        /// Value: "up" (helpful) or "down" (not helpful)
        /// Required
        /// </summary>
        [Required(ErrorMessage = "Feedback is required")]
        [RegularExpression("^(up|down)$", ErrorMessage = "Feedback must be 'up' or 'down'")]
        public string Feedback { get; set; }

        /// <summary>
        /// Optional issue or problem description with the recommendation
        /// </summary>
        public string Issue { get; set; }

        /// <summary>
        /// Description of what should be changed or improved
        /// Helpful for "down" feedback
        /// </summary>
        public string WhatToChange { get; set; }

        /// <summary>
        /// Priority level of the recommendation
        /// Values: High, Medium, Low
        /// </summary>
        public string Priority { get; set; }

        /// <summary>
        /// Array of score names that this recommendation improves
        /// Examples: "SimplicityScore", "TechnicalClarityScore", "ReadabilityScore"
        /// </summary>
        public List<string> Improves { get; set; }

        /// <summary>
        /// The original article content before applying the recommendation
        /// </summary>
        public string OriginalArticle { get; set; }

        /// <summary>
        /// The updated article content after applying the recommendation
        /// </summary>
        public string UpdatedArticle { get; set; }

        /// <summary>
        /// Primary keyword for the article being analyzed
        /// </summary>
        public string PrimaryKeyword { get; set; }
    }
}
