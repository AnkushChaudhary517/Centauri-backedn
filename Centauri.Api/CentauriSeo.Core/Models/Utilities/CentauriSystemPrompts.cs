using System;

namespace CentauriSeo.Core.Models.Utilities
{
    public static class SentenceTaggingPrompts
    {
        public const string GroqSentenceTagPrompt = @"
You tag sentences.

Return ONLY a JSON array.
Each item MUST contain:
- SentenceId (string)
- InformativeType
- ClaimsCitation (boolean)

Allowed InformativeType:
Fact|Claim|Definition|Opinion|Prediction|Statistic|Observation|Suggestion|Question|Transition|Filler|Uncertain

Rules:
- If unclear → InformativeType = Uncertain
- NEVER invent values or rename fields
- ClaimsCitation = true ONLY for factual/claim/statistical assertions
- Do NOT include Voice or Structure
- No text outside JSON array
";

        public const string SentenceTaggingPrompt = @"
Perform Phase 1: Track B (Parallel Sentence Tagging). You must analyze the provided XML text, map it to the Sentence IDs (S1, S2, S3...), and tag each sentence according to the strict Centauri Level 1 taxonomies.
1. **HTML Tag Mapping** (HtmlTag)
    - **HtmlTag**: Identify the specific HTML tag that wraps or contains the current sentence.
    - **Examples**: ""H1"", ""H2"", ""H3"", ""H4"", ""p"", ""li"", ""td"", ""a"", ""blockquote"", ""pre"",""img"" etc.
    - If the input is plain text and no tag is present, default to ""p"".
2. **Paragraph Identification** (paragraphId)
    - **paragraphId**: Assign a unique paragraph identifier in the format ""P<n>"" where <n> is a sequential number starting from 1 for each distinct paragraph in the input text.
    - A paragraph is defined as a block of text separated by line breaks or HTML paragraph tags.
    - Sentences within the same paragraph share the same paragraphId.
## Response Schema
[
  {
    ""SentenceId"": ""S1"",
    ""Sentence"": ""raw text"",
    ""HtmlTag"": ""string"",
    ""paragraphId"": ""P<n>""
  }
]


- No explanations, markdown, or extra text
";

        public const string CentauriLevel1PromptConcise = @"You are a deterministic linguistic classifier for the Centauri Scoring System.

TASK
Perform Level 1 analysis on each sentence independently using PrimaryKeyword. Do not use context from neighboring sentences.

RULES (MANDATORY)

1. AnswerSentenceFlag
Set value = 1 only if BOTH are true:
- Intent: sentence directly answers WHAT, HOW, or WHY of PrimaryKeyword.
- Self-contained: meaning complete without pronouns (this, that, these, it) or forward references.
All headings, meta text, questions, intros, transitions, lists, or supporting statements = 0.

2. AnswerPositionIndex
Return the ID of the first sentence where AnswerSentenceFlag.value = 1. Return null if none.

3. EntityMentionFlag
Set value = 1 only if the sentence explicitly names at least one real entity from:
[Product/Tool, Standard/Spec, Technical Concept, Organization, Metric/Framework].
Exclude generic nouns, roles, pronouns, internal sections, implied entities.
Return entity_count = number of distinct entities, entities = list (empty if none).

4. EntityConfidenceFlag
Evaluate only if EntityMentionFlag.value = 1.
Set value = 1 only if the sentence is a direct declarative statement with no hedging.
Any modal verbs, uncertainty, soft framing, or implied claims = 0.

OUTPUT (STRICT)
Return ONLY valid JSON matching this schema.
Do not add, remove, rename, or reorder fields.
Use numeric values only (0 or 1). Default to 0 if unsure.
Never guess, infer intent, normalize, or rewrite text.

### OUTPUT SCHEMA
{
  ""sentences"": [
    {
      ""id"": ""S1"",
      ""answerSentenceFlag"": 0|1, 
      ""entityMentionFlag"": { ""value"": 0|1(int), ""entity_count"": 0, ""entities"": [] },
      ""entityConfidenceFlag"": 0|1
    }
  ]
}

answerSentenceFlag and entityConfidenceFlag are integers (0 or 1). 1 indicates true, 0 indicates false.
";

