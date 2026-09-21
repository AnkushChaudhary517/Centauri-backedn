using Centauri.ContentArchitect.Backend.Models;
using Centauri.ContentArchitect.Backend.Services.Calculators;
using NUnit.Framework;
using System.Linq;
using System.Collections.Generic;

namespace Centauri.ContentArchitect.Backend.Tests;

public class MetricFormulaTests
{
    //[Test]
    //public void KeywordDifficulty_UsesSpecifiedWeights()
    //{
    //    var calc = new KeywordDifficultyCalculator();
    //    var pages = Enumerable.Range(1, 10).Select(i => new SerpResult
    //    {
    //        Position = i, DomainStrength = 50, ReferringDomains = 1000,
    //        IntentMatch = 1, ContentCoverage = 50
    //    }).ToList();

    //    var r = calc.Calculate(pages);
    //    Assert.That(r.Score, Is.EqualTo(65).Within(5));
    //}

    //[Test]
    //public void QuestionCoverage_IsRankWeighted()
    //{
    //    var calc = new QuestionCoverageCalculator();
    //    var pages = new List<SerpResult>
    //    {
    //        new() { Position = 1, Questions = ["How does CRM work?"] },
    //        new() { Position = 2, Questions = [] },
    //        new() { Position = 3, Questions = [] }
    //    };
    //    var r = calc.Calculate(["How does CRM work?"], pages);
    //    Assert.That(r.Questions[0].Coverage > 0);
    //    Assert.That(r.Questions[0].Coverage < 1);
    //}

    //[Test]
    //public void GapScore_UsesSpecifiedWeights()
    //{
    //    var calc = new ContentGapCalculator();
    //    var r = calc.Calculate(
    //        [new QuestionClassification {
    //            Question = "Q", IntentRelevance = 1, DemandProxy = 1, Uniqueness = 1
    //        }],
    //        [new QuestionCoverageResult { Question = "Q", Coverage = 0 }]);

    //    Assert.That(r.Gaps.Single().Score, Is.EqualTo(100).Within(5));
    //}
}
