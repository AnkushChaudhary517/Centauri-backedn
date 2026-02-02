import spacy
import re
from fastapi import FastAPI
from pydantic import BaseModel
from typing import List
from enum import Enum

# 1. Initialize the Large model
try:
    nlp = spacy.load("en_core_web_lg")
except:
    print("FATAL: Please run 'python -m spacy download en_core_web_lg' in terminal.")

app = FastAPI(title="Centauri Pro NLP Service")

# Missing URL Pattern initialization
URL_PATTERN = re.compile(r'http[s]?://(?:[a-zA-Z]|[0-9]|[$-_@.&+]|[!*\(\),]|(?:%[0-9a-fA-F][0-9a-fA-F]))+')

# --- 2. ENUMS & MODELS ---

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
    primaryKeyword: str  # C# se keyword yahan aayega

class SentenceOutput(BaseModel):
    SentenceId: str
    Sentence: str
    FunctionalType: str
    InformativeType: InformativeType  # Fixed: Added missing fields
    Structure: str
    Voice: str
    InfoQuality: str                  # Fixed: Added missing fields
    ClaritySynthesisType: str         # Fixed: Added missing fields
    ClaimsCitation: bool              # Fixed: Added missing fields
    IsGrammaticallyCorrect: bool
    HasPronoun: bool
    EntityCount: int
    RelevanceScore:float

# --- 3. LOGIC FUNCTIONS ---

def check_grammar_heuristics(doc, text: str) -> bool:
    if not text or len(text) < 2: return False
    has_root_verb = any(t.dep_ == "ROOT" and (t.pos_ in ["VERB", "AUX"]) for t in doc)
    if not has_root_verb: return False
    
    # Subject-Verb Agreement
    for t in doc:
        if t.dep_ == "nsubj" and t.head.pos_ in ["VERB", "AUX"]:
            if t.tag_ == "NN" and t.head.tag_ == "VBP" and t.head.lemma_ != "be": return False
            if t.tag_ == "NNS" and t.head.tag_ == "VBZ": return False

    if not text[0].isupper(): return False
    if text[-1] not in ".?!\"": return False
    return True

def detect_functional_type(doc, text: str) -> str:
    t = text.strip()
    if t.endswith("?"): return "Interrogative"
    if t.endswith("!"): return "Exclamatory"
    for tok in doc:
        if tok.dep_ == "ROOT" and tok.pos_ == "VERB" and tok.tag_ == "VB":
            return "Imperative"
    return "Declarative"

def detect_structure(doc) -> str:
    root_verbs = [tok for tok in doc if tok.dep_ == "ROOT"]
    has_subordinate = any(tok.dep_ in {"mark", "advcl", "relcl"} for tok in doc)
    if not root_verbs: return "Fragment"
    if len(root_verbs) == 1 and not has_subordinate: return "Simple"
    if len(root_verbs) >= 2 and not has_subordinate: return "Compound"
    if len(root_verbs) == 1 and has_subordinate: return "Complex"
    return "CompoundComplex"

def detect_voice(doc) -> str:
    for tok in doc:
        if tok.dep_ == "auxpass": return "Passive"
    return "Active"

def classify_informative_type(doc) -> InformativeType:
    text_lower = doc.text.lower().strip()
    if text_lower.endswith("?") or any(t.tag_ in ("WDT", "WP", "WRB") for t in doc):
        return InformativeType.QUESTION
    if len(doc) < 3 or text_lower.startswith(("url:", "meta:", "/*")):
        return InformativeType.FILLER
    
    transitions = {"however", "furthermore", "additionally", "consequently"}
    if any(t.text.lower() in transitions for t in doc if t.i < 2):
        return InformativeType.TRANSITION

    if any(t.text.lower() in {"might", "could", "may", "perhaps"} for t in doc):
        return InformativeType.UNCERTAIN

    if any(ent.label_ in {"PERCENT", "MONEY", "QUANTITY", "CARDINAL"} for ent in doc.ents):
        return InformativeType.STATISTIC

    if any(t.lemma_ in {"will", "expect", "predict"} for t in doc):
        return InformativeType.PREDICTION

    if any(t.text.lower() in {"should", "must", "recommend"} for t in doc) or doc[0].pos_ == "VERB":
        return InformativeType.SUGGESTION

    if any(p in text_lower for p in ["is defined as", "refers to", "means"]):
        return InformativeType.DEFINITION

    if any(t.lemma_ in {"think", "believe", "opinion"} for t in doc):
        return InformativeType.OPINION

    if any(t.lemma_ in {"notice", "observe", "appear"} for t in doc):
        return InformativeType.OBSERVATION

    if any(ent.label_ in {"DATE", "GPE", "LAW"} for ent in doc.ents):
        return InformativeType.FACT

    return InformativeType.CLAIM

def detect_info_quality(doc, text: str) -> str:
    text_lower = text.lower()
    if re.search(r"\b(according to|as per|reports that)\b", text_lower):
        return "Derived"
    if any(ent.label_ in {"ORG", "GPE", "LAW"} for ent in doc.ents):
        return "WellKnown"
    if "our " in text_lower or "we " in text_lower:
        return "Unique"
    return "PartiallyKnown"

def detect_clarity(doc) -> str:
    if len(doc) < 8: return "Focused"
    if len(doc) < 18: return "ModerateComplexity"
    if len(doc) > 30: return "LowClarity"
    return "UnIndexable"

def detect_claims_citation(doc, text: str) -> bool:
    text_lower = text.lower()
    if re.search(r"\b(we observed|according to|based on|as per)\b", text_lower):
        return True
    if URL_PATTERN.search(text):
        return True
    
    reporting_verbs = {"says", "states", "reports", "found"}
    if any(tok.lemma_ in reporting_verbs for tok in doc) and any(ent.label_ in {"ORG", "PERSON"} for ent in doc.ents):
        return True
    return False

def detect_pronoun(doc) -> bool:
    PRONOUN_TAGS = {"PRP", "PRP$", "WP", "WDT"}
    return any(tok.tag_ in PRONOUN_TAGS for tok in doc)

# --- 4. API ENDPOINT ---

@app.post("/analyze", response_model=List[SentenceOutput])
def analyze_sentences(request: AnalysisRequest):
    results = []
    keyword_doc = nlp(request.primaryKeyword.lower())
    for s in request.sentences:
        text = s.Text.strip()
        if not text: continue
        
        doc = nlp(text)
        # --- Semantic Relevance Score ---
        # Similarity score between keyword and sentence
        relevance = 0.0
        if doc.vector_norm and keyword_doc.vector_norm:
            relevance = doc.similarity(keyword_doc)
            
        results.append(SentenceOutput(
            SentenceId=s.Id,
            Sentence=text,
            InformativeType=classify_informative_type(doc),
            FunctionalType=detect_functional_type(doc, text),
            Structure=detect_structure(doc),
            Voice=detect_voice(doc),
            InfoQuality=detect_info_quality(doc, text),
            ClaritySynthesisType=detect_clarity(doc),
            ClaimsCitation=detect_claims_citation(doc, text),
            IsGrammaticallyCorrect=check_grammar_heuristics(doc, text),
            HasPronoun=detect_pronoun(doc),
            EntityCount=len(doc.ents),
            RelevanceScore=round(relevance, 4)
        ))
    return results
if __name__ == "__main__":
    import uvicorn
    uvicorn.run("nlp_service:app", host="0.0.0.0", port=8000, log_level="info")
