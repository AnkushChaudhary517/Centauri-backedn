#!/usr/bin/env python3
"""
Test the /recommendations-input endpoint with the complete user input data
"""
import requests
import json
import time

# Wait for server to start
time.sleep(2)

# Full input data as provided by user
full_input = {
    "PrimaryKeyword": "1099 Filing Requirements",
    "sections": [
        {
            "SectionText": "Which 1099 Forms Do You Need to File?",
            "Sentences": [
                "Most businesses only deal with one or two 1099 forms, but choosing the wrong one is a common reason filings get flagged.",
                "The IRS separates non-employee compensation from other income types, and each category has its own form and rules.",
                "If you pay contractors, landlords, or service providers, understanding where each payment belongs helps you avoid rework, penalties, and follow-up notices."
            ]
        },
        {
            "SectionText": "Form 1099-NEC",
            "Sentences": [
                "Form 1099-NEC is used to report payments made to non-employees for services.",
                "This includes freelancers, independent contractors, consultants, and agency partners who are not on your payroll."
            ]
        },
        {
            "SectionText": "Form 1099-MISC",
            "Sentences": [
                "Form 1099-MISC is used to report specific types of income that are not tied to service-based work.",
                "Common examples include rent paid to property owners, certain legal settlements, prizes or awards, and other miscellaneous income types."
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
        "FactualIsolationScore": 8.06,
        "AnswerBlockDensityScore": 6,
        "EntityAlignmentScore": 10,
        "SignalToNoiseScore": 8.9,
        "TechnicalClarityScore": 6.77,
        "PlagiarismScore": 5,
        "ClaritySynthesisScore": 7,
        "FactRetrievalScore": 5
    },
    "SearchIntent": "Informational",
    "secondaryKeywords": [],
    "entities": [],
    "Sentences": [
        {
            "Text": "What Are the 1099 Filing Requirements for 2025–2026?",
            "HtmlTag": "h1"
        },
        {
            "Text": "For US startups and cross-border teams, 1099 forms connect contractor payments to IRS reporting.",
            "HtmlTag": "p"
        }
    ]
}

try:
    print("=" * 80)
    print("Testing /recommendations-input endpoint with FULL input structure")
    print("=" * 80)
    
    response = requests.post(
        "http://localhost:8000/recommendations-input",
        json=full_input,
        timeout=30
    )
    
    print(f"\n✓ Status Code: {response.status_code}")
    
    if response.status_code == 200:
        result = response.json()
        print(f"\n✓ Successfully received recommendations:")
        print(f"  - Overall recommendations: {len(result.get('overall', []))}")
        print(f"  - Section-level recommendations: {len(result.get('sectionLevel', []))}")
        print(f"  - Sentence-level recommendations: {len(result.get('sentenceLevel', []))}")
        
        # Print first overall recommendation as sample
        if result.get('overall'):
            first_rec = result['overall'][0]
            print(f"\n  Sample Recommendation:")
            print(f"    - What to change: {first_rec.get('whatToChange')}")
            print(f"    - Priority: {first_rec.get('priority')}")
            print(f"    - Description: {first_rec.get('description')[:100]}...")
    else:
        print(f"\n✗ Error Response ({response.status_code}):")
        print(json.dumps(response.json(), indent=2))
        
except requests.exceptions.ConnectionError as e:
    print(f"\n✗ Connection Error: {e}")
    print("   Make sure the server is running: python nlp_service.py")
except requests.exceptions.Timeout as e:
    print(f"\n✗ Timeout Error: {e}")
except Exception as e:
    print(f"\n✗ Error: {type(e).__name__}: {e}")
finally:
    print("\n" + "=" * 80)
