//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using Mscc.GenerativeAI;
//using Mscc.GenerativeAI.Types;


//namespace CentauriSeo.Infrastructure.Services
//{

//    public class GeminiCacheService
//    {
//        private readonly string _apiKey = "YOUR_GEMINI_API_KEY";
//        private readonly GoogleAI _googleAi;

//        public GeminiCacheService()
//        {
//            _googleAi = new GoogleAI(_apiKey);
//        }

//        public async Task<string> CreateCacheAsync(string systemPrompt)
//        {
//            // Model name must include version suffix (e.g., -001)
//            var model = _googleAi.GenerativeModel("gemini-1.5-flash-001");

//            var cacheRequest = new CachedContent()
//            {
//                Model = "models/gemini-1.5-flash-001",
//                DisplayName = "SEO_Audit_System_Instruction",
//                SystemInstruction = new Content(systemPrompt),
//                Ttl =TimeSpan.FromHours(1) // 1 hour expiry
//            };

//            // Cache create karne ke liye
//            var response = await _googleAi.CreateCachedContent(cacheRequest);

//            // Ye 'Name' (ID) aapko store karni hogi (e.g. cachedContents/abc12345)
//            return response.Name;
//        }

//        public async Task<string> RunAuditWithCache(string cacheName, string userContent)
//        {
//            // Use the model with the cache reference
//            var model = _googleAi.GenerativeModel("gemini-1.5-flash-001");

//            // Request bhejte waqt CachedContent ID ka use karein
//            var request = new GenerateContentRequest(userContent)
//            {
//                CachedContent = cacheName
//            };

//            var response = await model.GenerateContent(request);
//            return response.Text;
//        }
//    }
//}
