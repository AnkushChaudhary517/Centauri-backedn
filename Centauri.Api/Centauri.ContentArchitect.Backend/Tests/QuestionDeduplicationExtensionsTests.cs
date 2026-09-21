using Centauri.ContentArchitect.Backend.Extensions;
using NUnit.Framework;

namespace Centauri.ContentArchitect.Backend.Tests;

public class QuestionDeduplicationExtensionsTests
{
    [Test]
    public void DeduplicateQuestions_RemovesParaphrasesWithinAndAcrossLists_KeepingFirstList()
    {
        var competitorQuestions = new[]
        {
            "What are the essential CRM features every business should look for?",
            "How does a CRM work?",
            "What are the 4 types of CRM?"
        };
        var additionalQuestions = new[]
        {
            "What should I look for when choosing a CRM system?",
            "What are the main functions of a CRM system?",
            "What are the 5 types of CRM?"
        };

        var result = new[] { competitorQuestions, additionalQuestions }.DeduplicateQuestions();

        Assert.That(result, Is.EqualTo(new[]
        {
            "What are the essential CRM features every business should look for?",
            "How does a CRM work?",
            "What are the 4 types of CRM?",
            "What should I look for when choosing a CRM system?"
        }));
    }

    [Test]
    public void DeduplicateQuestions_PreservesDifferentIntentsForTheSameTopic()
    {
        var result = new[]
        {
            new[] { "What are the main features of CRM tools?" },
            new[] { "How do I choose the right CRM tool?", "How much does CRM software cost?" }
        }.DeduplicateQuestions();

        Assert.That(result.Count, Is.EqualTo(3));
    }
}
