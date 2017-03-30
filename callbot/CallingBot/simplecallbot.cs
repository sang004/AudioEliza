﻿using Microsoft.Bot.Builder.Calling;
using Microsoft.Bot.Builder.Calling.Events;
using Microsoft.Bot.Builder.Calling.ObjectModel.Contracts;
using Microsoft.Bot.Builder.Calling.ObjectModel.Misc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using SSS = System.Speech.Synthesis;

namespace callbot
{
    public class simplecallbot : ICallingBot
    {
        public ICallingBotService CallingBotService
        {
            get; private set;
        }

        private List<string> response = new List<string>();
        int silenceTimes = 0;
        bool sttFailed = false;
        static ConversationTranscibe logger = new ConversationTranscibe(); // Will create a fresh new log file


        public simplecallbot(ICallingBotService callingBotService)
        {

            if (callingBotService == null)
                throw new ArgumentNullException(nameof(callingBotService));

            this.CallingBotService = callingBotService;

            CallingBotService.OnIncomingCallReceived += OnIncomingCallReceived;
            CallingBotService.OnPlayPromptCompleted += OnPlayPromptCompleted;
            CallingBotService.OnRecordCompleted += OnRecordCompleted;
            CallingBotService.OnHangupCompleted += OnHangupCompleted;
        }

        private Task OnIncomingCallReceived(IncomingCallEvent incomingCallEvent)
        {
            var id = Guid.NewGuid().ToString();
            incomingCallEvent.ResultingWorkflow.Actions = new List<ActionBase>
                {
                    new Answer { OperationId = id },
                    GetRecordForText("Hi hi!")
                };

            return Task.FromResult(true);
        }

        private ActionBase GetRecordForText(string promptText)
        {
            PlayPrompt prompt;
            if (string.IsNullOrEmpty(promptText))
                prompt = null;
            else
                prompt = GetPromptForText(promptText);
            //prompt = PlayAudioFile(promptText);
            var id = Guid.NewGuid().ToString();

            return new Record()
            {
                OperationId = id,
                PlayPrompt = prompt,
                MaxDurationInSeconds = 10,
                InitialSilenceTimeoutInSeconds = 5,
                MaxSilenceTimeoutInSeconds = 2,
                PlayBeep = false,
                RecordingFormat = RecordingFormat.Wav,
                StopTones = new List<char> { '#' }
            };
        }

        private Task OnPlayPromptCompleted(PlayPromptOutcomeEvent playPromptOutcomeEvent)
        {
            // get response from LUIS in text form
            if (response.Count > 0)
            {
                silenceTimes = 0;
                var actionList = new List<ActionBase>();
                foreach (var res in response)
                {
                    logger.WriteToText("USER: ", res);

                    Debug.WriteLine($"Response ----- {res}");

                    // start playback of response
                    ///TEST
                    string user = "user";
                    string private_key = "a8b9e532120b6b5ce491d4b4a102266740d285ca32c76b6ec2b5dd1158177d25";

                    RSAPI test2 = new RSAPI(user, private_key);
                    string replyAudioPath = test2.Call(res).Result;
                    actionList.Add(PlayAudioFile(replyAudioPath));

                }

                //actionList.Add(GetPromptForText(response));
                actionList.Add(GetRecordForText(string.Empty));
                playPromptOutcomeEvent.ResultingWorkflow.Actions = actionList;
                response.Clear();
            }
            else
            {
                if (sttFailed)
                {
                    playPromptOutcomeEvent.ResultingWorkflow.Actions = new List<ActionBase>
                    {
                        GetRecordForText("I didn't catch that, would you kindly repeat?")
                    };
                    sttFailed = false;
                    silenceTimes = 0;
                }
                else if (silenceTimes > 2)
                {
                    playPromptOutcomeEvent.ResultingWorkflow.Actions = new List<ActionBase>
                    {
                        GetPromptForText("Something went wrong. Call again later."),
                        new Hangup() { OperationId = Guid.NewGuid().ToString() }
                    };
                    playPromptOutcomeEvent.ResultingWorkflow.Links = null;
                    silenceTimes = 0;
                }
                else
                {
                    silenceTimes++;
                    playPromptOutcomeEvent.ResultingWorkflow.Actions = new List<ActionBase>
                    {
                        GetSilencePrompt(2000)
                    };
                }
            }
            return Task.CompletedTask;
        }

