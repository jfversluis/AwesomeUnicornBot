using Microsoft.Bot.Connector;
using Microsoft.ProjectOxford.Emotion;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;

namespace AwesomeUnicornBot
{
    [BotAuthentication]
    public class MessagesController : ApiController
    {
        /// <summary>
        /// POST: api/Messages
        /// Receive a message from a user and reply to it
        /// </summary>
        public async Task<HttpResponseMessage> Post([FromBody]Activity activity)
        {
            if (activity.Type == ActivityTypes.Message)
            {
                var connector = new ConnectorClient(new Uri(activity.ServiceUrl));

                var typingActivity = activity.CreateReply();
                typingActivity.Type = ActivityTypes.Typing;

                await connector.Conversations.ReplyToActivityAsync(typingActivity);

                if (activity.Attachments?.Count() > 0)
                {
                    var imageCount = activity.Attachments.Count(a => a.ContentType.Contains("image"));

                    if (imageCount > 0)
                    {
                        // Notify user that we got something
                        var message = imageCount == 1 ? $"You've sent 1 image" : $"You've sent {imageCount} images";
                        var imageReply = activity.CreateReply(message);
                        await connector.Conversations.ReplyToActivityAsync(imageReply);
                    }

                    foreach (var attachment in activity.Attachments.Where(a => a.ContentType.Contains("image")).Take(1))
                    {
                        byte[] imageBytes = null;

                        // For Skype we need to get an OAuth token to access the content url
                        if (activity.ChannelId == "skype")
                        {
                            using (var httpClient = new HttpClient())
                            {
                                // Request OAuth token
                                var formValues = new KeyValuePair<string, string>[]
                                {
                                    new KeyValuePair<string, string>("client_id", ConfigurationManager.AppSettings["MicrosoftAppId"]),
                                    new KeyValuePair<string, string>("client_secret", ConfigurationManager.AppSettings["MicrosoftAppPassword"]),
                                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                                    new KeyValuePair<string, string>("scope", "https://graph.microsoft.com/.default")
                                };

                                // Anonymous definition for return object
                                var definition = new { access_token = "" };

                                var tokenJson = await httpClient.PostAsync("https://login.microsoftonline.com/common/oauth2/v2.0/token",
                                    new FormUrlEncodedContent(formValues));

                                var token = JsonConvert.DeserializeAnonymousType(await tokenJson.Content.ReadAsStringAsync(), definition);

                                // Set Bearer: <token> header for the request
                                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.access_token);
                                imageBytes = await httpClient.GetByteArrayAsync(attachment.ContentUrl);
                            }
                        }
                        else
                            imageBytes = ConvertContentToByteArray(attachment.ContentUrl);

#error TODO: Add your Emotion API key here
                        var emotionClient = new EmotionServiceClient("b436xxxxxxxxxxxxxxxcb30");
                        var emotionResults = await emotionClient.RecognizeAsync(new MemoryStream(imageBytes));

                        var aggregatedResults = new Dictionary<string, float>();
                        foreach (var e in emotionResults)
                        {
                            foreach (var s in e.Scores.ToRankedList())
                            {
                                if (!aggregatedResults.ContainsKey(s.Key))
                                    aggregatedResults.Add(s.Key, s.Value);
                                else
                                    aggregatedResults[s.Key] += s.Value;
                            }
                        }

                        var emotionsMessage = $"I recognize {emotionResults.Count()} people, in general their emotion seems to be: {aggregatedResults.OrderByDescending(r => r.Value).First().Key}";
                        var emotionsReply = activity.CreateReply(emotionsMessage);
                        await connector.Conversations.ReplyToActivityAsync(emotionsReply);
                    }
                }
            }
            else
            {
                HandleSystemMessage(activity);
            }
            var response = Request.CreateResponse(HttpStatusCode.OK);
            return response;
        }

        private Activity HandleSystemMessage(Activity message)
        {
            if (message.Type == ActivityTypes.DeleteUserData)
            {
                // Implement user deletion here
                // If we handle user deletion, return a real message
            }
            else if (message.Type == ActivityTypes.ConversationUpdate)
            {
                // Handle conversation state changes, like members being added and removed
                // Use Activity.MembersAdded and Activity.MembersRemoved and Activity.Action for info
                // Not available in all channels
            }
            else if (message.Type == ActivityTypes.ContactRelationUpdate)
            {
                // Handle add/remove from contact lists
                // Activity.From + Activity.Action represent what happened
            }
            else if (message.Type == ActivityTypes.Typing)
            {
                // Handle knowing tha the user is typing
            }
            else if (message.Type == ActivityTypes.Ping)
            {
            }

            return null;
        }

        /// <summary>
        /// This method takes an image url and converts this to a byte array
        /// </summary>
        /// <param name="contenturl">url of where the bot places a user uploaded image</param>
        /// <returns>byte array containing the image data</returns>
        private byte[] ConvertContentToByteArray(string contenturl)
        {
            var imageBytes = new byte[500000];
            var imageRequest = (HttpWebRequest)WebRequest.Create(contenturl);
            var imageResponse = imageRequest.GetResponse();

            using (var responseStream = imageResponse.GetResponseStream())
            {
                using (var ms = new MemoryStream())
                {
                    int read;
                    while ((read = responseStream.Read(imageBytes, 0, imageBytes.Length)) > 0)
                        ms.Write(imageBytes, 0, read);

                    imageBytes = ms.ToArray();
                }

                imageResponse.Close();
                responseStream.Close();
            }

            return imageBytes;
        }
    }
}