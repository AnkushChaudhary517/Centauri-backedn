import requests
import spacy
import json
from collections import Counter
import re
import numpy as np
from bs4 import BeautifulSoup
from fastapi import FastAPI, Body
from pydantic import BaseModel, ConfigDict, Field
from typing import List, Optional, Dict, Any, Union
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

class ScoreCard(BaseModel):
    model_config = ConfigDict(extra='ignore')
    
    IntentScore: Union[int, float] = 5
    SectionScore: Union[int, float] = 5
    KeywordScore: Union[int, float] = 5
    OriginalInfoScore: Union[int, float] = 5
    ExpertiseScore: Union[int, float] = 5
    CredibilityScore: Union[int, float] = 5
    AuthorityScore: Union[int, float] = 5
    SimplicityScore: Union[int, float] = 5
    GrammarScore: Union[int, float] = 5
    VariationScore: Union[int, float] = 5
    PlagiarismScore: Union[int, float] = 5
    ClaritySynthesisScore: Union[int, float] = 5
    FactRetrievalScore: Union[int, float] = 5
    AnswerBlockDensityScore: Union[int, float] = 5
    FactualIsolationScore: Union[int, float] = 5
    EntityAlignmentScore: Union[int, float] = 5
    TechnicalClarityScore: Union[int, float] = 5
    SignalToNoiseScore: Union[int, float] = 5

class ContentSection(BaseModel):
    model_config = ConfigDict(extra='ignore')
    
    type: str  # "article", "section", "sentence"
    text: str
    level: Optional[str] = None  # "H2", "H3", "paragraph", etc.
    index: Optional[int] = None

class PreviousRecommendation(BaseModel):
    whatToChange: str
    description: str

class RecommendationExample(BaseModel):
    model_config = ConfigDict(extra='ignore', populate_by_name=True)
    bad: str = ""
    good: str = ""

class RecommendationItem(BaseModel):
    model_config = ConfigDict(extra='ignore', populate_by_name=True)
    whatToChange: str
    priority: str = "Medium"
    description: str = ""
    fix: str = ""
    improves: List[str] = []
    examples: Optional[RecommendationExample] = None

class ArticleLevelRecommendation(RecommendationItem):
    model_config = ConfigDict(extra='ignore', populate_by_name=True)

class SectionLevelRecommendation(RecommendationItem):
    model_config = ConfigDict(extra='ignore', populate_by_name=True)
    text: str = ""

class SentenceLevelRecommendation(RecommendationItem):
    model_config = ConfigDict(extra='ignore', populate_by_name=True)
    text: str = ""
    sentenceIndex: Optional[int] = None

class RecommendationsResponse(BaseModel):
    model_config = ConfigDict(extra='ignore', populate_by_name=True)
    overall: List[ArticleLevelRecommendation] = Field(default_factory=list)
    sectionLevel: List[SectionLevelRecommendation] = Field(default_factory=list)
    sentenceLevel: List[SentenceLevelRecommendation] = Field(default_factory=list)

class RecommendationRequest(BaseModel):
    model_config = ConfigDict(extra='ignore')
    sections: List[ContentSection]
    scoreCard: ScoreCard
    primaryKeyword: str
    secondaryKeywords: List[str] = Field(default_factory=list)
    entities: List[str] = Field(default_factory=list)
    searchIntent: str  # "informational", "transactional", "navigational"
    previousRecommendations: Optional[RecommendationsResponse] = None


# Alternative input model to match the actual API input format
class SectionInputData(BaseModel):
    model_config = ConfigDict(extra='ignore', populate_by_name=True)
    SectionText: str
    Sentences: List[str]

class SentenceInputData(BaseModel):
    model_config = ConfigDict(extra='ignore', populate_by_name=True)
    Text: str
    HtmlTag: str

class RecommendationRequestInput(BaseModel):
    model_config = ConfigDict(extra='ignore', populate_by_name=True)
    PrimaryKeyword: str
    sections: List[SectionInputData]
    Scores: Dict[str, Union[int, float]]
    SearchIntent: str
    Sentences: Optional[List[SentenceInputData]] = None
    secondaryKeywords: Optional[List[str]] = Field(default_factory=list)
    entities: Optional[List[str]] = Field(default_factory=list)
    previousRecommendations: Optional[RecommendationsResponse] = None


def build_scorecard(score_dict: Optional[Dict[str, Union[int, float]]]) -> ScoreCard:
    score_dict = score_dict or {}
    return ScoreCard(
        IntentScore=score_dict.get("IntentScore", 5),
        SectionScore=score_dict.get("SectionScore", 5),
        KeywordScore=score_dict.get("KeywordScore", 5),
        OriginalInfoScore=score_dict.get("OriginalInfoScore", 5),
        ExpertiseScore=score_dict.get("ExpertiseScore", 5),
        CredibilityScore=score_dict.get("CredibilityScore", 5),
        AuthorityScore=score_dict.get("AuthorityScore", 5),
        SimplicityScore=score_dict.get("SimplicityScore", 5),
        GrammarScore=score_dict.get("GrammarScore", 5),
        VariationScore=score_dict.get("VariationScore", 5),
        PlagiarismScore=score_dict.get("PlagiarismScore", 5),
        ClaritySynthesisScore=score_dict.get("ClaritySynthesisScore", 5),
        FactRetrievalScore=score_dict.get("FactRetrievalScore", 5),
        AnswerBlockDensityScore=score_dict.get("AnswerBlockDensityScore", 5),
        FactualIsolationScore=score_dict.get("FactualIsolationScore", 5),
        EntityAlignmentScore=score_dict.get("EntityAlignmentScore", 5),
        TechnicalClarityScore=score_dict.get("TechnicalClarityScore", 5),
        SignalToNoiseScore=score_dict.get("SignalToNoiseScore", 5)
    )


def normalize_recommendation_request(
    request: Union[RecommendationRequest, RecommendationRequestInput]
) -> RecommendationRequest:
    if isinstance(request, RecommendationRequest):
        return request

    sections_list = []
    for idx, section in enumerate(request.sections):
        if section.SectionText.strip():
            sections_list.append(ContentSection(
                type="section_header",
                text=section.SectionText,
                level="H2",
                index=idx
            ))

        for sent in section.Sentences:
            if sent and sent.strip():
                sections_list.append(ContentSection(
                    type="sentence",
                    text=sent,
                    level="paragraph",
                    index=idx
                ))

    if request.Sentences:
        for idx, sentence in enumerate(request.Sentences, start=len(sections_list)):
            if sentence.Text.strip():
                sections_list.append(ContentSection(
                    type="sentence",
                    text=sentence.Text,
                    level=sentence.HtmlTag,
                    index=idx
                ))

    return RecommendationRequest(
        sections=sections_list,
        scoreCard=build_scorecard(request.Scores),
        primaryKeyword=request.PrimaryKeyword,
        secondaryKeywords=request.secondaryKeywords or [],
        entities=request.entities or [],
        searchIntent=request.SearchIntent,
        previousRecommendations=request.previousRecommendations
    )

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