        private async Task OnRecordCompleted(RecordOutcomeEvent recordOutcomeEvent)
        {
            // When recording is done, send to BingSpeech to process
            if (recordOutcomeEvent.RecordOutcome.Outcome == Outcome.Success)
            {
                //TEST AUDIO input
                ///Retrieve random audio
                string user = "user";
                string private_key = "a8b9e532120b6b5ce491d4b4a102266740d285ca32c76b6ec2b5dd1158177d25";

                RSAPI test2 = new RSAPI(user, private_key);
                string replyAudioPath = test2.Call("sample").Result;

                var webClient = new WebClient();
                byte[] bytes = webClient.DownloadData(replyAudioPath);

                //byte[] bytes = System.IO.File.ReadAllBytes("C:\\Users\\user\\Downloads\\BOT\\morning.wav");
                System.IO.Stream streams = new System.IO.MemoryStream(bytes);

                var record = streams;//await recordOutcomeEvent.RecordedContent;//streams;//
                BingSpeech bs = new BingSpeech(recordOutcomeEvent.ConversationResult, t => response.Add(t), s => sttFailed = s);
                bs.CreateDataRecoClient();
                bs.SendAudioHelper(record);

                //AskLUIS test = new AskLUIS();
                //String response = test.questionLUIS(activity.Text);
                recordOutcomeEvent.ResultingWorkflow.Actions = new List<ActionBase>
                {
                    GetSilencePrompt()
                };

            }
            else
            {
                if (silenceTimes > 1)
                {
                    recordOutcomeEvent.ResultingWorkflow.Actions = new List<ActionBase>
                    {
                        GetPromptForText("Bye bye!"),
                        new Hangup() { OperationId = Guid.NewGuid().ToString() }
                    };
                    recordOutcomeEvent.ResultingWorkflow.Links = null;
                    silenceTimes = 0;
                }
                else
                {
                    silenceTimes++;
                    recordOutcomeEvent.ResultingWorkflow.Actions = new List<ActionBase>
                    {
                        GetRecordForText("I didn't catch that, would you kindly repeat?")
                    };
                }
            }


        }

        private Task OnHangupCompleted(HangupOutcomeEvent hangupOutcomeEvent)
        {
            logger.uploadToRS();
            hangupOutcomeEvent.ResultingWorkflow = null;
            return Task.FromResult(true);
        }

        // TEST playback
        private static PlayPrompt PlayAudioFile(string audioPath)
        {
            //System.Uri uri = new System.Uri("https://callbotstorage.blob.core.windows.net/blobtest/graham_any_nation.wav");
            System.Uri uri = new System.Uri(audioPath);

            var prompt = new Prompt { FileUri = uri };
            return new PlayPrompt { OperationId = Guid.NewGuid().ToString(), Prompts = new List<Prompt> { prompt } };
        }

        private static PlayPrompt GetPromptForText(string text)
        {
            logger.WriteToText("BOT: ", text);

            var prompt = new Prompt { Value = text, Voice = VoiceGender.Female };
            return new PlayPrompt { OperationId = Guid.NewGuid().ToString(), Prompts = new List<Prompt> { prompt } };
        }

        private static PlayPrompt GetPromptForText(List<string> text)
        {
            var prompts = new List<Prompt>();
            foreach (var txt in text)
            {
                logger.WriteToText("BOT: ", txt);

                if (!string.IsNullOrEmpty(txt))
                    prompts.Add(new Prompt { Value = txt, Voice = VoiceGender.Female });
            }
            if (prompts.Count == 0)
                return GetSilencePrompt(1000);
            return new PlayPrompt { OperationId = Guid.NewGuid().ToString(), Prompts = prompts };
        }

        private static PlayPrompt GetSilencePrompt(uint silenceLengthInMilliseconds = 3000)
        {
            var prompt = new Prompt { Value = string.Empty, Voice = VoiceGender.Female, SilenceLengthInMilliseconds = silenceLengthInMilliseconds };
            return new PlayPrompt { OperationId = Guid.NewGuid().ToString(), Prompts = new List<Prompt> { prompt } };
        }
    }


}

