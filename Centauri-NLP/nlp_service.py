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
    Source: str

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

def detect_structure_advanced(sent) -> str:
    """Ek single sentence ki structure nikalne ke liye logic"""
    # Verbs check
    verbs = [t for t in sent if t.pos_ in ("VERB", "AUX")]
    if not verbs: 
        return "Fragment"

    # Independent Clauses (IC): ROOT aur uske parallel main verbs
    ic_count = sum(1 for t in sent if t.dep_ == "ROOT" or (t.dep_ == "conj" and t.pos_ in ("VERB", "AUX")))
    
    # Dependent Clauses (DC): Extra layers like 'because', 'when', 'which'
    dc_count = sum(1 for t in sent if t.dep_ in ("advcl", "relcl", "acl", "ccomp") or t.dep_ == "mark")

    # Final Classification Logic
    if ic_count >= 2 and dc_count >= 1: 
        return "CompoundComplex"
    if ic_count >= 1 and dc_count >= 1: 
        return "Complex"
    if ic_count >= 2: 
        return "Compound"
    return "Simple" if ic_count == 1 else "Fragment"
    
def detect_clarity_synthesis(doc, voice: str, structure: str, info_type: InformativeType) -> str:
    text_lower = doc.text.lower()
    
    # 1. UNINDEXABLE: Filler content ya aise phrases jo web context ke liye kachra hain
    if info_type == InformativeType.FILLER or structure == "Fragment": 
        return "UnIndexable"
    if any(p in text_lower for p in ["as we can see", "look at this", "click here", "read more"]): 
        return "UnIndexable"

    # 2. LOGIC PREPARATION
    # Depth of dependency tree (kitne complex branches hain sentence mein)
    depths = [len(list(t.ancestors)) for t in doc]
    avg_depth = sum(depths) / len(doc) if len(doc) > 0 else 0
    
    # Unique entities vs total tokens (High entity density means technical jargon)
    entity_ratio = len(doc.ents) / len(doc) if len(doc) > 0 else 0
    
    # Verbs and Punctuation check
    verb_count = len([t for t in doc if t.pos_ == "VERB"])
    is_punctuated = any(t.pos_ == "PUNCT" for t in doc)

    # 3. LOW CLARITY: Agar sentence bahut zyada "deep" hai ya verbs ki jagah sirf adjectives bhare hain
    # (High depth + passive voice + no proper verbs)
    if avg_depth > 4 or (voice == "Passive" and structure == "CompoundComplex"):
        return "LowClarity"
    
    # Adjective/Adverb loading (Abhi bhi check karenge par length ke context mein nahi)
    modifiers = [t for t in doc if t.pos_ in ("ADJ", "ADV")]
    if len(modifiers) / len(doc) > 0.3: # Agar 30% se zyada words sirf tareef ya quality wale hain
        return "LowClarity"

    # 4. FOCUSED: Active voice, sahi verb-to-token ratio, aur direct structure
    # (Length check ko flexible rakha hai - Focused content 20 words tak bhi ho sakta hai)
    if (voice == "Active" 
        and structure in ("Simple", "Compound") 
        and verb_count >= 1 
        and entity_ratio < 0.2): # Bahut zyada technical names nahi hain toh "Focused" hai
        return "Focused"

    # 5. DEFAULT: Agar sentence structured hai par thoda bhari hai
    return "ModerateComplexity"

def classify_informative_type_merged(doc) -> InformativeType:
    text_lower = doc.text.lower().strip()
    
    # 1. QUESTION (Syntactic Check)
    # Check for '?' or inverted auxiliary-subject order (e.g., "Are you...")
    if text_lower.endswith("?") or (doc[0].pos_ == "AUX" and len(doc) > 1 and doc[1].pos_ in {"PRON", "NOUN"}):
        return InformativeType.QUESTION

    # 2. SUGGESTION (The Expertise Powerhouse)
    # Catch Imperatives (sentences starting with a base verb like "Learn", "Choose", "Build")
    # And Modals (should, must, need to)
    is_imperative = doc[0].pos_ == "VERB" and doc[0].dep_ == "ROOT"
    has_modal = any(t.lemma_ in {"should", "must", "ought", "need", "require"} for t in doc)
    
    if is_imperative or has_modal:
        return InformativeType.SUGGESTION

    # 3. DEFINITION (The Authority Check)
    # Check for "X is a Y" where X is a Subject and Y is a Complement
    has_copula = any(t.lemma_ == "be" and t.dep_ == "ROOT" for t in doc)
    if has_copula:
        # If the root is 'be' and it connects a subject to a noun/adj, it's a definition or observation
        return InformativeType.DEFINITION

    # 4. PREDICTION (Future Outlook)
    # Look for 'will' or 'going to' which your C# code counts as Expertise
    if any(t.lemma_ == "will" and t.pos_ == "AUX" for doc_t in doc for t in doc):
         return InformativeType.PREDICTION

    # 5. UNCERTAIN
    if any(t.lemma_ in {"might", "could", "may", "possibly", "suggest"} for t in doc):
        return InformativeType.UNCERTAIN

    # 6. STATISTIC & FACT (Entity-Based)
    if doc.ents:
        if any(ent.label_ in {"PERCENT", "MONEY", "QUANTITY", "CARDINAL"} for ent in doc.ents):
            return InformativeType.STATISTIC
        if any(ent.label_ in {"DATE", "GPE", "LAW", "ORG", "PERSON"} for ent in doc.ents):
            return InformativeType.FACT

    # 7. FILLER / NOISE
    # Short fragments without verbs are usually headers or noise
    if len(doc) < 4 and not any(t.pos_ == "VERB" for t in doc):
        return InformativeType.FILLER

    # 8. CLAIM (Default)
    # If it's a full sentence but doesn't meet the above, it's a general claim.
    return InformativeType.CLAIM