class RecommendationGenerator:
    """Generate SEO and AI indexing recommendations based on scores, content, and keywords"""
    
    def __init__(self, request: RecommendationRequest):
        self.scores = request.scoreCard
        self.primary_kw = request.primaryKeyword.lower()
        self.secondary_kws = [kw.lower() for kw in request.secondaryKeywords]
        self.entities = request.entities
        self.search_intent = request.searchIntent
        self.sections = request.sections
        
        # Extract all previous recommendations from all levels
        self.previous_recs = set()
        self.previous_targets = set()
        if request.previousRecommendations:
            prev = request.previousRecommendations
            # Collect from overall level
            self.previous_recs.update(rec.whatToChange.lower() for rec in prev.overall)
            # Collect from section level
            self.previous_recs.update(rec.whatToChange.lower() for rec in prev.sectionLevel)
            # Collect from sentence level
            self.previous_recs.update(rec.whatToChange.lower() for rec in prev.sentenceLevel)
            for rec in list(prev.overall) + list(prev.sectionLevel) + list(prev.sentenceLevel):
                target_text = ""
                if getattr(rec, "text", None):
                    target_text = rec.text
                elif getattr(rec, "examples", None) and rec.examples and rec.examples.bad:
                    target_text = rec.examples.bad
                if target_text:
                    self.previous_targets.add(self.normalize_for_match(target_text))
        
        # Flag to indicate if we should generate predictions (when Gemini response is null)
        self.use_predictions = not request.previousRecommendations or (
            len(request.previousRecommendations.overall) == 0 and
            len(request.previousRecommendations.sectionLevel) == 0 and
            len(request.previousRecommendations.sentenceLevel) == 0
        )
        
    def is_duplicate(self, what_to_change: str) -> bool:
        """Check if recommendation already exists in previous results"""
        if self.use_predictions:
            return False  # Generate all predictions if Gemini was null
        return what_to_change.lower() in self.previous_recs
    
    def score_to_dict(self):
        """Convert ScoreCard to dictionary for easier access"""
        return self.scores.dict()

    def make_section_example(self, section_text: str, good: str) -> RecommendationExample:
        return RecommendationExample(bad=section_text, good=good)

    def make_sentence_example(self, sentence_text: str, good: str) -> RecommendationExample:
        return RecommendationExample(bad=sentence_text, good=good)

    def normalize_text(self, text: str) -> str:
        return re.sub(r"\s+", " ", (text or "")).strip()

    def normalize_for_match(self, text: str) -> str:
        return re.sub(r"[^a-z0-9 ]", "", self.normalize_text(text).lower()).strip()

    def has_previous_target_match(self, text: str) -> bool:
        normalized = self.normalize_for_match(text)
        if not normalized:
            return False
        if normalized in self.previous_targets:
            return True
        return any(
            normalized in previous or previous in normalized
            for previous in self.previous_targets
            if previous
        )

    def should_skip_recommendation(self, what_to_change: str, text: str = "") -> bool:
        if self.is_duplicate(what_to_change):
            return True
        if text and self.has_previous_target_match(text):
            return True
        return False

    def extract_relevant_snippet(self, text: str, max_words: int = 22) -> str:
        clean = self.normalize_text(text)
        if not clean:
            return clean

        sentences = re.split(r'(?<=[.!?])\s+', clean)
        candidate = sentences[0].strip() if sentences else clean
        words = candidate.split()
        if len(words) <= max_words:
            return candidate
        return " ".join(words[:max_words]).rstrip(",;:") + "..."

    def get_section_candidates(self) -> List[ContentSection]:
        return [
            section for section in self.sections
            if section.level in {"H1", "H2", "H3", "paragraph"} or section.type in {"article", "section", "sentence", "section_header"}
        ]

    def get_heading_sections(self) -> List[ContentSection]:
        return [section for section in self.sections if section.level in {"H1", "H2", "H3"}]

    def get_all_sentences(self) -> List[tuple[int, str]]:
        sentences = []
        sent_index = 0
        for section in self.sections:
            if section.type in {"section", "article", "sentence"} or section.level in {"paragraph", "H1", "H2", "H3"}:
                doc = nlp(section.text)
                for sent in doc.sents:
                    sent_text = self.normalize_text(sent.text)
                    if sent_text:
                        sentences.append((sent_index, sent_text))
                        sent_index += 1
        return sentences

    def article_text(self) -> str:
        return self.normalize_text(" ".join(section.text for section in self.get_section_candidates()))

    def opening_text(self, word_limit: int = 100) -> str:
        words = self.article_text().split()
        return " ".join(words[:word_limit])

    def find_keyword_gap_sentence(self) -> Optional[str]:
        keyword_tokens = set(self.primary_kw.split())
        for _, sentence in self.get_all_sentences():
            sent_lower = sentence.lower()
            overlap = sum(1 for token in keyword_tokens if token in sent_lower)
            if 0 < overlap < len(keyword_tokens):
                return sentence
        return None

    def build_keyword_example(self, text: str) -> str:
        clean = self.normalize_text(text)
        if not clean:
            return self.primary_kw.title()
        if self.primary_kw in clean.lower():
            return clean
        if clean.endswith("."):
            clean = clean[:-1]
        return f"{self.primary_kw.title()} {clean[0].lower() + clean[1:] if clean else ''}."

    def build_clarity_example(self, text: str) -> str:
        clean = self.normalize_text(text)
        if len(clean.split()) <= 18:
            return clean
        parts = re.split(r'(?<=[.!?])\s+', clean, maxsplit=1)
        if len(parts) == 2:
            return f"{parts[0]} {parts[1]}"
        midpoint = len(clean.split()) // 2
        words = clean.split()
        return " ".join(words[:midpoint]) + ". " + " ".join(words[midpoint:])

    def build_evidence_example(self, text: str) -> str:
        clean = self.normalize_text(text)
        if clean.endswith("."):
            clean = clean[:-1]
        return f"{clean}, backed by a specific threshold, percentage, or cited source."

    def build_structure_example(self, text: str) -> str:
        clean = self.normalize_text(text)
        snippet = self.extract_relevant_snippet(clean, max_words=10)
        return f"{snippet}\nSupporting details\nKey exceptions\nPractical takeaway"

    def build_answer_block_example(self, text: str) -> str:
        clean = self.normalize_text(text)
        sentences = [s.strip() for s in re.split(r'(?<=[.!?])\s+', clean) if s.strip()]
        if not sentences:
            return clean
        return "\n".join(f"- {sentence}" for sentence in sentences[:3])

    def build_citation_example(self, text: str) -> str:
        clean = self.normalize_text(text)
        if clean.endswith("."):
            return f"{clean} (Source: IRS guidance.)"
        return f"{clean} (Source: IRS guidance.)"

    def build_active_voice_example(self, text: str) -> str:
        clean = self.normalize_text(text)
        patterns = [
            (r"\bwas ([a-z]+ed) by ([^.]+)", r"\2 \1"),
            (r"\bwere ([a-z]+ed) by ([^.]+)", r"\2 \1"),
            (r"\bis ([a-z]+ed) by ([^.]+)", r"\2 \1"),
            (r"\bare ([a-z]+ed) by ([^.]+)", r"\2 \1"),
        ]
        for pattern, replacement in patterns:
            rewritten = re.sub(pattern, replacement, clean, flags=re.IGNORECASE)
            if rewritten != clean:
                rewritten = self.normalize_text(rewritten)
                return rewritten[0].upper() + rewritten[1:] if rewritten else clean
        return clean

    def build_pronoun_example(self, text: str) -> str:
        clean = self.normalize_text(text)
        replacements = {
            "It ": "This requirement ",
            "This ": "This requirement ",
            "That ": "That requirement ",
            "These ": "These points ",
            "Those ": "Those requirements ",
        }
        for source, target in replacements.items():
            if clean.startswith(source):
                return target + clean[len(source):]
        return clean

    def build_authority_example(self, text: str) -> str:
        clean = self.normalize_text(text)
        hedging_words = ["might", "could", "possibly", "seems", "appears", "arguably", "may", "perhaps", "allegedly"]
        improved = clean
        for word in hedging_words:
            improved = re.sub(rf"\b{word}\b", "", improved, flags=re.IGNORECASE)
        improved = self.normalize_text(improved)
        return improved or clean

    def build_grammar_example(self, text: str) -> str:
        clean = self.normalize_text(text)
        if clean and clean[0].islower():
            clean = clean[0].upper() + clean[1:]
        return clean

    def is_meaningful_rewrite(self, bad: str, good: str) -> bool:
        bad_norm = self.normalize_for_match(bad)
        good_norm = self.normalize_for_match(good)
        return bool(bad_norm and good_norm and bad_norm != good_norm)

    def build_overall_instruction(self, title: str) -> str:
        title = (title or "").lower()
        if "keyword" in title:
            return f"Add '{self.primary_kw}' naturally in the introduction and one relevant H2."
        if "header" in title or "structure" in title:
            return "Use clearer H2/H3 headings that describe the topic of each section."
        if "redundancy" in title or "repet" in title:
            return "Rewrite repeated passages so each section adds a new point."
        if "length" in title or "coverage" in title:
            return "Expand the article with missing subtopics, examples, and answer blocks."
        if "intent" in title:
            return f"Align the opening sections more directly with {self.search_intent.lower()} intent."
        if "artifact" in title:
            return "Remove editorial notes and replace them with final publish-ready copy."
        return "Revise this area to improve SEO clarity and relevance."

    def normalize_response_examples(self, response: RecommendationsResponse) -> RecommendationsResponse:
        response.overall = [
            recommendation for recommendation in response.overall
            if "PlagiarismScore" not in recommendation.improves and "plagiarism" not in recommendation.whatToChange.lower()
        ]

        opening_text = self.opening_text() or self.article_text()

        for recommendation in response.overall:
            if recommendation.examples is None:
                recommendation.examples = RecommendationExample()

            base_text = opening_text
            recommendation.examples.bad = self.extract_relevant_snippet(base_text)
            title = recommendation.whatToChange.lower()
            recommendation.examples.good = self.build_overall_instruction(title)

            if "keyword" in title:
                recommendation.description = f"The recommendation is based on SEO keyword coverage in the supplied article content for '{self.primary_kw}'."
                recommendation.fix = f"Rework the matched passage so it uses '{self.primary_kw}' or closely related terms where they naturally fit the search intent."
            elif "header" in title or "structure" in title:
                recommendation.description = "The recommendation is based on heading clarity and section hierarchy detected from the article structure."
                recommendation.fix = "Convert generic headings into descriptive H2/H3 labels that explain what the section answers."
            elif "redundancy" in title or "repet" in title:
                recommendation.description = "The recommendation is based on repeated phrasing or overlapping topic coverage found in the article."
                recommendation.fix = "Rewrite repeated passages so each section adds a distinct fact, example, or interpretation."
            elif "length" in title or "coverage" in title:
                recommendation.description = "The recommendation is based on SEO coverage depth and how much supporting context the article provides."
                recommendation.fix = "Add missing subtopics, examples, and answer blocks that make the page more complete for the target query."
            elif "intent" in title:
                recommendation.description = f"The recommendation is based on how clearly the article signals {self.search_intent.lower()} intent."
                recommendation.fix = f"Rewrite the opening and section framing so the article answers the {self.search_intent.lower()} query more directly."
            elif "artifact" in title:
                recommendation.description = "The recommendation is based on editorial or placeholder text detected in the article body."
                recommendation.fix = "Remove drafting notes, placeholders, and non-publishable workflow text."

        for recommendation in response.sectionLevel:
            if recommendation.text:
                if recommendation.examples is None:
                    recommendation.examples = RecommendationExample()
                recommendation.examples.bad = self.extract_relevant_snippet(recommendation.text)
                title = recommendation.whatToChange.lower()

                if "definition" in title or "context" in title:
                    recommendation.description = "This recommendation is based on NLP signals showing that the section introduces topic-specific language without enough explanatory context."
                    recommendation.fix = "Add one or two lines that define the key concept before expanding into detail."
                    recommendation.examples.good = self.build_clarity_example(recommendation.text)
                elif "statistical" in title or "evidence" in title:
                    recommendation.description = "This recommendation is based on SEO credibility signals showing a claim-heavy section without supporting evidence."
                    recommendation.fix = "Support the main claim with a number, threshold, date, or cited source relevant to the section."
                    recommendation.examples.good = self.build_evidence_example(recommendation.text)
                elif "structure" in title:
                    recommendation.description = "This recommendation is based on section hierarchy and topical flow within the article."
                    recommendation.fix = "Split the section into smaller H3-led subtopics so each block answers one clear question."
                    recommendation.examples.good = self.build_structure_example(recommendation.text)
                elif "entity" in title:
                    recommendation.description = "This recommendation is based on entity alignment between the article topic and the section language."
                    recommendation.fix = "Add the most relevant named entities or topic terms where they genuinely clarify the section."
                    recommendation.examples.good = self.build_keyword_example(recommendation.text)
                elif "thin" in title:
                    recommendation.description = "This recommendation is based on section depth and the amount of supporting detail available in the matched passage."
                    recommendation.fix = "Expand the section with examples, exceptions, thresholds, or procedural details."
                    recommendation.examples.good = self.build_answer_block_example(recommendation.text)
                elif "answer block" in title:
                    recommendation.description = "This recommendation is based on SEO readability and answer-block density in the matched section."
                    recommendation.fix = "Convert the matched content into a list, checklist, comparison table, or mini FAQ."
                    recommendation.examples.good = self.build_answer_block_example(recommendation.text)

        for recommendation in response.sentenceLevel:
            if recommendation.text:
                if recommendation.examples is None:
                    recommendation.examples = RecommendationExample()
                recommendation.examples.bad = recommendation.text
                title = recommendation.whatToChange.lower()

                if "grammar" in title:
                    recommendation.description = "This recommendation is based on NLP grammar heuristics applied to the matched sentence."
                    recommendation.fix = "Correct grammar, agreement, punctuation, or casing issues while preserving the sentence meaning."
                    recommendation.examples.good = self.build_grammar_example(recommendation.text)
                elif "keyword" in title:
                    recommendation.description = f"This recommendation is based on sentence-level SEO analysis showing that the target keyword '{self.primary_kw}' is missing from a relevant sentence."
                    recommendation.fix = f"Revise the sentence so it mentions '{self.primary_kw}' naturally."
                    recommendation.examples.good = self.build_keyword_example(recommendation.text)
                elif "passive" in title:
                    recommendation.description = "This recommendation is based on voice and clarity analysis of the matched sentence."
                    recommendation.fix = "Rewrite the sentence in active voice with a direct subject-verb-object structure."
                    recommendation.examples.good = self.build_active_voice_example(recommendation.text)
                elif "source" in title or "citation" in title:
                    recommendation.description = "This recommendation is based on credibility analysis showing a factual or numerical claim without direct attribution."
                    recommendation.fix = "Add a source, citation, or authority reference immediately after the claim."
                    recommendation.examples.good = self.build_citation_example(recommendation.text)
                elif "hedging" in title or "authority" in title:
                    recommendation.description = "This recommendation is based on authority signals showing uncertain language in the matched sentence."
                    recommendation.fix = "Remove unnecessary hedging and support the statement with evidence if needed."
                    recommendation.examples.good = self.build_authority_example(recommendation.text)
                elif "complex" in title:
                    recommendation.description = "This recommendation is based on sentence-length and clause-complexity analysis."
                    recommendation.fix = "Break the sentence into shorter units so each sentence carries one main idea."
                    recommendation.examples.good = self.build_clarity_example(recommendation.text)
                elif "pronoun" in title:
                    recommendation.description = "This recommendation is based on reference clarity and ambiguity in the matched sentence."
                    recommendation.fix = "Replace the opening pronoun with the exact noun it refers to."
                    recommendation.examples.good = self.build_pronoun_example(recommendation.text)

        response.sectionLevel = [
            recommendation for recommendation in response.sectionLevel
            if recommendation.examples
            and self.is_meaningful_rewrite(recommendation.examples.bad or recommendation.text, recommendation.examples.good or "")
            and not self.has_previous_target_match(recommendation.text)
        ]
        response.sentenceLevel = [
            recommendation for recommendation in response.sentenceLevel
            if recommendation.examples
            and self.is_meaningful_rewrite(recommendation.examples.bad or recommendation.text, recommendation.examples.good or "")
            and not self.has_previous_target_match(recommendation.text)
        ]

        return response
    
    def generate_overall_recommendations(self) -> List[ArticleLevelRecommendation]:
        """Generate article-level recommendations"""
        recs = []
        scores = self.score_to_dict()
        
        # 1. Missing Keyword Coverage (Article-level)
        if scores["KeywordScore"] < 10:  # More aggressive in prediction mode
            threshold = 8 if not self.use_predictions else 9
            if scores["KeywordScore"] < threshold:
                rec_text = f"Missing Keyword Optimization in Headers"
                if not self.is_duplicate(rec_text):
                    article_text = self.article_text()
                    opening_text = self.opening_text()
                    target_text = self.find_keyword_gap_sentence() or opening_text or article_text
                    heading_has_keyword = any(
                        self.primary_kw in self.normalize_text(section.text).lower()
                        for section in self.get_heading_sections()
                    )
                    if self.primary_kw not in opening_text.lower() or not heading_has_keyword:
                        recs.append(ArticleLevelRecommendation(
                            whatToChange=rec_text,
                            priority="High",
                            description=f"The primary keyword '{self.primary_kw}' is not reinforced strongly enough in the opening copy and heading structure, which weakens SEO focus.",
                            fix=f"Add '{self.primary_kw}' naturally to the introduction and at least one descriptive H2 that matches the search intent.",
                            improves=["KeywordScore", "EntityAlignmentScore"],
                            examples=RecommendationExample(
                                bad=target_text,
                                good=self.build_keyword_example(target_text)
                            )
                        ))
        
        # 2. Secondary Keyword Integration
        if self.use_predictions and len(self.secondary_kws) > 0:
            missing_secondary = [kw for kw in self.secondary_kws if kw not in " ".join([s.text.lower() for s in self.sections])]
            if missing_secondary and scores["KeywordScore"] < 9:
                rec_text = "Missing Secondary Keyword Distribution"
                if not self.is_duplicate(rec_text):
                    target_text = self.find_keyword_gap_sentence() or self.opening_text() or self.article_text()
                    recs.append(ArticleLevelRecommendation(
                        whatToChange=rec_text,
                        priority="High",
                        description=f"Important related SEO terms are missing or underused across the article: {', '.join(missing_secondary[:3])}. This limits topical breadth.",
                        fix="Integrate missing related terms into the sections where those subtopics are already discussed instead of clustering them in one place.",
                        improves=["KeywordScore", "EntityAlignmentScore"],
                        examples=RecommendationExample(
                            bad=target_text,
                            good=self.build_keyword_example(target_text)
                        )
                    ))
        
        # 3. Content Structure Issues
        if scores["SectionScore"] < 10:
            threshold = 8 if not self.use_predictions else 7
            if scores["SectionScore"] < threshold:
                rec_text = "Unclear Header Structure and Hierarchy"
                if not self.is_duplicate(rec_text):
                    recs.append(ArticleLevelRecommendation(
                        whatToChange=rec_text,
                        priority="High",
                        description="The heading hierarchy does not clearly communicate section intent, which makes the article harder to scan for both readers and search engines.",
                        fix="Convert all numbered prefixes and list-style headers into proper H2/H3 tags. Ensure a maximum depth of 3 levels (H2 → H3 → H4).",
                        improves=["SectionScore", "AnswerBlockDensityScore"],
                        examples=RecommendationExample(
                            bad="1. Introduction\n2. Key Points\n3. Conclusion",
                            good="## Introduction\n### Key Points\n### Conclusion"
                        )
                    ))
        
        # 4. Redundancy Detection
        if scores["VariationScore"] < 10:
            threshold = 7 if not self.use_predictions else 6
            if scores["VariationScore"] < threshold:
                rec_text = "High Redundancy in Content"
                if not self.is_duplicate(rec_text):
                    recs.append(ArticleLevelRecommendation(
                        whatToChange=rec_text,
                        priority="Medium",
                        description="Multiple sections repeat similar concepts with minimal variation, reducing signal and increasing noise.",
                        fix="Consolidate or differentiate repeated sections. Add unique perspectives, examples, or data points to each instance.",
                        improves=["VariationScore", "SignalToNoiseScore"],
                        examples=RecommendationExample(
                            bad="Section A: This process requires careful attention.\nSection B: You need to pay attention to this process.",
                            good="Section A: This process requires careful attention to detail.\nSection B: Automation tools can reduce manual effort in this process."
                        )
                    ))
        
        # 5. Word Count Optimization
        total_words = sum(len(s.text.split()) for s in self.sections)
        word_count_threshold = 600 if self.use_predictions else 800
        if total_words < word_count_threshold:
            rec_text = "Insufficient Content Length"
            if not self.is_duplicate(rec_text):
                recs.append(ArticleLevelRecommendation(
                    whatToChange=rec_text,
                    priority="High",
                    description=f"Article contains only {total_words} words. Comprehensive coverage requires 1500+ words for strong SEO performance.",
                    fix=f"Expand content by adding missing subtopics, detailed examples, and expert insights. Target 1500-2500 words.",
                    improves=["IntentScore", "OriginalInfoScore"],
                    examples=RecommendationExample(
                        bad="Brief overview with 3 paragraphs (~300 words)",
                        good="Comprehensive guide with introduction, background, detailed sections, case studies, and FAQ (~2000 words)"
                    )
                ))
        
        # 6. Intent Alignment
        if scores["IntentScore"] < 10:
            threshold = 8 if not self.use_predictions else 7
            if scores["IntentScore"] < threshold:
                rec_text = "Intent Drift from Search Query"
                if not self.is_duplicate(rec_text):
                    intent_map = {
                        "informational": "educational content, definitions, and how-to guides",
                        "transactional": "product information, pricing, and call-to-action elements",
                        "navigational": "brand-specific information and internal navigation"
                    }
                    intent_goal = intent_map.get(self.search_intent, "aligned content")
                    recs.append(ArticleLevelRecommendation(
                        whatToChange=rec_text,
                        priority="High",
                        description=f"Article content does not match {self.search_intent} search intent. Expected: {intent_goal}",
                        fix=f"Restructure content to prioritize {intent_goal}. Ensure primary message appears in first 100 words and H2 headers.",
                        improves=["IntentScore", "SectionScore"],
                        examples=RecommendationExample(
                            bad="Generic overview of the topic without addressing user's specific need",
                            good="Direct answer with actionable steps tailored to the search intent"
                        )
                    ))
        
        # 8. Non-Content Artifacts (Prediction mode only)
        if self.use_predictions:
            all_text = " ".join([s.text for s in self.sections])
            has_artifacts = any(pattern in all_text.lower() for pattern in ["edit this", "click here", "todo", "placeholder", "[insert"])
            if has_artifacts:
                rec_text = "Remove Non-Content Artifacts"
                if not self.is_duplicate(rec_text):
                    recs.append(ArticleLevelRecommendation(
                        whatToChange=rec_text,
                        priority="High",
                        description="Article contains editing instructions, placeholders, or incomplete sections that should not be published.",
                        fix="Remove all placeholder text, editing notes, and incomplete sections before publication.",
                        improves=["SectionScore", "SignalToNoiseScore"],
                        examples=RecommendationExample(
                            bad="This section [INSERT CASE STUDY]. Todo: Add pricing table.",
                            good="This section includes a detailed case study about ABC Company achieving 40% ROI improvement."
                        )
                    ))
        
        return recs
    
    def generate_section_recommendations(self) -> List[SectionLevelRecommendation]:
        """Generate section-level recommendations"""
        recs = []
        scores = self.score_to_dict()
        
        # Find H2/H3 sections
        sections = [s for s in self.sections if s.level in ["H2", "H3", "paragraph"]]
        paragraph_sections = [s for s in sections if s.level == "paragraph" and len(s.text.split()) > 8]
        
        # 1. Missing Definitions
        if scores["ClaritySynthesisScore"] < 9:
            threshold = 8 if not self.use_predictions else 7
            if scores["ClaritySynthesisScore"] < threshold:
                for i, section in enumerate(paragraph_sections[:3]):
                    if len(section.text.split()) < 100:
                        rec_text = f"Missing Definition or Context"
                        if not self.should_skip_recommendation(rec_text, section.text):
                            recs.append(SectionLevelRecommendation(
                                text=section.text,
                                whatToChange=rec_text,
                                priority="Medium",
                                description="This section references technical terms without defining them, reducing clarity for general audiences.",
                                fix="Add a definition paragraph or inline explanation for technical terms. Use plain language first, then technical detail.",
                                improves=["ClaritySynthesisScore", "SimplicityScore"],
                                examples=self.make_section_example(
                                    section.text,
                                    "Implement the API integration (connecting your system to our service via code) immediately."
                                )
                            ))
                            break
        
        # 2. Missing Statistics/Evidence
        if scores["FactRetrievalScore"] < 9:
            for section in paragraph_sections[:3]:
                has_claim = any(word in section.text.lower() for word in ["important", "significant", "effective", "improve", "benefit", "advantage"])
                has_stat = any(char.isdigit() for char in section.text)
                if has_claim and not has_stat:
                    rec_text = "Missing Statistical Evidence"
                    if not self.should_skip_recommendation(rec_text, section.text):
                        recs.append(SectionLevelRecommendation(
                            text=section.text,
                            whatToChange=rec_text,
                            priority="Medium",
                            description="This section makes claims without supporting data, reducing credibility and answer block density.",
                            fix="Add specific statistics, percentages, or quantified examples. Include source attribution if applicable.",
                            improves=["FactRetrievalScore", "CredibilityScore", "AnswerBlockDensityScore"],
                            examples=self.make_section_example(
                                section.text,
                                "This process improved efficiency by 34% across 200+ customer implementations (2024 case study)."
                            )
                        ))
                        break
        
        # 3. Weak Flow or Misplaced Content
        if scores["SectionScore"] < 8:
            for section in sections:
                if section.level and section.level in ["H2"]:
                    rec_text = "Misaligned Subtopic Structure"
                    if not self.should_skip_recommendation(rec_text, section.text):
                        recs.append(SectionLevelRecommendation(
                            text=section.text,
                            whatToChange=rec_text,
                            priority="Medium",
                            description="This H2 section lacks clear H3 subtopics or contains mixed concepts without logical flow.",
                            fix="Break the section into 2-3 clear H3 subsections, each with a single focused idea.",
                            improves=["SectionScore", "AnswerBlockDensityScore"],
                            examples=self.make_section_example(
                                section.text,
                                "## Process Overview\n### Setup Phase\n### Execution Phase\n### Results Analysis"
                            )
                        ))
                        break
        
        # 4. Missing Entity Alignment
        if scores["EntityAlignmentScore"] < 9:
            for section in paragraph_sections[:2]:
                if not any(entity.lower() in section.text.lower() for entity in self.entities):
                    rec_text = "Missing Entity Context"
                    if not self.should_skip_recommendation(rec_text, section.text):
                        relevant_entities = [e for e in self.entities if len(e) < 20][:2]
                        if relevant_entities:
                            recs.append(SectionLevelRecommendation(
                                text=section.text,
                                whatToChange=rec_text,
                                priority="Medium",
                                description=f"This section does not mention relevant entities ({', '.join(relevant_entities)}), missing AI indexing opportunities.",
                                fix=f"Contextually integrate these entities: {', '.join(relevant_entities)}. Explain their relevance to the topic.",
                                improves=["EntityAlignmentScore", "FactualIsolationScore"],
                                examples=self.make_section_example(
                                    section.text,
                                    f"This approach, used by enterprise platforms like {relevant_entities[0] if relevant_entities else 'leading tools'}, works best..."
                                )
                            ))
                            break
        
        # 5. Thin Sections Needing Expansion (Prediction mode)
        if self.use_predictions and scores["SectionScore"] < 7:
            for section in paragraph_sections:
                if 50 < len(section.text.split()) < 150:
                    rec_text = "Thin Section Requires Expansion"
                    if not self.should_skip_recommendation(rec_text, section.text):
                        recs.append(SectionLevelRecommendation(
                            text=section.text,
                            whatToChange=rec_text,
                            priority="Medium",
                            description=f"This section contains only {len(section.text.split())} words. Sections should contain at least 150+ words for proper context.",
                            fix="Add supporting details, examples, or sub-points to expand this section to 150-200 words.",
                            improves=["SectionScore", "AnswerBlockDensityScore", "IntentScore"],
                            examples=self.make_section_example(
                                section.text,
                                "This is important for businesses because it directly impacts ROI. For example, companies implementing this approach saw cost reductions of 20-35% within the first quarter, with additional benefits including..."
                            )
                        ))
                        break
        
        # 6. Missing Answer Blocks (Prediction mode)
        if self.use_predictions:
            has_lists_or_bullets = any(
                ("•" in s.text or "-" in s.text or "1." in s.text or "* " in s.text) 
                for s in sections
            )
            if not has_lists_or_bullets and scores["AnswerBlockDensityScore"] < 7:
                rec_text = "Missing Structured Answer Blocks"
                target_section = paragraph_sections[0] if paragraph_sections else None
                if target_section and not self.should_skip_recommendation(rec_text, target_section.text):
                    recs.append(SectionLevelRecommendation(
                        text=target_section.text,
                        whatToChange=rec_text,
                        priority="Medium",
                        description="Content lacks structured formats (bullet lists, tables, etc.) reducing AI readability and snippet optimization.",
                        fix="Convert key points into bullet lists, FAQs, or tables to improve answer block density and AI indexing.",
                        improves=["AnswerBlockDensityScore", "TechnicalClarityScore"],
                        examples=RecommendationExample(
                            bad=target_section.text,
                            good="Benefits:\n• Cost reduction: Save 20-30% on operational costs\n• Time savings: Reduce process duration by 45%\n• Improved efficiency: Automate 80% of manual tasks"
                        )
                    ))
        
        return recs
    
    def generate_sentence_recommendations(self) -> List[SentenceLevelRecommendation]:
        """Generate sentence-level recommendations"""
        recs = []
        scores = self.score_to_dict()
        
        # Extract sentences from content
        sentences = []
        sent_index = 0
        for section in self.sections:
            if section.type in {"section", "article", "sentence"}:
                doc = nlp(section.text)
                for sent in doc.sents:
                    sent_text = sent.text.strip()
                    if sent_text and len(sent_text.split()) > 3:
                        sentences.append((sent_index, sent_text))
                        sent_index += 1
        
        if not sentences:
            return recs
        
        # 1. Grammar Corrections
        if scores["GrammarScore"] < 10:
            threshold = 9 if not self.use_predictions else 8
            if scores["GrammarScore"] < threshold:
                for idx, sent_text in sentences[:8]:
                    if not check_grammar_heuristics(nlp(sent_text), sent_text):
                        rec_text = "Grammar Correction Required"
                        if not self.should_skip_recommendation(rec_text, sent_text):
                            recs.append(SentenceLevelRecommendation(
                                text=sent_text,
                                sentenceIndex=idx,
                                whatToChange=rec_text,
                                priority="High",
                                description="Sentence contains grammatical error affecting readability and professionalism.",
                                fix="Correct tense, subject-verb agreement, or punctuation issues.",
                                improves=["GrammarScore", "SimplicityScore"],
                                examples=RecommendationExample(
                                    bad=sent_text,
                                    good="[Corrected version with proper grammar]"
                                )
                            ))
                            break
        
        # 2. Keyword Insertion (Sentence-level)
        if scores["KeywordScore"] < 9:
            for idx, sent_text in sentences[5:20]:
                if (self.primary_kw not in sent_text.lower() and 
                    any(kw in sent_text.lower() for kw in self.secondary_kws)):
                    rec_text = "Natural Keyword Insertion Opportunity"
                    if not self.should_skip_recommendation(rec_text, sent_text):
                        recs.append(SentenceLevelRecommendation(
                            text=sent_text,
                            sentenceIndex=idx,
                            whatToChange=rec_text,
                            priority="High",
                            description=f"This sentence discusses related topics but misses the primary keyword '{self.primary_kw}'.",
                            fix=f"Naturally integrate '{self.primary_kw}' into the sentence without forcing or creating awkward phrasing.",
                            improves=["KeywordScore", "SectionScore"],
                            examples=RecommendationExample(
                                bad=sent_text,
                                good=f"Rewritten sentence incorporating '{self.primary_kw}' naturally"
                            )
                        ))
                        break
        
        # 3. Passive Voice Conversion
        if scores["SimplicityScore"] < 9:
            threshold = 8 if not self.use_predictions else 7
            if scores["SimplicityScore"] < threshold:
                for idx, sent_text in sentences:
                    doc = nlp(sent_text)
                    if any(t.dep_ == "auxpass" for t in doc) and len(sent_text.split()) > 18:
                        rec_text = "Passive Voice Simplification"
                        if not self.should_skip_recommendation(rec_text, sent_text):
                            recs.append(SentenceLevelRecommendation(
                                text=sent_text,
                                sentenceIndex=idx,
                                whatToChange=rec_text,
                                priority="Medium",
                                description="Long passive voice sentence reduces clarity. Converting to active voice improves readability.",
                                fix="Rewrite in active voice: '[Subject] [Verb] [Object]' format.",
                                improves=["SimplicityScore", "ClaritySynthesisScore"],
                                examples=self.make_sentence_example(
                                    sent_text,
                                    "The team decided on the implementation strategy."
                                )
                            ))
                            break
        
        # 4. Citation Addition
        if scores["CredibilityScore"] < 9:
            numeric_patterns = re.findall(r"\b\d+%|\$\d+|[\d,]+(?:\s+(dollars|thousand|million|times|cases|items))?\b", " ".join([s[1] for s in sentences]))
            if numeric_patterns:
                for idx, sent_text in sentences:
                    if any(pattern in sent_text for pattern in numeric_patterns):
                        if not URL_PATTERN.search(sent_text):
                            rec_text = "Missing Source Attribution"
                            if not self.should_skip_recommendation(rec_text, sent_text):
                                recs.append(SentenceLevelRecommendation(
                                    text=sent_text,
                                    sentenceIndex=idx,
                                    whatToChange=rec_text,
                                    priority="High",
                                    description="Sentence contains statistics or claims without source attribution, reducing credibility.",
                                    fix="Add hyperlinked source or attribution: 'According to [Source], [statistic]'",
                                    improves=["CredibilityScore", "FactRetrievalScore"],
                                    examples=self.make_sentence_example(
                                        sent_text,
                                        "According to [Industry Report 2024], businesses save 40% on costs using this approach."
                                    )
                                ))
                                break
        
        # 5. Hedging Language Removal (for Authority)
        if scores["AuthorityScore"] < 9:
            threshold = 8 if not self.use_predictions else 7
            if scores["AuthorityScore"] < threshold:
                hedging_words = ["might", "could", "possibly", "seems", "appears", "arguably", "may", "perhaps", "allegedly"]
                for idx, sent_text in sentences:
                    if any(word in sent_text.lower() for word in hedging_words):
                        rec_text = "Remove Hedging Language"
                        if not self.should_skip_recommendation(rec_text, sent_text):
                            recs.append(SentenceLevelRecommendation(
                                text=sent_text,
                                sentenceIndex=idx,
                                whatToChange=rec_text,
                                priority="Medium",
                                description="Hedging words ('might', 'seems', 'possibly') weaken authority and confidence in the statement.",
                                fix="Replace hedging language with definitive statements backed by evidence.",
                                improves=["AuthorityScore", "ExpertiseScore"],
                                examples=self.make_sentence_example(
                                    sent_text,
                                    "This approach delivers consistent results across enterprise implementations."
                                )
                            ))
                            break
        
        # 6. Long Complex Sentence Breaking (Prediction mode)
        if self.use_predictions and scores["SimplicityScore"] < 7:
            for idx, sent_text in sentences:
                if len(sent_text.split()) > 35:
                    has_multiple_clauses = sent_text.count(" and ") + sent_text.count(" but ") + sent_text.count(" or ") > 2
                    if has_multiple_clauses:
                        rec_text = "Break Complex Sentence"
                        if not self.should_skip_recommendation(rec_text, sent_text):
                            recs.append(SentenceLevelRecommendation(
                                text=sent_text,
                                sentenceIndex=idx,
                                whatToChange=rec_text,
                                priority="Medium",
                                description=f"Sentence is too long ({len(sent_text.split())} words) with multiple clauses, reducing clarity.",
                                fix="Split into 2-3 shorter sentences with a single idea each.",
                                improves=["SimplicityScore", "ClaritySynthesisScore"],
                                examples=RecommendationExample(
                                    bad=sent_text[:80] + "...",
                                    good="[Split into clearer, shorter sentences]"
                                )
                            ))
                            break
        
        # 7. Pronoun Reference Clarity (Prediction mode)
        if self.use_predictions:
            for idx, sent_text in sentences:
                if sent_text.lower().startswith(("it ", "this ", "that ", "these ", "those ")):
                    if idx > 0:  # Has previous sentence
                        rec_text = "Clarify Pronoun Reference"
                        if not self.should_skip_recommendation(rec_text, sent_text):
                            recs.append(SentenceLevelRecommendation(
                                text=sent_text,
                                sentenceIndex=idx,
                                whatToChange=rec_text,
                                priority="Low",
                                description="Sentence begins with ambiguous pronoun that may not clearly reference the previous sentence.",
                                fix="Replace pronoun with the specific noun or rephrase to clarify the reference.",
                                improves=["ClaritySynthesisScore", "SimplicityScore"],
                                examples=self.make_sentence_example(
                                    sent_text,
                                    "The system has multiple features. These key features are essential for users."
                                )
                            ))
                            break
        
        return recs
    
    def generate_all_recommendations(self) -> RecommendationsResponse:
        """Generate all recommendations"""
        response = RecommendationsResponse(
            overall=self.generate_overall_recommendations(),
            sectionLevel=self.generate_section_recommendations(),
            sentenceLevel=self.generate_sentence_recommendations()
        )
        return self.normalize_response_examples(response)


