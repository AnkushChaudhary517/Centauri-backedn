using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Sentences;

public record Sentence(
    string Id,
    string Text,
    string ParagraphIndex
);
