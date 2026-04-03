#!/usr/bin/env python3
import requests
import json
import time

# Wait for server to start
time.sleep(2)

# Test data
test_input = {
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
    "entities": []
}

try:
    response = requests.post(
        "http://localhost:8000/recommendations-input",
        json=test_input,
        timeout=10
    )
    
    print(f"Status Code: {response.status_code}")
    print(f"Response Headers: {dict(response.headers)}")
    print(f"\nResponse Body:")
    print(json.dumps(response.json(), indent=2))
    
except requests.exceptions.ConnectionError as e:
    print(f"❌ Connection Error: {e}")
    print("The server is not running on port 8000")
except requests.exceptions.Timeout as e:
    print(f"❌ Timeout Error: {e}")
except Exception as e:
    print(f"❌ Error: {type(e).__name__}: {e}")