def detect_info_quality_merged(doc, text: str) -> str:
    text_lower = text.lower()
    
    # 1. FALSE: Extreme claims or suspicious patterns
    # Logical contradictions, exaggerated superlatives, or obvious spam patterns
    if re.search(r"\b(guaranteed|instantly|always|never|100% true|no doubt)\b", text_lower):
        # Yahan context check hota hai, agar claim bina evidence ke extreme hai toh False/High-Risk
        return "False"

    # 2. DERIVED: Attribution logic (According to, Cited by, etc.)
    # Isme humne patterns badha diye hain for better accuracy
    derived_patterns = r"\b(according to|as per|reports that|studies show|cited by|based on|referencing)\b"
    if re.search(derived_patterns, text_lower):
        return "Derived"

    # 3. UNIQUE: Personal experience and First-hand insights
    # "I found", "In my experience", "Our testing revealed"
    unique_patterns = r"\b(in my experience|i found|our testing|we discovered|unique insight|specifically observed)\b"
    if re.search(unique_patterns, text_lower) or any(token.text.lower() in {"i", "my", "we", "our"} for token in doc):
        # Personal pronouns + observation verbs usually indicate unique/first-hand info
        return "Unique"

    # 4. WELLKNOWN: Public facts, Entities, and Legal mentions
    # Entities like Organizations (IRS), Laws, and Dates
    if any(ent.label_ in {"ORG", "GPE", "LAW", "DATE", "EVENT"} for ent in doc.ents):
        return "WellKnown"

    # 5. PARTIALLYKNOWN: Default fallback
    # Jab info vague ho ya half-context mein ho
    return "PartiallyKnown"
import re

# Regex to detect numbering prefixes like i), ii), 1., a), etc.
NUMBERING_REGEX = re.compile(r'^\s*(?:[ivxlcdm]+|[0-9]+|[a-z]+)(?:[\.\)])\s+', re.IGNORECASE)
# Pehle ye install kar lena: pip install pyspellchecker
import re

def check_grammar_heuristics(doc, text: str) -> bool:
    if not text or len(text.strip()) < 2: 
        return False

    raw_text = text.strip()
    
    for sent in doc.sents:
        for i, t in enumerate(sent):
            # 1. BLUNT TENSE ERROR (is treat, is file)
            # 'be' verb ke turant baad agar Base Form (VB) hai toh 100% galti hai
            if t.lemma_ == "be" and i + 1 < len(sent):
                nxt = sent[i+1]
                if nxt.tag_ == "VB": 
                    return False

            # 2. BLUNT SUBJECT-VERB MISMATCH (is Requirements)
            # Singular 'is' aur Plural attribute ka combo
            if t.lemma_ == "be" and "Number=Sing" in t.morph:
                for child in t.children:
                    if child.dep_ in {"attr", "nsubj"} and "Number=Plur" in child.morph:
                        return False

            # 3. BLUNT SPELLING/OOV (telled, busines)
            # Technical Acronyms (EIN, LLC) ko upper-case logic se bacha liya hai
            if t.is_alpha and not t.is_stop and not t.ent_type_:
                if hasattr(t, 'is_oov') and t.is_oov:
                    # Acronyms like EINs, LLCs are ignored
                    is_acronym = t.text.isupper() or (t.text[:-1].isupper() and t.text.endswith('s'))
                    if not is_acronym:
                        return False

    # 4. CAPITALIZATION (Starting with lowercase)
    # i), 1) jaise numbering ko clean karke check karta hai
    content = re.sub(r'^(\d+[\.\)]|[a-zA-Z][\.\)]|[ivxIVX]+[\.\)]|\(\w\))\s*', '', raw_text).strip()
    if content and content[0].islower() and not content[0].isdigit():
        return False

    return True

    