@app.post("/recommendations", response_model=RecommendationsResponse)
def get_recommendations(
    request: Union[RecommendationRequest, RecommendationRequestInput] = Body(...)
):
    """Generate SEO and AI indexing recommendations from either supported request shape."""
    normalized_request = normalize_recommendation_request(request)
    generator = RecommendationGenerator(normalized_request)
    return generator.generate_all_recommendations()


@app.post("/recommendations-input", response_model=RecommendationsResponse)
def get_recommendations_from_input(request: RecommendationRequestInput):
    """Generate recommendations from the Postman-friendly request shape."""
    normalized_request = normalize_recommendation_request(request)
    generator = RecommendationGenerator(normalized_request)
    return generator.generate_all_recommendations()

    try:
        # STEP 1: Transform sections - map SectionText → text, create proper ContentSection objects
        sections_list = []
        for idx, section in enumerate(request.sections):
            # Add section header with the section text
            sections_list.append(ContentSection(
                type="section_header",
                text=section.SectionText,
                level="H2",
                index=idx
            ))
            
            # Add each sentence from the section as a separate content item
            for sent in section.Sentences:
                sections_list.append(ContentSection(
                    type="sentence",
                    text=sent,
                    level="paragraph",
                    index=idx
                ))
        
        # STEP 2: Build ScoreCard from Scores dict - NOW ACCEPTS BOTH INT AND FLOAT
        # No conversion needed - ScoreCard now has Union[int, float] fields
        score_dict = request.Scores
        scorecard = ScoreCard(
            IntentScore=score_dict.get("IntentScore", 5),
            SectionScore=score_dict.get("SectionScore", 5),
            KeywordScore=score_dict.get("KeywordScore", 5),
            OriginalInfoScore=score_dict.get("OriginalInfoScore", 5),
            ExpertiseScore=score_dict.get("ExpertiseScore", 5),
            CredibilityScore=score_dict.get("CredibilityScore", 5),
            AuthorityScore=score_dict.get("AuthorityScore", 5),
            SimplicityScore=score_dict.get("SimplicityScore", 5),
            GrammarScore=score_dict.get("GrammarScore", 5),
            VariationScore=score_dict.get("VariationScore", 5),
            PlagiarismScore=score_dict.get("PlagiarismScore", 5),
            ClaritySynthesisScore=score_dict.get("ClaritySynthesisScore", 5),
            FactRetrievalScore=score_dict.get("FactRetrievalScore", 5),
            AnswerBlockDensityScore=score_dict.get("AnswerBlockDensityScore", 5),
            FactualIsolationScore=score_dict.get("FactualIsolationScore", 5),
            EntityAlignmentScore=score_dict.get("EntityAlignmentScore", 5),
            TechnicalClarityScore=score_dict.get("TechnicalClarityScore", 5),
            SignalToNoiseScore=score_dict.get("SignalToNoiseScore", 5)
        )
        
        # STEP 3: Create standard RecommendationRequest with ALL properly mapped fields
        # Map user's field names to standard field names
        standard_request = RecommendationRequest(
            sections=sections_list,
            scoreCard=scorecard,
            primaryKeyword=request.PrimaryKeyword,
            secondaryKeywords=request.secondaryKeywords or [],
            entities=request.entities or [],
            searchIntent=request.SearchIntent,
            previousRecommendations=request.previousRecommendations
        )
        
        # Generate recommendations
        generator = RecommendationGenerator(standard_request)
        return generator.generate_all_recommendations()
    
    except Exception as e:
        import traceback
        error_msg = f"Error processing recommendations: {str(e)}\n{traceback.format_exc()}"
        print(error_msg)
        raise ValueError(error_msg)


