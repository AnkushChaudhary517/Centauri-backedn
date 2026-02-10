from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import List, Dict
import spacy
import re

# Model Load
nlp = spacy.load("en_core_web_lg")
app = FastAPI(title="SEO Section-Based Analyzer")

class ContentItem(BaseModel):
    s_id: int
    text: str
    tag: str

class AnalysisRequest(BaseModel):
    keyword: str
    content: List[ContentItem]

def identify_source_type_semantic(text_lower, subjects):
    """Semantic logic for source attribution based on block content."""
    # Third Party: Regulatory/IRS content [cite: 14, 22, 38]
    regulatory_kws = {"irs", "tax", "form", "rule", "penalty", "law", "deadline"}
    if any(kw in text_lower for kw in regulatory_kws):
        if not any(s in {"we", "our", "i"} for s in subjects):
            return "ThirdParty"
    
    # First Party: Brand/Inkle specific actions 
    brand_kws = {"inkle", "book a demo", "our platform", "we help"}
    if any(bk in text_lower for bk in brand_kws) or "we" in subjects:
        return "FirstParty"
        
    return "Unknown"

def get_logical_sections(content: List[ContentItem]):
    """Merges consecutive P tags into one block until a new Heading or Section starts."""
    sections = []
    current_text = ""
    current_tags = []
    current_ids = []

    for item in content:
        # Agar naya Heading tag aaye toh pichla section close karo [cite: 1, 12, 37]
        if item.tag.startswith('h') and current_text:
            sections.append({"text": current_text.strip(), "tag": current_tags[0], "ids": current_ids})
            current_text = ""
            current_ids = []
            current_tags = []

        current_text += " " + item.text
        current_tags.append(item.tag)
        current_ids.append(item.s_id)

    if current_text:
        sections.append({"text": current_text.strip(), "tag": current_tags[0], "ids": current_ids})
    return sections

@app.post("/analyze-seo")
async def analyze_seo(request: AnalysisRequest):
    keyword_doc = nlp(request.keyword)
    sections = get_logical_sections(request.content)
    final_results = []
    first_answer_idx = -1

    for idx, sec in enumerate(sections):
        doc = nlp(sec['text'])
        text_lower = sec['text'].lower()
        subjects = [t.text.lower() for t in doc if "subj" in t.dep_]
        
        # Relevance & Source
        relevance = doc.similarity(keyword_doc) if doc.vector_norm else 0
        source = identify_source_type_semantic(text_lower, subjects)
        
        # Intent: Definition/Fact recognition [cite: 17, 28, 45]
        is_answer = 0
        if relevance > 0.60 and not text_lower.endswith('?'):
            is_answer = 1
            if first_answer_idx == -1: first_answer_idx = idx

        final_results.append({
            "section_id": idx,
            "html_tag": sec['tag'],
            "text": sec['text'],
            "source": source,
            "relevance": round(relevance, 4),
            "is_answer": is_answer
        })

    # Answer Block Density Calculation
    base_density = 0.0
    if first_answer_idx != -1:
        # Logic: Top 5 blocks are high density [cite: 5, 40]
        if first_answer_idx <= 2: base_density = 3.33
        elif first_answer_idx <= 5: base_density = 2.0
        else: base_density = 1.0

    return {
        "keyword": request.keyword,
        "answer_block_density_score": round(base_density * 3, 2),
        "total_sections_analyzed": len(final_results),
        "results": final_results
    }