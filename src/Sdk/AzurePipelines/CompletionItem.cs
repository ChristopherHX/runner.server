using System.Reflection.Emit;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Runner.Server.Azure.Devops {

    [JsonObject("", NamingStrategyType = typeof(CamelCaseNamingStrategy), ItemNullValueHandling = NullValueHandling.Ignore)]
    public class CompletionItemLabel {
        public string Label { get; set; }
        public string Description { get; set; }
        public string Detail { get; set; }
    }
    [JsonObject("", NamingStrategyType = typeof(CamelCaseNamingStrategy), ItemNullValueHandling = NullValueHandling.Ignore)]
    public class MarkdownString {
        public string Value { get; set; }
        public bool? SupportThemeIcons { get; set; }
        public bool? SupportHtml { get; set; }
    }

    [JsonObject("", NamingStrategyType = typeof(CamelCaseNamingStrategy), ItemNullValueHandling = NullValueHandling.Ignore)]
    public class SnippedString {
        public string Value { get; set; }
    }
    
    [JsonObject("", NamingStrategyType = typeof(CamelCaseNamingStrategy), ItemNullValueHandling = NullValueHandling.Ignore)]
    public class Position {
        long Character { get; set; }
        long Line { get; set; }
    }

    [JsonObject("", NamingStrategyType = typeof(CamelCaseNamingStrategy), ItemNullValueHandling = NullValueHandling.Ignore)]
    public class Range {
        Position Start { get; set; }
        Position End { get; set; }
        bool? IsEmpty { get; set; }
        bool? IsSingleLine { get; set; }
    }
    
    
    [JsonObject("", NamingStrategyType = typeof(CamelCaseNamingStrategy), ItemNullValueHandling = NullValueHandling.Ignore)]
    public class CompletionItem {
        public CompletionItemLabel Label { get; set; }
        public string FilterText { get; set; }
        public SnippedString InsertText { get; set; }
        public string SortText { get; set; }
        public bool? Preselect { get; set; }
        public string Detail { get; set; }
        public string[] CommitCharacters { get; set; }
        public bool? KeepWhitespace { get; set; }
        public int? Kind { get; set; }
        public Range Range { get; set; }
        public MarkdownString Documentation { get; set; }

        [JsonIgnore]
        public int Priority { get; set; } = 0;
    }
}