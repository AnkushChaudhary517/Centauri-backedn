import requests
import spacy
import json
from collections import Counter
import re
import numpy as np
from bs4 import BeautifulSoup
from fastapi import FastAPI, Body
from pydantic import BaseModel
from typing import List, Optional, Dict, Any
from enum import Enum
from sentence_transformers import SentenceTransformer, util

# --- 1. INITIALIZATION ---
try:
    nlp = spacy.load("en_core_web_lg")
except:
    print("FATAL: Please run 'python -m spacy download en_core_web_lg' in terminal.")

app = FastAPI(title="Centauri Pro NLP Service - Full Merged Version")
model = SentenceTransformer('all-mpnet-base-v2')

URL_PATTERN = re.compile(r'http[s]?://(?:[a-zA-Z]|[0-9]|[$-_@.&+]|[!*\(\),]|(?:%[0-9a-fA-F][0-9a-fA-F]))+')

# --- 2. MODELS & ENUMS ---
class Competitor(BaseModel):
    Url: str
    Headings: List[str]
    Intent: int

class CompetitorAnalysisRequest(BaseModel):
    data: List[Competitor]
class ArticleRequest(BaseModel):
    htmlContent: str
    primaryKeyword: str

class SimilarityRequest(BaseModel):
    text1: str
    text2: str

class SimilarityResponse(BaseModel):
    similarity: float

class SimilarityItem(BaseModel):
    text1: str
    text2: str

class SimilarityBatchRequest(BaseModel):
    items: List[SimilarityItem]

class SimilarityBatchResponse(BaseModel):
    similarities: List[float]

class InformativeType(str, Enum):
    FACT = "Fact"
    STATISTIC = "Statistic"
    DEFINITION = "Definition"
    CLAIM = "Claim"
    OBSERVATION = "Observation"
    OPINION = "Opinion"
    PREDICTION = "Prediction"
    SUGGESTION = "Suggestion"
    QUESTION = "Question"
    TRANSITION = "Transition"
    FILLER = "Filler"
    UNCERTAIN = "Uncertain"

class SentenceInput(BaseModel):
    Id: str
    Text: str

class AnalysisRequest(BaseModel):
    sentences: List[SentenceInput]
    primaryKeyword: str

class EntityMentionFlag(BaseModel):
    value: int
    entity_count: int
    entities: List[str]

class SentenceOutput(BaseModel):
    SentenceId: str
    Sentence: str
    HtmlTag: Optional[str] = None
    ParagraphId: Optional[str] = None
    FunctionalType: str
    InformativeType: InformativeType
    Structure: str
    Voice: str
    InfoQuality: str
    ClaritySynthesisType: str
    ClaimsCitation: bool
    IsGrammaticallyCorrect: bool
    HasPronoun: bool
    EntityCount: int
    RelevanceScore: float
    answerSentenceFlag: int
    entityMentionFlag: EntityMentionFlag
    entityConfidenceFlag: int

class AnalysisResponse(BaseModel):
    sentences: List[SentenceOutput]
    answerPositionIndex: Optional[str] = None

def compute_similarity(text1: str, text2: str) -> float:
    doc1 = nlp(text1)
    doc2 = nlp(text2)
    tokens1 = [t.vector for t in doc1 if not t.is_stop and not t.is_punct and t.has_vector]
    tokens2 = [t.vector for t in doc2 if not t.is_stop and not t.is_punct and t.has_vector]
    if not tokens1 or not tokens2:
        return 0.0
    v1 = np.mean(tokens1, axis=0)
    v2 = np.mean(tokens2, axis=0)
    norm1 = np.linalg.norm(v1)
    norm2 = np.linalg.norm(v2)
    if norm1 == 0 or norm2 == 0:
        return 0.0
    return float(np.dot(v1, v2) / (norm1 * norm2))

