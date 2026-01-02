using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Infrastructure.LlmDtos
{
    public class ChatGptResponse
    {
        public string Id { get; set; }
        public string Model { get; set; }
        public List<Choice> Choices { get; set; }
    }

    public class Choice
    {
        public int Index { get; set; }
        public Message Message { get; set; }
    }
    public class Message
    {
        public string Content { get; set; }
    }
}
