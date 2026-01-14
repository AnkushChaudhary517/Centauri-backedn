//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace CentauriSeo.Infrastructure.LlmClients
//{
//    public class GenerativeAiClient
//    {
//        public void GetData()
//        {
//            var genAi = new GenerativeAIClient();

//            // 1. Create cached content with system instructions / scoring logic
//            var cachedContent = await CachedContent.CreateAsync(
//                model: "models/gemini-1.5-pro-002",
//                systemInstruction: "Your long definitions and scoring methods go here...",
//                ttl: TimeSpan.FromHours(1)
//            );

//            // 2. Create model from cached content
//            var model = GenerativeModel.FromCachedContent(cachedContent);

//            // 3. Use cached context in generation
//            var response = await model.GenerateContentAsync(
//                "Apply the scoring to this user input: ..."
//            );

//        }
//        // Initialize client (API key via env var GOOGLE_API_KEY)

//    }
//}