def is_block_element(tag):
    return tag.name in ['h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'p', 'li', 'td', 'th']

def is_self_contained(doc) -> bool:
    pronouns = {"it", "this", "that", "these", "those", "they", "them"}
    starts_with_pronoun = any(t.lower_ in pronouns for t in doc[:2])
    forward_refs = ["mentioned above", "as stated", "previous", "foregoing"]
    has_ref = any(ref in doc.text.lower() for ref in forward_refs)
    return not (starts_with_pronoun or has_ref)

def detect_structure_advanced(doc) -> str:
    verbs = [t for t in doc if t.pos_ in ("VERB", "AUX")]
    if not verbs: return "Fragment"
    ic_count = sum(1 for t in doc if t.dep_ == "ROOT" or (t.dep_ == "conj" and t.head.pos_ in ("VERB", "AUX")))
    dc_count = sum(1 for t in doc if t.dep_ in ("advcl", "relcl", "ccomp"))
    if ic_count >= 2 and dc_count >= 1: return "CompoundComplex"
    if ic_count >= 1 and dc_count >= 1: return "Complex"
    if ic_count >= 2: return "Compound"
    return "Simple" if ic_count == 1 else "Fragment"

def detect_clarity_synthesis(doc, voice: str, structure: str, info_type: InformativeType) -> str:
    text_lower = doc.text.lower()
    if info_type == InformativeType.FILLER or structure == "Fragment": return "UnIndexable"
    if any(p in text_lower for p in ["as we can see", "look at this"]): return "UnIndexable"
    modifiers = [t for t in doc if t.pos_ in ("ADJ", "ADV")]
    if len(doc) > 25 or (len(doc) > 15 and len(modifiers) > 5): return "LowClarity"
    if voice == "Active" and structure in ("Simple", "Compound") and len(doc) < 15: return "Focused"
    return "ModerateComplexity"

def classify_informative_type_merged(doc) -> InformativeType:
    text_lower = doc.text.lower().strip()
    if text_lower.endswith("?") or any(t.lower_ in {"what", "how", "why"} for t in doc[:1]): return InformativeType.QUESTION
    noise_markers = ("meta title", "meta description", "url :", "/*", "visual suggestion")
    if text_lower.startswith(noise_markers) or len(doc) < 4: return InformativeType.FILLER
    if any(t.text.lower() in {"might", "could", "may", "perhaps"} for t in doc): return InformativeType.UNCERTAIN
    if any(ent.label_ in {"PERCENT", "MONEY"} for ent in doc.ents): return InformativeType.STATISTIC
    if any(p in text_lower for p in ["is defined as", "refers to"]): return InformativeType.DEFINITION
    return InformativeType.FACT if any(ent.label_ in {"DATE", "GPE", "LAW"} for ent in doc.ents) else InformativeType.CLAIM

def detect_info_quality_merged(doc, text: str) -> str:
    if re.search(r"\b(according to|as per|reports that)\b", text.lower()): return "Derived"
    if any(ent.label_ in {"ORG", "GPE", "LAW"} for ent in doc.ents): return "WellKnown"
    return "PartiallyKnown"

def check_grammar_heuristics(doc, text: str) -> bool:
    if not text or len(text) < 2: return False
    has_root_verb = any(t.dep_ == "ROOT" and (t.pos_ in ["VERB", "AUX"]) for t in doc)
    return has_root_verb and text[0].isupper() and text[-1] in ".?!\""

# --- 4. ENGINE: analyze_single_sentence ---

def analyze_logic(text: str, s_id: str, keyword_doc, state: Dict, h_tag: str = None, p_id: str = None) -> SentenceOutput:
    doc = nlp(text)
    info_type = classify_informative_type_merged(doc)
    voice = "Passive" if any(t.dep_ == "auxpass" for t in doc) else "Active"
    struct = detect_structure_advanced(doc)
    relevance = doc.similarity(keyword_doc) if doc.vector_norm and keyword_doc.vector_norm else 0.0

    # State Tracking
    subjects = [t.text.lower() for t in doc if "subj" in t.dep_]
    starts_with_pronoun = any(t.lower_ in {"it", "this", "that", "these"} for t in doc[:2])
    is_relevant_by_context = False
    if starts_with_pronoun and state["is_keyword_active"]:
        is_relevant_by_context = True
    elif any(k in " ".join(subjects) for k in keyword_doc.text.split()):
        state["is_keyword_active"] = True
        is_relevant_by_context = True
    else:
        if subjects: state["is_keyword_active"] = False

    is_answer = 0
    if info_type not in [InformativeType.FILLER, InformativeType.QUESTION] and any(t.pos_ in {"VERB", "AUX"} for t in doc):
        if (relevance > 0.60 or is_relevant_by_context) and is_self_contained(doc):
            is_answer = 1

    ent_data = [ent.text for ent in doc.ents if ent.label_ in {"ORG", "PRODUCT", "LAW", "NORP", "FAC", "PERCENT", "MONEY", "GPE"}]
    unique_ents = list(set(ent_data))

    return SentenceOutput(
        SentenceId=s_id, Sentence=text, HtmlTag=h_tag, ParagraphId=p_id,
        FunctionalType="Interrogative" if info_type == InformativeType.QUESTION else "Declarative",
        InformativeType=info_type, Structure=struct, Voice=voice,
        InfoQuality=detect_info_quality_merged(doc, text),
        ClaritySynthesisType=detect_clarity_synthesis(doc, voice, struct, info_type),
        ClaimsCitation=bool(URL_PATTERN.search(text)), IsGrammaticallyCorrect=check_grammar_heuristics(doc, text),
        HasPronoun=not is_self_contained(doc), EntityCount=len(unique_ents), RelevanceScore=round(relevance, 4),
        answerSentenceFlag=is_answer,
        entityMentionFlag=EntityMentionFlag(value=1 if unique_ents else 0, entity_count=len(unique_ents), entities=unique_ents),
        entityConfidenceFlag=1 if (unique_ents and not any(h in text.lower() for h in ["might", "could", "maybe"])) else 0
    )

@app.post("/get-subtopics")
async def get_subtopics(request: CompetitorAnalysisRequest):
    all_comps = request.data
    if not all_comps: return []

    all_headings = []
    for i, comp in enumerate(all_comps):
        for h in comp.Headings:
            all_headings.append({"text": h, "comp_idx": i})

    if not all_headings: return []

    # 1. Embeddings generate karo
    texts = [h["text"] for h in all_headings]
    embeddings = model.encode(texts, convert_to_tensor=True)

    final_output = []
    already_grouped = set()

    # 2. Semantic Logic
    for i in range(len(all_headings)):
        if i in already_grouped: continue
        
        # Is group mein kaunse URLs (competitors) hain
        current_group_indices = [i]
        matched_comp_indices = {all_headings[i]["comp_idx"]}

        for j in range(i + 1, len(all_headings)):
            if j in already_grouped: continue
            
            # Cosine similarity calculate karo
            score = util.cos_sim(embeddings[i], embeddings[j]).item()
            
            # DEBUG: Console pe check karo kya match ho raha hai
            # print(f"Comparing: [{texts[i]}] vs [{texts[j]}] | Score: {score:.4f}")

            # 0.6 Similarity (Semantic match ke liye kaafi hai)
            if score > 0.6:
                matched_comp_indices.add(all_headings[j]["comp_idx"])
                current_group_indices.append(j)

        # 3. CONSENSUS: 3 ya usse zyada competitors
        if len(matched_comp_indices) >= 3:
            # Sabse lambi heading (zyaada informative) uthao
            best_h = max([texts[idx] for idx in current_group_indices], key=len)
            final_output.append(best_h)
            # Group waali saari headings ko mark karo taaki duplicate na ho
            for idx in current_group_indices:
                already_grouped.add(idx)

    return final_output
@app.post("/similarity", response_model=SimilarityResponse)
def similarity(req: SimilarityRequest):
    return SimilarityResponse(similarity=compute_similarity(req.text1, req.text2))

@app.post("/similarity/batch", response_model=SimilarityBatchResponse)
def similarity_batch(req: SimilarityBatchRequest):
    return SimilarityBatchResponse(similarities=[round(compute_similarity(i.text1, i.text2), 4) for i in req.items])

@app.post("/analyze", response_model=AnalysisResponse)
def analyze(request: AnalysisRequest):
    results, first_id = [], None
    kw_doc, state = nlp(request.primaryKeyword.lower()), {"is_keyword_active": True}
    for s in request.sentences:
        res = analyze_logic(s.Text, s.Id, kw_doc, state)
        if res.answerSentenceFlag == 1 and first_id is None: first_id = res.SentenceId
        results.append(res)
    return AnalysisResponse(sentences=results, answerPositionIndex=first_id)

@app.post("/process-article", response_model=AnalysisResponse)
def process_article(request: ArticleRequest):
    soup = BeautifulSoup(request.htmlContent, "html.parser")
    results, first_id, s_count, p_count = [], None, 1, 1
    kw_doc, state = nlp(request.primaryKeyword.lower()), {"is_keyword_active": True}
    processed_texts, pointer_regex = set(), re.compile(r'^([a-zA-Z0-9]{1,3}\.)+$')

    for block in soup.find_all(is_block_element):
        if any(is_block_element(p) for p in block.parents): continue
        raw_text = block.get_text(separator=" ", strip=True)
        if not raw_text or raw_text in processed_texts: continue
        
        doc = nlp(raw_text)
        temp_sents = [sent.text.strip() for sent in doc.sents if sent.text.strip()]
        merged_sents, skip = [], False
        for i in range(len(temp_sents)):
            if skip: (skip := False); continue
            if pointer_regex.match(temp_sents[i]) and (i+1) < len(temp_sents):
                merged_sents.append(f"{temp_sents[i]} {temp_sents[i+1]}"); skip = True
            elif not pointer_regex.match(temp_sents[i]): merged_sents.append(temp_sents[i])

        for text in merged_sents:
            res = analyze_logic(text, f"S{s_count}", kw_doc, state, block.name, f"P{p_count}")
            if res.answerSentenceFlag == 1 and first_id is None: first_id = res.SentenceId
            results.append(res); s_count += 1
        processed_texts.add(raw_text); p_count += 1
    return AnalysisResponse(sentences=results, answerPositionIndex=first_id)

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)