def identify_source_type_semantic(doc, text: str) -> str:
    text_lower = text.lower().strip()
    
    # --- 0. PRE-REQUISITES ---
    subjects = [t.text.lower() for t in doc if "subj" in t.dep_]
    # Check for specific entities (IRS, ACH, GPE, etc.)
    has_external_entity = any(ent.label_ in {"ORG", "GPE", "LAW", "MONEY", "CARDINAL"} for ent in doc.ents)
    has_brand = "inkle" in text_lower # Specific brand recognition
    
    # Action/Verb analysis
    root_verb = [t.lemma_ for t in doc if t.dep_ == "ROOT"]
    root_verb = root_verb[0] if root_verb else ""

    # --- 1. FIRST PARTY (Publisher's Expertise/Action) ---
    # Logic: If brand name is present OR First-person used with internal actions
    first_person_markers = {"we", "our", "us", "my", "i"}
    discovery_keywords = {"analyze", "find", "audit", "proprietary", "data", "observe", "research", "platform", "help"}
    
    if has_brand or any(s in first_person_markers for s in subjects):
        # Agar brand khud ki baat kar raha hai ya "Humein mila" bol raha hai
        if any(k in text_lower for k in discovery_keywords) or root_verb in {"help", "provide", "analyze"}:
            return "FirstParty"
        # Contextual First Party (e.g., "Our team recommends")
        if "our" in text_lower:
            return "FirstParty"

    # --- 2. THIRD PARTY (Regulatory/External Authority) ---
    # Logic: Agar IRS, Tax, Rules, ya Laws ki baat hai aur hum (First Party) claim nahi kar rahe
    regulatory_keywords = {"irs", "tax", "form", "rule", "requirement", "penalty", "law", "government", "deadline", "filing"}
    
    # A. Explicit Attribution (According to, reports)
    referencing_markers = {"according", "report", "state", "cite", "publish", "mention", "require"}
    if any(m in text_lower for m in referencing_markers):
        if not any(s in first_person_markers for s in subjects):
            return "ThirdParty"

    # B. Implicit Attribution (Tax facts are always Third Party)
    # This fixes S6, S14, S22, S29
    if any(rk in text_lower for rk in regulatory_keywords) or has_external_entity:
        # Agar sentence Fact ya Statistic hai aur first person missing hai
        if not any(s in first_person_markers for s in subjects):
            return "ThirdParty"

    # --- 3. SECOND PARTY (Direct Engagement) ---
    # Logic: Direct quotes, interviews, or "told us"
    interaction_verbs = {"interview", "told", "confirm", "respond", "speak"}
    if root_verb in interaction_verbs and any(p in text_lower for p in ["us", "me", "our"]):
        return "SecondParty"
    if "exclusive" in text_lower and "interview" in text_lower:
        return "SecondParty"

    # --- 4. FALLBACK FOR TECHNICAL CONTEXT ---
    # Agar 1099 jaisa technical term hai, toh wo third-party documentation se hi hai
    if re.search(r"\b(1099|nec|misc|k-1|w2)\b", text_lower):
        return "ThirdParty"

    return "Unknown"
    
    
def analyze_logic(text: str, s_id: str, keyword_doc, state: Dict, h_tag: str = None, p_id: str = None) -> SentenceOutput:
    doc = nlp(text)
    info_type = classify_informative_type_merged(doc)
    # Target types for source attribution
    source_trigger_types = {
        InformativeType.FACT, 
        InformativeType.CLAIM, 
        InformativeType.DEFINITION, # Definitions often have sources
        InformativeType.STATISTIC
    }
    
    source_value = "Unknown"
    if info_type in source_trigger_types:
        # Using Semantic brain instead of just hardcoded strings
        source_value = identify_source_type_semantic(doc, text)
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
        entityConfidenceFlag=1 if (unique_ents and not any(h in text.lower() for h in ["might", "could", "maybe"])) else 0,
        Source=source_value
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
    
    # Primary keyword setting
    kw_doc = nlp(request.primaryKeyword.lower())
    state = {"is_keyword_active": True}
    processed_texts = set()

    for block in soup.find_all(is_block_element):
        if any(is_block_element(p) for p in block.parents): 
            continue
            
        # 1. Pura Block Text extract karna
        raw_text = block.get_text(separator=" ", strip=True)
        if not raw_text or raw_text in processed_texts: 
            continue

        # 2. Table Rows (TR) special handling
        if block.name == 'tr':
            raw_text = " - ".join([td.get_text(strip=True) for td in block.find_all('td') if td.get_text(strip=True)])

        # --- CHANGE START: Logical Sentence Splitting ---
        # SpaCy ka use karke text ko semantic sentences mein split karenge
        doc = nlp(raw_text)
        sentences = [sent.text.strip() for sent in doc.sents if sent.text.strip()]

        for sentence_text in sentences:
            # Har sentence ko ab alag analyze karenge
            res = analyze_logic(
                text=sentence_text, 
                s_id=f"S{s_count}", 
                keyword_doc=kw_doc, 
                state=state, 
                h_tag=block.name, 
                p_id=f"P{p_count}" # Same block ke liye p_id same rahegi
            )

            # First Answer Detection
            if res.answerSentenceFlag == 1 and first_id is None: 
                first_id = res.SentenceId
                
            results.append(res)
            s_count += 1 
        
        # --- CHANGE END ---

        processed_texts.add(raw_text)
        p_count += 1 # Sirf block khatam hone par paragraph count badhao

    return AnalysisResponse(sentences=results, answerPositionIndex=first_id)

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)