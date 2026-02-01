from fastapi import FastAPI
from pydantic import BaseModel
from typing import List
import re
import spacy
import language_tool_python

app = FastAPI(title="Centauri Sentence Tagger")

# Load models (FREE & OPEN SOURCE)
nlp = spacy.load("en_core_web_sm")
grammar_tool = language_tool_python.LanguageTool("en-US")

# ----------- INPUT / OUTPUT MODELS -----------

class SentenceIn(BaseModel):
    Id: str
    Text: str

class SentenceOut(BaseModel):
    SentenceId: str
    FunctionalType: str
    Structure: str
    Voice: str
    InformativeType: str
    InfoQuality: str
    ClaritySynthesisType: str
    ClaimsCitation: bool
    IsGrammaticallyCorrect: bool
    HasPronoun: bool

# ----------- UTILITIES -----------

URL_PATTERN = re.compile(r"https?://\S+|www\.\S+", re.IGNORECASE)

# ----------- 1) FUNCTIONAL TYPE (SEMANTIC) -----------

def detect_functional_type(doc, text: str) -> str:
    t = text.strip()

    if t.endswith("?"):
        return "Interrogative"

    if t.endswith("!"):
        return "Exclamatory"

    # Imperative detection via dependency structure
    for tok in doc:
        if tok.dep_ == "ROOT" and tok.pos_ == "VERB" and tok.tag_ == "VB":
            return "Imperative"

    return "Declarative"

# ----------- 2) STRUCTURE (CLAUSE-BASED) -----------

def detect_structure(doc) -> str:
    root_verbs = [tok for tok in doc if tok.dep_ == "ROOT"]
    has_subordinate = any(tok.dep_ in {"mark", "advcl", "relcl"} for tok in doc)

    if len(root_verbs) == 0:
        return "Fragment"

    if len(root_verbs) == 1 and not has_subordinate:
        return "Simple"

    if len(root_verbs) >= 2 and not has_subordinate:
        return "Compound"

    if len(root_verbs) == 1 and has_subordinate:
        return "Complex"

    return "CompoundComplex"

# ----------- 3) VOICE (PASSIVE DETECTION) -----------

def detect_voice(doc) -> str:
    for tok in doc:
        if tok.dep_ == "auxpass":
            return "Passive"
    return "Active"

# ----------- 4) INFORMATIVE TYPE (RULE-BASED + SEMANTIC) -----------

def detect_informative_type(doc, text: str) -> str:
    text_lower = text.lower()

    if any(tok.like_num for tok in doc):
        return "Statistic"

    if re.search(r"\b(is|are|refers to|means|stands for)\b", text_lower):
        return "Definition"

    if text.strip().endswith("?"):
        return "Question"

    if re.search(r"\b(will|is likely to|expected to|is going to)\b", text_lower):
        return "Prediction"

    if re.search(r"\b(you should|consider|try to|recommend)\b", text_lower):
        return "Suggestion"

    if re.search(r"\b(however|therefore|moving on|next|furthermore)\b", text_lower):
        return "Transition"

    if len(doc) <= 4:
        return "Filler"

    if re.search(r"\b(i think|we believe|in my view)\b", text_lower):
        return "Opinion"

    if re.search(r"\b(we noticed|we observed|users tend to)\b", text_lower):
        return "Observation"

    return "Fact"

# ----------- 5) INFO QUALITY (SEMANTIC HEURISTIC) -----------

def detect_info_quality(doc, text: str) -> str:
    text_lower = text.lower()

    if re.search(r"\b(according to|as per|per the|reports that)\b", text_lower):
        return "Derived"

    if any(ent.label_ in {"ORG", "GPE", "LAW", "PRODUCT"} for ent in doc.ents):
        return "WellKnown"

    if "our " in text_lower or "we " in text_lower:
        return "Unique"

    return "PartiallyKnown"

# ----------- 6) CLARITY SYNTHESIS TYPE -----------

def detect_clarity(doc) -> str:
    if len(doc) < 8 and not any(tok.dep_ in {"advcl", "relcl"} for tok in doc):
        return "Focused"

    if len(doc) < 18:
        return "ModerateComplexity"

    if any(tok.dep_ in {"discourse", "parataxis"} for tok in doc) or len(doc) > 30:
        return "LowClarity"

    return "UnIndexable"

# ----------- 7) CLAIMS CITATION (NLP + RULES) -----------

def detect_claims_citation(doc, text: str) -> bool:
    text_lower = text.lower()

    # 1) First-person source
    if re.search(r"\b(we observed|we noticed|we found|in our study|our analysis shows)\b", text_lower):
        return True

    # 2) Third-person source
    if re.search(r"\b(according to|as per|based on|per the|reports that)\b", text_lower):
        return True

    # 3) Visible hyperlink
    if URL_PATTERN.search(text):
        return True

    # 4) NLP-based fallback logic (your "own citation check logic")
    # If sentence contains a numeric/statistical claim BUT has no source → treat as NOT cited
    if any(tok.like_num for tok in doc):
        return False

    # If sentence contains a named entity + reporting verb → likely cited
    reporting_verbs = {"says", "states", "reports", "claims", "found", "showed"}
    has_reporting = any(tok.lemma_ in reporting_verbs for tok in doc)
    has_entity = any(ent.label_ in {"ORG", "PERSON", "GPE"} for ent in doc.ents)

    if has_reporting and has_entity:
        return True

    return False

# ----------- 8) GRAMMAR CHECK (REAL NLP, NOT LLM) -----------

def detect_grammar(text: str) -> bool:
    matches = grammar_tool.check(text)
    return len(matches) == 0

# ----------- 9) HAS PRONOUN (SYNTACTIC DETECTION) -----------

PRONOUN_TAGS = {"PRP", "PRP$", "WP", "WDT"}

def detect_pronoun(doc) -> bool:
    for tok in doc:
        if tok.tag_ in PRONOUN_TAGS:
            return True
    return False

# ----------- API ENDPOINT -----------

@app.post("/tag", response_model=List[SentenceOut])
def tag_sentences(sentences: List[SentenceIn]):
    results = []

    for s in sentences:
        doc = nlp(s.Text)

        results.append(
            SentenceOut(
                SentenceId=s.Id,
                FunctionalType=detect_functional_type(doc, s.Text),
                Structure=detect_structure(doc),
                Voice=detect_voice(doc),
                InformativeType=detect_informative_type(doc, s.Text),
                InfoQuality=detect_info_quality(doc, s.Text),
                ClaritySynthesisType=detect_clarity(doc),
                ClaimsCitation=detect_claims_citation(doc, s.Text),
                IsGrammaticallyCorrect=detect_grammar(s.Text),
                HasPronoun=detect_pronoun(doc),
            )
        )

    return results
