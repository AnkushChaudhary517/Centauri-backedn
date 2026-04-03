#!/usr/bin/env python3
"""Test with exact user input - all floats in Scores"""
import requests
import json
import time

time.sleep(2)

# EXACT input from user - with FLOAT scores
data = {
    "PrimaryKeyword": "1099 Filing Requirements",
    "sections": [
        {"SectionText": "Which 1099 Forms Do You Need to File?",
         "Sentences": ["Most businesses only deal with one or two 1099 forms, but choosing the wrong one is a common reason filings get flagged."]},
        {"SectionText": "Form 1099-NEC",
         "Sentences": ["Form 1099-NEC is used to report payments made to non-employees for services."]}
    ],
    "Scores": {
        "IntentScore": 10,
        "SectionScore": 1.43,  # FLOAT
        "KeywordScore": 3.6,   # FLOAT
        "OriginalInfoScore": 6.283783783783784,  # FLOAT
        "ExpertiseScore": 4.66,  # FLOAT
        "CredibilityScore": 1.7222222222222219,  # FLOAT
        "AuthorityScore": 8.229729729729733,  # FLOAT
        "SimplicityScore": 6.148648648648649,  # FLOAT
        "GrammarScore": 10,
        "VariationScore": 4.250680260775103,  # FLOAT
        "PlagiarismScore": 5,
        "ClaritySynthesisScore": 7,
        "FactRetrievalScore": 5,
        "AnswerBlockDensityScore": 6,
        "FactualIsolationScore": 8.06,  # FLOAT
        "EntityAlignmentScore": 10,
        "SignalToNoiseScore": 8.9,  # FLOAT
        "TechnicalClarityScore": 6.77  # FLOAT
    },
    "SearchIntent": "Informational",
    "secondaryKeywords": [],
    "entities": []
}

try:
    print("="*80)
    print("Testing with EXACT user input (FLOAT scores)")
    print("="*80)
    
    response = requests.post(
        "http://localhost:8000/recommendations-input",
        json=data,
        timeout=30
    )
    
    print(f"\nStatus Code: {response.status_code}")
    
    if response.status_code == 200:
        result = response.json()
        print("\n✓ SUCCESS! Recommendations generated:")
        print(f"  - Overall: {len(result.get('overall', []))} recommendations")
        print(f"  - Section-level: {len(result.get('sectionLevel', []))} recommendations")
        print(f"  - Sentence-level: {len(result.get('sentenceLevel', []))} recommendations")
        print("\n✓ Your input format is now working!")
    else:
        print(f"\n✗ Error ({response.status_code}):")
        error_msg = response.json()
        print(json.dumps(error_msg, indent=2))
        
except Exception as e:
    print(f"\n✗ {type(e).__name__}: {e}")
finally:
    print("="*80)