def extract_seo_label_generic(text: str):
    """
    Generic SEO Label Extractor: 
    Handles: 'Step 1:', 'v)', '1.1.', 'Capability #2 -', 'Phase A =>'
    Separators: :, :-, --, -, |, ., ), ~, =>
    """
    if not text:
        return None, ""

    # 1. List markers like 'v)', '1.', 'a)'
    list_marker = r"(?:[ivxclm]+|[a-z]|\d+(?:\.\d+)*)\s?[.)\-]+"
    
    # 2. SEO Keywords like 'Step 1', 'Capability #2'
    anchors = r"(?:Step|Capability|Component|Part|Point|Phase|Requirement|Module|Factor|Section|Level|Task)\s?#?[a-zA-Z0-9]+"
    
    # 3. All common separators used in SEO headers
    # Isme humne covers kiye: :, :-, --, |, ., ), ~, =>, aur Em-dash
    separators = r"(?:\s?[:-]{1,2}|\s?[:.\-|—~]|\s?=>|\s?\))*"
    
    # Combined Regex: Match start of string with either marker or anchor + any separator
    # Group 1 captures the actual label for your records
    full_pattern = rf"^((?:{list_marker}|{anchors}){separators})\s*"
    
    match = re.match(full_pattern, text, re.IGNORECASE)
    
    if match:
        label_part = match.group(1).strip()
        # Pure match length (including trailing spaces) ko uda rahe hain
        cleaned_content = text[len(match.group(0)):].strip()
        
        # Cleanup: Agar cleaning ke baad pehla char lowercase hai (jaise label ke baad space ho)
        if cleaned_content:
            cleaned_content = cleaned_content[0].upper() + cleaned_content[1:]
            
        return label_part, cleaned_content
    
    # Agar kuch match nahi hua toh original text return karo
    return None, text
    
    
    
