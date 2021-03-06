using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Config.Net;
using Newtonsoft.Json.Linq;
using Telegram.Bot;
using Telegram.Bot.Args;
using VkNet;
using VkNet.Enums.SafetyEnums;
using VkNet.Model;
using VkNet.Model.Attachments;
using VkNet.Model.RequestParams;
using Vosk;
using Xabe.FFmpeg;

namespace VoiceToTextBot
{
    class Program
    {
        private static ITelegramBotClient _tgBotClient;
        private static VkApi _vkApi;
        private static VoskRecognizer _voskRecognizer;

        static void Main()
        {
            var config = new ConfigurationBuilder<IConfiguration>()
                .UseJsonFile("config.json")
                .Build();

            Vosk.Vosk.SetLogLevel(0);
            var model = new Model("model");
            _voskRecognizer = new VoskRecognizer(model, 16000.0f);

            _tgBotClient = new TelegramBotClient(config.TgBotToken);

            var me = _tgBotClient.GetMeAsync().Result;
            Console.WriteLine(
                $"Hello, World! I am user {me.Id} and my name is {me.FirstName}."
            );

            _tgBotClient.OnMessage += TgBot_OnMessage;
            _tgBotClient.StartReceiving();

            _vkApi = new VkApi();
            _vkApi.Authorize(new ApiAuthParams()
            {
                AccessToken = config.VkGroupToken
            });

            var longPollServer = _vkApi.Groups.GetLongPollServer(config.VkGroupId);
            var ts = longPollServer.Ts;

            new Task(async () =>
            {
                while (true)
                {
                    var poll = _vkApi.Groups.GetBotsLongPollHistory(new BotsLongPollHistoryParams()
                    {
                        Server = longPollServer.Server,
                        Ts = ts,
                        Key = longPollServer.Key,
                        Wait = 25
                    });

                    if (poll.Updates == null) continue;

                    ts = poll.Ts;
                    foreach (var update in poll.Updates.Where(gu => gu.Type == GroupUpdateType.MessageNew))
                    {
                        var message = update.MessageNew.Message;
                        var forwardedMessage = message.ForwardedMessages.FirstOrDefault();

                        if (forwardedMessage != null &&
                            forwardedMessage.Attachments.Any(a => a.Type == typeof(AudioMessage)))
                        {
                            var rnd = new Random();
                            var resultMessageId = _vkApi.Messages.Send(new MessagesSendParams()
                            {
                                RandomId = rnd.Next(),
                                PeerId = message.FromId,
                                Message = "Распознаю речь..."
                            });
                            var audioMessageAttachment =
                                forwardedMessage.Attachments.First(a => a.Type == typeof(AudioMessage));
                            var audioMessage = (AudioMessage) audioMessageAttachment.Instance;

                            var webClient = new WebClient();

                            var vkAudioFilePath = $"{Guid.NewGuid()}.ogg";
                            webClient.DownloadFile(audioMessage.LinkOgg, vkAudioFilePath);

                            var resultTextMessage = await RecognizeText(vkAudioFilePath);
                            File.Delete(vkAudioFilePath);

                            if (message.FromId != null)
                                _vkApi.Messages.Edit(new MessageEditParams
                                {
                                    Message = resultTextMessage,
                                    MessageId = resultMessageId,
                                    PeerId = (long) message.FromId
                                });
                        }
                    }
                }
            }).Start();

            Console.WriteLine("Press any key to exit");
            Console.ReadKey();

            _tgBotClient.StopReceiving();
        }

        static async void TgBot_OnMessage(object sender, MessageEventArgs e)
        {
            if (e.Message.Voice != null)
            {
                var message = await _tgBotClient.SendTextMessageAsync(
                    chatId: e.Message.Chat,
                    text: "Распознаю речь..."
                );

                var tgAudioFilePath = $"{Guid.NewGuid()}.ogg";
                var tgAudioStream = new FileStream(tgAudioFilePath, FileMode.Create);
                await _tgBotClient.GetInfoAndDownloadFileAsync(e.Message.Voice.FileId, tgAudioStream);
                tgAudioStream.Close();

                var resultTextMessage = await RecognizeText(tgAudioFilePath);
                File.Delete(tgAudioFilePath);

                await _tgBotClient.EditMessageTextAsync(
                    chatId: message.Chat.Id,
                    messageId: message.MessageId,
                    text: resultTextMessage
                );
            }
        }

        static async Task ConvertAudio(string sourcePath, string outputPath)
        {
            var mediaInfo = await FFmpeg.GetMediaInfo(sourcePath);

            IStream audioStream = mediaInfo.AudioStreams.FirstOrDefault()
                ?.SetCodec(AudioCodec.pcm_s16le)
                .SetChannels(1)
                .SetSampleRate(16000);

            await FFmpeg.Conversions.New()
                .AddStream(audioStream)
                .SetOutput(outputPath)
                .Start();
        }

        static async Task<string> RecognizeText(string audioPath)
        {
            var convertedAudioFilePath = $"{Guid.NewGuid()}.wav";
            await ConvertAudio(audioPath, convertedAudioFilePath);

            var convertedAudioStream = File.OpenRead(convertedAudioFilePath);
            var buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = await convertedAudioStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                _voskRecognizer.AcceptWaveform(buffer, bytesRead);
            }

            convertedAudioStream.Close();
            File.Delete(convertedAudioFilePath);
            var recognizeResult = _voskRecognizer.FinalResult();
            var voskResult = JsonSerializer.Deserialize<VoskResult>(recognizeResult);
            return voskResult != null ? voskResult.Text : string.Empty;
        }
    }
}