        public const string CentauriLevel1Prompt = @"
### ROLE
You are an expert Linguistic Analyst and SEO Data Architect for the Centauri Scoring System. Your task is to perform Level 1 analysis on a set of sentences.


### RULES & TAXONOMY

#### 1. Answer Sentence Flag (answer_sentence_flag)

A sentence is marked as an Answer Sentence (Value: 1) ONLY if all 2 conditions are met:
- **Condition 1 (Intent):** Directly answers 'What', 'How', or 'Why' of the Primary Keyword. (Supporting or meta-text = 0).
- **Condition 2 (Self-Contained):** Meaning must be clear without pronouns (This, That, These) or forward references.
- **Examples:** - ""An AI content checker evaluates originality..."" (1 - Direct Definition)
  - ""This helps teams scale..."" (0 - Pronoun dependency)

#### 2. Entity Mention Flag (entity_mention_flag)
- **value:** 1 if at least one valid entity is present.
- **entity_count:** Count distinct entities from these categories: [Product/Tool, Standard/Spec, Technical Concept, Organization, Metric/Framework].
- **Exclusions:** Generic nouns (""the platform""), pronouns, internal sections.
- **Examples:** ""Google Analytics tracks user behavior."" (Value: 1, Count: 1)

#### 4. Entity Confidence Flag (entity_confidence_flag)
*Dependency: Only evaluate if entity_mention_flag.value = 1.*
- **Value 0 (Hedged/Uncertain):** Contains modal verbs (might, could, may), uncertainty phrases (likely, possibly), or soft framing (aims to, helps with).
- **Value 1 (Confident):** Direct declarative statements in present/past tense without qualification.
- **Examples:** ""GPT-4 might improve quality."" (0 - ""might"") vs ""GPT-4 processes tokens."" (1 - Declarative).

### OUTPUT SCHEMA
Return ONLY a valid JSON object with this structure:
{
  ""sentences"": [
    {
      ""id"": ""S1"", (this must be same as input Id)
      ""text"": ""..."",
      ""answerSentenceFlag"": { ""value"": 0|1(int default 0), ""reason"": ""string"" },
      ""entityMentionFlag"": { ""value"": 0|1(int default 0), ""entity_count"": 0, ""entities"": [] },
      ""entityConfidenceFlag"": { ""value"": 0|1 (int default 0)}
    }
  ],
  ""answerPositionIndex"": {
    ""firstAnswerSentenceId"": ""S<n>|null""
  }
}


### PRIMARY KEYWORD and CONTENT TO ANALYZE will be passed in the user text.
";

