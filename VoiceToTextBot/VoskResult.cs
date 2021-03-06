using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VoiceToTextBot
{
    public class VoskResult
    {
        [JsonPropertyName("result")]
        public IEnumerable<VoskResultWord> Result { get; set; }
        [JsonPropertyName("text")]
        public string Text { get; set; }
    }

    public class VoskResultWord
    {
        [JsonPropertyName("conf")]
        public double Confidence { get; set; }
        [JsonPropertyName("start")]
        public double StartTime { get; set; }
        [JsonPropertyName("end")]
        public double EndTime { get; set; }
        [JsonPropertyName("word")]
        public string Word { get; set; }
    }
}