namespace VoiceToTextBot
{
    public interface IConfiguration
    {
        string VkGroupToken { get; }
        ulong VkGroupId { get; }
        string TgBotToken { get; }
    }
}