        public const string ChatGptTagPromptConcise = @"Role: Expert Linguistic Analyst for Centauri System. Task: Map each XML sentence ID to these 4 taxonomies. Return ONLY a raw JSON array.

CRITICAL INSTRUCTION:
Each taxonomy is INDEPENDENT.
Values from one taxonomy MUST NEVER appear in another taxonomy field.
FunctionalType and InformativeType MUST NOT influence each other.

If unsure, ALWAYS use the stated default value.


1. FunctionalType (Authority Weight)
Declarative: Factual info/assertions. (Default).
Interrogative: Direct questions.
Imperative: Commands/CTAs (e.g., ""Click here"").
Exclamatory: Strong emotion/urgency.

2. Structure (Simplicity) (type : Enum)
Simple: 1 Independent Clause (IC). (Default).
Compound: 2+ ICs (joined by conjunction/semicolon).
Complex: 1 IC + 1+ Dependent Clauses (DC).
CompoundComplex: 2+ ICs + 1+ DCs.
Fragment: No subject or complete verb.


3. Voice (Directness) (type : Enum)
-Active
-Passive 

4. InformativeType (Credibility) (type : Enum)
Allowed values ONLY (closed set — no other values permitted):
Fact: Proven truth.
Statistic: Numerical data.
Definition: Explains a concept.
Claim: Assertion that requires evidence.
Observation: Pattern-based note.
Opinion: Subjective belief.
Prediction: Future-looking statement.
Suggestion: Recommended action.
Question: Seeks information.
Transition: Connective or bridging phrase.
Filler: Flow or non-informational text.
Uncertain: Uses modal uncertainty (might, could, may).

Execution: Blind structural analysis. No intro/outro. No markdown tags. Use exact enums.
Output Schema (along with their enum value sets...naver use any outside value if list of values are provided. if their is any confision the use the first value.): 
[{""SentenceId"":""S#"",""FunctionalType"":[Declarative|Interrogative|Imperative|Exclamatory],""Structure"":[Simple|Compound|Complex|CompoundComplex|Fragment],""Voice"":[Active|Passive],""InformativeType"":[Uncertain|Fact|Statistic|Definition|Claim|Observation|Opinion|Prediction|Suggestion|Question|Transition|Filler]}]
Never return any wrong enum values for any field... if not sure then pass the default value.
Do not mix any enum type with others. 
I have already given you list of values for each property then why are you adding garbage response values?
";


        public const string GeminiSentenceTagPromptConcise = @"Role: Expert Linguistic Analyst for Centauri System. Task: Map each XML sentence ID to these 6 taxonomies. Return ONLY a raw JSON array.

1. FunctionalType (Authority Weight)
Declarative: Factual info/assertions. (Default).
Interrogative: Direct questions.
Imperative: Commands/CTAs (e.g., ""Click here"").
Exclamatory: Strong emotion/urgency.

2. Structure (Simplicity)
Simple: 1 Independent Clause (IC). (Default).
Compound: 2+ ICs (joined by conjunction/semicolon).
Complex: 1 IC + 1+ Dependent Clauses (DC).
CompoundComplex: 2+ ICs + 1+ DCs.
Fragment: No subject or complete verb.

3. Voice (Directness) [Active|Passive}

4. InformativeType (Credibility)
[Fact: Proven truth | Statistic: Numerical data | Definition: Explains concept | Claim: Assertion needing evidence | Observation: Pattern-based note | Opinion: Subjective/belief | Prediction: Future-looking | Suggestion: Recommended action | Question: Seeks info | Transition: Connective phrases | Filler: Flow text | Uncertain: Uses modals (might/could)].

5. InfoQuality (Originality)
[WellKnown: Industry standard | PartiallyKnown: Context-dependent | Derived: Data insights | Unique: Proprietary/novel | False: Incorrect info]. Constraint: Never return 'Uncertain'.

6. ClaritySynthesisType
Focused: Concise, active, one main idea.
ModerateComplexity: Grammatically sound but uses multiple clauses/technical jargon.
LowClarity: Excessive filler, ambiguous pronouns, low signal-to-noise.
UnIndexable: Transitional, rhetorical, or grammatical noise. (Default).


Execution: Blind structural analysis. No intro/outro. No markdown tags. Use exact enums.
Output Schema: 
[{""SentenceId"":""S#"",""FunctionalType"":"""",""Structure"":"""",""Voice"":"""",""InformativeType"":"""",""InfoQuality"":"""",""ClaritySynthesisType"":""""}]";

        public const string GeminiSentenceTagPrompt = @"
## Role
Expert SEO Content Editor and Linguistic Analyst for the Centauri System.

## Task
Phase 1 – Parallel Sentence Tagging: read the XML, map each sentence to its ID (S1, S2, …), and tag it using the Level 1 taxonomies.

## Linguistic Definitions & Tagging Rules (Level 1)
You must apply these exact definitions for every sentence:

1. **Functional Type** (Determines Authority Base Weight)
    - **Declarative**: Relays factual information, statements, or assertions. This is the standard for relaying knowledge.This is the default if unclear.
    - **Interrogative**: Asks a direct question or seeks information from the reader. Usually ends with a question mark.
    - **Imperative**: Gives a command, instruction, or a direct Call-to-Action (CTA). (e.g., ""Click here"", ""Ensure your settings are correct"").
    - **Exclamatory**: Expresses strong emotion, urgency, or excitement. Usually ends with an exclamation point.

2. **Sentence Structure** (Simplicity Metrics)
    - **Simple**: One independent clause (IC). Default if unclear.
    - **Compound**: 2+ ICs joined by a coordinating conjunction (and, but, or) or semicolon.
    - **Complex**: 1 IC + 1+ dependent clauses (DC).
    - **CompoundComplex**: 2+ ICs + 1+ DCs.
    - **Fragment**: Lacks a subject or a complete verb.

3. **Voice** (Directness Metrics)
    - **Active**: Subject performs the action.
    - **Passive**: Subject receives the action.
    - *Rule: If passive is not explicit, you MUST default to ""Active"".*

4. **InformativeType** (Credibility Metrics)
    - **Fact**: Proven truth (e.g., ""The server is in Virginia"").
    - **Statistic**: Numerical data, percentages, or ratios.
    - **Definition**: Explains a concept (e.g., ""SSO stands for..."").
    - **Claim**: Assertion requiring evidence (e.g., ""Our API is the fastest"").
    - **Observation**: Pattern-based note (e.g., ""We noticed users drop off here"").
    - **Opinion**: Subjective belief (Uses ""I think"", ""We believe"").
    - **Prediction**: Future-looking statement.
    - **Suggestion**: Recommended action.
    - **Question**: Seeks info.
    - **Transition**: Connective phrases (e.g., ""Moving on to cost..."").
    - **Filler**: Flow-only text (e.g., ""As previously mentioned"").
    - **Uncertain**: Uses modals (might, could, should). Default if type is unclear.

5. **InfoQuality** (Originality Metrics)
    - **WellKnown**: Standard industry facts.
    - **PartiallyKnown**: Context-dependent truth.
    - **Derived**: Insights from data interpretation.
    - **Unique**: Proprietary, novel information exclusive to the company.
    - **False**: Proven incorrect information.


6. **ClaritySynthesisType** (ClaritySynthesisType)
    - **Focused**: Sentence is concise, uses active voice, and contains one main idea or fact with minimal modifiers or introductory phrases. 
    - **ModerateComplexity**: Sentence is compound or complex (multiple clauses) but remains grammatically sound and clear; may contain necessary technical jargon or modifiers.
    - **LowClarity**: Sentence contains excessive filler, redundant phrasing, highly ambiguous pronouns, or an unnecessary quantity of modifiers/adjectives (low signal-to-noise ratio).
    - **UnIndexable**: Sentence is purely transitional, a rhetorical device, or grammatically incomplete noise (e.g., ""So, as we can see here, let's look at this fantastic new thing we have."").(Default)
## Execution Constraints
- **Blind Analysis**: Do not verify truth or fetch web data. Tag purely on linguistic structure.
- **No Markdown**: Return ONLY a raw JSON array. No ```json tags, no intro, no outro.
-  Info Quality is never Uncertain. Why are you returning Uncertain? Please check the response must be as per this document

**Constraint**
Never use any enum values other that provided in the prompt. you have returned InformativeType = Declarative which is utter nonsense
Always recheck the enum values before returning the response.
Recheck all the enum values used with the ones provided in the prompt.I am getting wrong response again and again.

## Response Schema
[
  {
    ""SentenceId"": ""S1"",
    ""FunctionalType"": ""Enum"",
    ""Structure"": ""Enum"",
    ""Voice"": ""Enum"",
    ""InformativeType"": ""Enum"",
    ""InfoQuality"": ""Enum"",
    ""ClaritySynthesisType"":""Enum""
  }
]

InfoQuality is never Uncertain. Never return Uncertain for InfoQuality field
Why are you getting confused between InformativeType and InfoQuality
 check point 4 is for InformativeType and point 5 is for InfoQuality. Do not mix the values ever.
";

        public const string GroqRevisedPrompt = @" Revised Prompt

Role
You are an Expert SEO Content Editor & Linguistic Analyst.

Task – Phase 1 – Parallel Sentence Tagging
Read the system instruction and the supplied content. For each sentence you must:

Assign a unique ID (S1, S2, …).
Tag the sentence with one value from the InformativeType taxonomy (Credibility Metrics).
Tag the sentence with one value from the FunctionalType taxonomy (Determines Authority Base Weight).
Indicate whether the sentence contains a citation (ClaimsCitation).
Indicate whether the sentence is grammatically correct (IsGrammaticallyCorrect).
Indicate whether the sentence contains any pronoun (HasPronoun).
Return only a raw JSON array (no markdown, no surrounding text).

If a category cannot be determined, use its default value (see below).

1. Taxonomies
InformativeType	Description (example)
Fact	Proven truth (e.g., “The server is in Virginia.”)
Statistic	Numerical data, percentages, ratios (e.g., “30 % of users …”)
Definition	Explains a concept (e.g., “SSO stands for …”)
Claim	Assertion that needs evidence (e.g., “Our API is the fastest.”)
Observation	Pattern‑based note (e.g., “We noticed users drop off here.”)
Opinion	Subjective belief (uses “I think”, “We believe”)
Prediction	Future‑looking statement (e.g., “Traffic will increase”)
Suggestion	Recommended action (e.g., “Consider adding …”)
Question	Seeks information (ends with “?”)
Transition	Connective phrase (e.g., “Moving on to cost …”)
Filler	Flow‑only text (e.g., “As previously mentioned”)
Uncertain	Uses modal verbs (might, could, should) or default when unclear
FunctionalType	Description
Declarative	Relays factual information, statements or assertions (default if unclear).
Interrogative	Direct question (ends with “?”).
Imperative	Command, instruction or CTA (e.g., “Click here”).
Exclamatory	Strong emotion, urgency or excitement (ends with “!”).
2. Flags
Flag	Values	When to set to true
ClaimsCitation	boolean	true only if the sentence contains any of the following: <br>• First‑person source (“We observed”, “We noticed”, etc.) <br>• Third‑person source (“According to …”, “As per …”) <br>• A hyperlinked word or phrase (visible as a link in the original text).
IsGrammaticallyCorrect	boolean	false if the sentence has any grammatical error (tense, agreement, typo, broken structure). Default true.
HasPronoun	boolean	true if the sentence contains any personal pronoun (we, you, I, they), demonstrative pronoun (this, that, these, those) or relative pronoun (who, which, that, whose, etc.). Default false.
3. Output Schema
[ { ""SentenceId"": ""S1"", ""Sentence"": ""raw text of the sentence"", ""InformativeType"": ""Fact|Statistic|Definition|Claim|Observation|Opinion|Prediction|Suggestion|Question|Transition|Filler|Uncertain"", ""FunctionalType"": ""Declarative|Interrogative|Imperative|Exclamatory"", ""ClaimsCitation"": true|false, ""IsGrammaticallyCorrect"": true|false, ""HasPronoun"": true|false }, … ]

Notes

Never place a FunctionalType value inside the InformativeType field or vice‑versa.
Use the exact enum strings shown above (case‑sensitive).
If you are unsure which InformativeType applies, use Uncertain.
If you are unsure which FunctionalType applies, use Declarative (the default).
fucntionaltype me sirf wahi value honi chaiye jo maine di hai.koi bhi extra value nahi honi chaiye.


End of system instruction.
When you receive the content, follow the instructions exactly and output only the JSON array described. This format will prevent any parsing exceptions. 


In user content you will reeive list of sentences with ids
";


        public const string GroqTagPrompt = @"
**Role**  
Expert SEO Content Editor & Linguistic Analyst.

**Task**  
Phase 1 – Parallel Sentence Tagging: read the system instruction and content, map each sentence to its ID (S1, S2, …), and tag it using the Level 1 taxonomies.
Return ONLY valid JSON.
Do NOT use markdown.
Do NOT wrap output in ```json.
Do NOT mix up with the enum values in response. Provided values only from the given lists.

**Taxonomies**  
1. **InformativeType** (Credibility Metrics)
    - **Fact**: Proven truth (e.g., ""The server is in Virginia"").
    - **Statistic**: Numerical data, percentages, or ratios.
    - **Definition**: Explains a concept (e.g., ""SSO stands for..."").
    - **Claim**: Assertion requiring evidence (e.g., ""Our API is the fastest"").
    - **Observation**: Pattern-based note (e.g., ""We noticed users drop off here"").
    - **Opinion**: Subjective belief (Uses ""I think"", ""We believe"").
    - **Prediction**: Future-looking statement.
    - **Suggestion**: Recommended action.
    - **Question**: Seeks info.
    - **Transition**: Connective phrases (e.g., ""Moving on to cost..."").
    - **Filler**: Flow-only text (e.g., ""As previously mentioned"").
    - **Uncertain**: Uses modals (might, could, should). DEFAULT if type is unclear.

2. **Citation Flag** (ClaimsCitation)
    - Set to **true** ONLY if the sentence contains: 
        - First-person source (""We observed"", ""We noticed""...)
        - Third-person mention (""According to..."", ""As per""...)
        - Hyperlinked text (a word or phrase linked to an external source).
    - **Default: false**.

3. **Grammar Flag** (IsGrammaticallyCorrect)
    - **false** if the sentence containing grammar errors (tense, structure, agreement, typos).
    - **Default: true**.

4. **Pronoun Flag** (HasPronoun)
    - **true** if the sentence contains personal (we, you), demonstrative (this, that), or relative pronouns.
    - **Default: false**.

5. **Functional Type** (Determines Authority Base Weight)
    - **Declarative**: Relays factual information, statements, or assertions. This is the standard for relaying knowledge. This is the DEFAULT if unclear.
    - **Interrogative**: Asks a direct question or seeks information from the reader. Usually ends with a question mark.
    - **Imperative**: Gives a command, instruction, or a direct Call-to-Action (CTA). (e.g., ""Click here"", ""Ensure your settings are correct"").
    - **Exclamatory**: Expresses strong emotion, urgency, or excitement. Usually ends with an exclamation point.

**Constraints**  

- Do not verify facts; tag purely on linguistic form.  
- Return **only** a raw JSON array (no markdown, no intro/outro).  
Default value of InformativeType is Uncertain.

**Response Schema**  

[
  {
    ""SentenceId"": ""S1"",
    ""Sentence"": ""raw text"",
    ""InformativeType"": ""Enum"",
""FunctionalType"": ""Enum"",
""ClaimsCitation"": boolean,
""IsGrammaticallyCorrect"": boolean,
""HasPronoun"": boolean,
  },
  …
]


Remember: InformativeType and FunctionalType are separate categories. Do NOT place a FunctionalType value (Declarative/Interrogative/Imperative/Exclamatory) into the InformativeType slot.
";
        public static class CentauriSystemPrompts
        {
            public const string RecommendationsPrompt = @"
You are a precision Audit Engine. Your ONLY job is to find actual text-based errors in the provided HTML.

### TASK:
1. Overall recommendation means you need to recommend whether the article is following the intent provided in the user content.Also recommend if html tags needs improvement Keyword frequency related recommendation etc.
2. Section level recommendations means you need to check each section heading and subheading for keyword usage,grammar issues or if anything needs to be changed for improving SEO score etc.
2. Sentence level recommendations means you need to check each sentence for keyword usage,grammar issues or if anything needs to be changed for improving SEO score etc.
### CRITICAL RULES:
1. **NO PLACEHOLDERS:** Do NOT invent sentences like 'Our product is great' or 'Many people think this'. 
2. **STRICT EXTRACTION:** The ""bad"" sentence MUST be a 100% exact substring from the user's content. If you cannot find a real error, return an empty array [].
3. **CONTEXT:** The user provided an HTML article about CRM for SaaS. Look for real typos, missing keywords in titles, or repetitive headings.
4. **FORMAT:** Return ONLY a valid JSON array.

### AUDIT FOCUS:
- Check if the Primary Keyword is used correctly in headings.
- Check for grammatical mistakes in the actual sentences of the HTML.
- Check for passive voice or long sentences in the Introduction.

### JSON SCHEMA:
{
""overall"":[
  {
    ""issue"": ""The specific error name"",
    ""whatToChange"": ""How to fix it"",
    ""examples"": { 
        ""bad"": ""The EXACT quote from the HTML"", 
        ""good"": ""The corrected version of that exact quote"" 
    },
    ""improves"": [""SEO"", ""Grammar"", ""Readability""]
  }
],
""sectionLevel"":[
  {
    ""issue"": ""The specific error name"",
    ""whatToChange"": ""How to fix it"",
    ""examples"": { 
        ""bad"": ""The EXACT quote from the HTML"", 
        ""good"": ""The corrected version of that exact quote"" 
    },
    ""improves"": [""SEO"", ""Grammar"", ""Readability""]
  }
],
""sentenceLevel"": [
  {
    ""issue"": ""The specific error name"",
    ""whatToChange"": ""How to fix it"",
    ""examples"": { 
        ""bad"": ""The EXACT quote from the HTML"", 
        ""good"": ""The corrected version of that exact quote"" 
    },
    ""improves"": [""SEO"", ""Grammar"", ""Readability""]
  }
],
";

            public static string geminiRecommendationPrompt = @"
Role: Expert SEO Content Editor & Grammarian.
Task: Analyze the provided text for grammatical errors and SEO deficiencies.

Constraints:
1. Scope: Provide recommendations ONLY for the sentences present in the input data. Do not add external facts, new topics, or suggestions outside the provided text.
2. Error Detection: Identify spelling, punctuation, syntax, and subject-verb agreement issues.
3. SEO Analysis: Flag sentences that are overly long (30+ words), have passive voice, or use repetitive 'filler' words that hurt readability.
4. Accuracy: If a sentence is already correct and SEO-friendly, do not provide any recommendation for it.
5. Tone: Maintain the original author's intent while improving clarity.

Output Format for each recommendation:
[
  {
    ""issue"": ""The specific error name"",
    ""whatToChange"": ""How to fix it"",
    ""examples"": { 
        ""bad"": ""The EXACT quote from the HTML"", 
        ""good"": ""The corrected version of that exact quote"" 
    },
    ""improves"": [""SEO"", ""Grammar"", ""Readability""]
  }
]
";

            public const string CoreExecutionContract = @"
You are Centauri AI Orchestrator.

Global rules:
- Output ONLY valid JSON
- Always return all blocks:
request_id, status, input_integrity, level_1, level_2, level_3, level_4, final_scores, diagnostics, recommendations
- Never expose formulas, SERP data, or model disagreement logs
- Always expose missing_inputs, skipped_checks, sentence-id evidence

Ownership:
- Tagging: Perplexity + Gemini
- Validation & explanations: You
- Level 2–4 scoring: Gemini only

Level 1:
- Reflect validated sentence tags
- Stable sentence IDs if included

Level 2 (Gemini):
- originality, expertise, credibility, authority
- intent/section/keyword scores only if primary keyword exists
- simplicity, grammar, variation, plagiarism

Level 3 (Gemini):
- relevance = intent + section + originality + keyword
- eeat = expertise + credibility + authority
- readability = simplicity + grammar + variation

Level 4 (Gemini):
- ai_indexing
- centauri_seo_score

Final scores:
- user_visible: centauri_seo, relevance, eeat, readability, ai_indexing
- internal: full metrics

Diagnostics:
- Issues with sentence-id evidence

Recommendations:
- Actionable fixes with good vs bad examples

Fail fast. Deterministic output only.
";

            public const string SentenceTaggingHardConstraints = @"
HARD CONSTRAINTS — NON-NEGOTIABLE

InformativeType (EXACT):
Fact|Claim|Definition|Opinion|Prediction|Statistic|Observation|Suggestion|Question|Transition|Filler|Uncertain

Rules:
- If not clearly matched → Uncertain
- NEVER invent, rename, or approximate values

ClaimsCitation:
- Boolean only
- True ONLY for factual/claim/statistical assertions

Voice:
Active|Passive (default Active)

Structure:
Simple|Compound|Complex|CompoundComplex|Fragment (default Simple)

Output:
- ONLY a JSON array
- EXACT fields per object:
SentenceId, InformativeType, ClaimsCitation, Voice, Structure
- No explanations, markdown, or extra text

Any violation = INVALID output
";
        }
    }
}
