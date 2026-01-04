using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CentauriSeo.Core.Models.Input;

public class SeoRequest
{
    public ArticleInput? Article { get; set; }
    public string? PrimaryKeyword { get; set; }
    public List<string>? SecondaryKeywords { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public string? Url { get; set; }
    public ContextInput? Context { get; set; }
}

