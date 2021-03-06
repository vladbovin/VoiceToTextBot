using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Telegram.Bot;
using Telegram.Bot.Args;
using Vosk;
using Xabe.FFmpeg;

namespace VoiceToTextBot
{
    class Program
    {
        private static ITelegramBotClient _botClient;
        private static VoskRecognizer _voskRecognizer;

        static void Main()
        {
            Vosk.Vosk.SetLogLevel(0);
            var model = new Model("model");
            _voskRecognizer = new VoskRecognizer(model, 16000.0f);

            _botClient = new TelegramBotClient("1656931193:AAEIAy6ZMKKWFrQ5SRETe2hWi9EBTM8ucSE");

            var me = _botClient.GetMeAsync().Result;
            Console.WriteLine(
                $"Hello, World! I am user {me.Id} and my name is {me.FirstName}."
            );

            _botClient.OnMessage += TgBot_OnMessage;
            _botClient.StartReceiving();

            Console.WriteLine("Press any key to exit");
            Console.ReadKey();

            _botClient.StopReceiving();
        }

        static async void TgBot_OnMessage(object sender, MessageEventArgs e)
        {
            if (e.Message.Voice != null)
            {
                var message = await _botClient.SendTextMessageAsync(
                    chatId: e.Message.Chat,
                    text: "Распознаю речь..."
                );
                
                var oggAudioFilePath = $"{e.Message.Voice.FileId}.ogg";
                var oggAudioStream = new FileStream(oggAudioFilePath, FileMode.Create);
                await _botClient.GetInfoAndDownloadFileAsync(e.Message.Voice.FileId, oggAudioStream);
                oggAudioStream.Close();

                var resultTextMessage = await RecognizeText(oggAudioFilePath);
                File.Delete(oggAudioFilePath);
                
                await _botClient.EditMessageTextAsync(
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