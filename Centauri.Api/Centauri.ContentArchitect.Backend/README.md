# Centauri ContentArchitect Backend

.NET 8 Web API implementing the metric architecture and formulas from **Centauri Phase 2**.

## Implemented

1. Total Search Volume
   - Primary search volume
   - Secondary keyword clusters
   - Addressable Search Demand = sum of canonical cluster volumes

2. Keyword Difficulty
   - Authority Pressure = mean normalized domain strength of Top 10
   - Link Pressure = median of capped log-normalized referring-domain scores
   - Intent Saturation = exact-intent Top 10 / 10 × 100
   - Competitor Content Strength = mean content coverage
   - KD = 0.35 AP + 0.35 LP + 0.20 IS + 0.10 CCS

3. Indexability Readiness
   - Historical Index Rate
   - Crawl Health
   - Canonical Consistency
   - Sitemap Health
   - Internal Discovery
   - Domain Strength
   - Readiness = 0.40 HIR + 0.20 CH + 0.15 CAN + 0.10 SH + 0.10 ID + 0.05 DS

4. Traffic Potential
   - Σ(SVc × CTRr × SCc × IP)
   - Conservative position 12
   - Expected position 8
   - Strong position 5
   - SERP clickability values from the specification are configurable in appsettings.

5. Questions Answered
   - Question universe from PAA + competitor questions + question keywords
   - Weight(r) = 1 / log2(r + 1)
   - Rank-weighted question coverage
   - Core >= 0.60; Common 0.30–0.59; Potential Gap < 0.30

6. Additional Questions / Content Gaps
   - CG = 1 - QuestionCoverage
   - Gap = 100 × [0.35 IR + 0.25 CG + 0.20 DP + 0.20 UN]
   - Strong 75–100; Useful 50–74; Optional 25–49; Probably unnecessary 0–24

7. EEAT / Information Gain
   - Evidence Requirement:
     0.25 FER + 0.20 ODP + 0.15 SRC + 0.15 AUTH + 0.15 YMYL + 0.10 FRESH
   - Information Gain:
     0.40 Redundancy + 0.25 MissingQuestion + 0.20 MissingEntity + 0.15 MissingEvidence
   - Composite:
     0.60 Evidence Requirement + 0.40 Information Gain

## External integrations

- DataForSEO: keyword ideas/volume/CPC, SERP, and backlink data
- Google Search Console URL Inspection: index/crawl/canonical
- Gemini: intent, content coverage, question normalization, semantic similarity, evidence analysis
- Direct HTML parsing
- Sitemap/robots discovery

Put credentials in `appsettings.json` or, preferably, environment variables / user secrets.

## Run

```bash
dotnet restore
dotnet build
dotnet run
```

POST to:

`POST /api/content-architect/analyze`

Example body:

```json
{
  "primaryKeyword": "best crm software",
  "countryOrRegion": "United States",
  "language": "en",
  "websiteUrl": "https://example.com",
  "competitors": [],
  "userMaterials": []
}
```

`POST /api/content-architect/generate-outline` uses the cached result from the matching
`/analyze` request, so the client does not send the large analysis response again:

```json
{
  "primaryKeyword": "best crm software",
  "countryOrRegion": "United States",
  "language": "en",
  "websiteUrl": "https://example.com",
  "selectedCompetitorQuestions": ["Which CRM features matter most?"],
  "selectedAdditionalQuestions": ["How much does CRM software cost?"]
}
```

Analysis results are held in the in-memory cache for 30 minutes by default
(`Analysis:CacheDurationMinutes`). Re-run `/analyze` when the cache expires or when
the application restarts.

## Important implementation notes

The specification distinguishes a defensible MVP readiness score from a true probability. This backend therefore returns Metric 3 as **Indexability Readiness Score**, not a statistical probability.

A trained 14-day probability model is deliberately not fabricated. The persistence/training inputs specified by the document (publish date, indexed-within-7-days, indexed-within-14-days, site index rate, internal links, crawl health, domain strength, content type, content uniqueness, sitemap presence) should be stored when historical data is available, then a calibrated logistic/sigmoid model can replace the readiness score.

The document also recommends stratified sitemap sampling. The current MVP uses a deterministic sample because a persistent crawl/index model is not specified. The sampling limits are configurable.

## Security

Do not commit real API credentials. Use:
- `dotnet user-secrets`
- environment variables
- Azure Key Vault / AWS Secrets Manager in production