@app.post("/process-article", response_model=AnalysisResponse)
def process_article(request: ArticleRequest):
    soup = BeautifulSoup(request.htmlContent, "html.parser")
    results, first_id, s_count, p_count = [], None, 1, 1
    
    kw_doc = nlp(request.primaryKeyword.lower())
    state = {"is_keyword_active": True}
    processed_texts = set()

    for block in soup.find_all(is_block_element):
        if any(is_block_element(p) for p in block.parents): 
            continue
            
        raw_text = block.get_text(separator=" ", strip=True)
        if not raw_text or raw_text in processed_texts: 
            continue

        # 1. TR Handling
        if block.name == 'tr':
            raw_text = " - ".join([td.get_text(strip=True) for td in block.find_all('td') if td.get_text(strip=True)])

        # --- NEW CLEANING LOGIC START ---
        # Agar block header hai, toh sentence splitting se PEHLE label udao
        if block.name in ["h1", "h2", "h3", "h4", "h5", "h6"]:
            # extract_seo_label_generic sirf label return karega aur bacha hua mal-paani
            _, cleaned_text = extract_seo_label_generic(raw_text)
            
            # Agar cleaning ke baad kuch bacha, toh usey hi use karo
            # Varna agar sirf label hi tha header mein, toh raw_text ko empty kar do skip karne ke liye
            raw_text = cleaned_text if cleaned_text.strip() else ""
        # --- NEW CLEANING LOGIC END ---

        if not raw_text: # Skip if the header became empty after cleaning
            continue

        # 2. Logical Sentence Splitting (Ab cleaned text pe split hoga)
        doc = nlp(raw_text)
        sentences = [sent.text.strip() for sent in doc.sents if sent.text.strip()]

        for sentence_text in sentences:
            res = analyze_logic(
                text=sentence_text, 
                s_id=f"S{s_count}", 
                keyword_doc=kw_doc, 
                state=state, 
                h_tag=block.name, 
                p_id=f"P{p_count}"
            )

            # Agar analyze_logic None return kare (additional safety), toh skip
            if res is None:
                continue

            if res.answerSentenceFlag == 1 and first_id is None: 
                first_id = res.SentenceId
                
            results.append(res)
            s_count += 1 
        
        processed_texts.add(raw_text)
        p_count += 1

    return AnalysisResponse(sentences=results, answerPositionIndex=first_id)
    
    
if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
