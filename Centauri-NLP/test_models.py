#!/usr/bin/env python
# Test the models directly without HTTP
from pydantic import ValidationError
import sys
sys.path.insert(0, 'C:\\Digna\\Centauri-backedn\\Centauri-NLP')

# Import your models
from nlp_service import (
    SectionInputData, 
    RecommendationRequestInput,
    ScoreCard
)
import json

data = {
    "PrimaryKeyword": "1099 Filing Requirements",
    "sections": [
        {
            "SectionText": "Which 1099 Forms Do You Need to File?",
            "Sentences": [
                "Most businesses only deal with one or two 1099 forms, but choosing the wrong one is a common reason filings get flagged.",
                "The IRS separates non-employee compensation from other income types, and each category has its own form and rules."
            ]
        }
    ],
    "Scores": {
        "IntentScore": 10,
        "SectionScore": 1.43,
        "KeywordScore": 3.6,
        "OriginalInfoScore": 6.283783783783784,
        "ExpertiseScore": 4.66,
        "CredibilityScore": 1.7222222222222219,
        "AuthorityScore": 8.229729729729733,
        "SimplicityScore": 6.148648648648649,
        "GrammarScore": 10,
        "VariationScore": 4.250680260775103,
        "PlagiarismScore": 5,
        "ClaritySynthesisScore": 7,
        "FactRetrievalScore": 5,
        "AnswerBlockDensityScore": 6,
        "FactualIsolationScore": 8.06,
        "EntityAlignmentScore": 10,
        "TechnicalClarityScore": 6.77,
        "SignalToNoiseScore": 8.9
    },
    "SearchIntent": "Informational",
    "secondaryKeywords": [],
    "entities": [],
    "previousRecommendations": None
}

print("Testing model validation...")
print("=" * 80)

try:
    print("\n1. Testing SectionInputData...")
    for section in data["sections"]:
        section_obj = SectionInputData(**section)
        print(f"   ✓ SectionInputData created: {section_obj.SectionText}")
    
    print("\n2. Testing RecommendationRequestInput...")
    req = RecommendationRequestInput(**data)
    print(f"   ✓ RecommendationRequestInput created successfully!")
    print(f"   - PrimaryKeyword: {req.PrimaryKeyword}")
    print(f"   - Sections: {len(req.sections)}")
    print(f"   - SearchIntent: {req.SearchIntent}")
    
except ValidationError as e:
    print(f"\n   ✗ Validation Error:")
    print(json.dumps(e.errors(), indent=2))
    sys.exit(1)
except Exception as e:
    print(f"\n   ✗ Error: {type(e).__name__}: {e}")
    import traceback
    traceback.print_exc()
    sys.exit(1)

print("\n" + "=" * 80)
print("✓ All models validated successfully!")
