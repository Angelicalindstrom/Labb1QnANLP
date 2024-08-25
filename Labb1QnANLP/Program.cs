using Azure;
using Azure.AI.Language.QuestionAnswering;
using Azure.AI.Translation.Text;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Web;

namespace QnAApp
{
    // Angelica Lindström .NET23 - Labb 1 -  Natural Language Processing och frågetjänster i Azure AI

    // applikation med NLP och QnA
    // tar emot en fråga, om engelska svarar direkt med QnA chatbot.
    // Om inte engelska, tar reda på språket och översätter till engelska
    // sedan skickas översättningen till chatbot QnA och letar svar och svarar.
    internal class Program
    {
        // AI services
        private static string cogSvcEndpoint;
        private static string cogSvcKey;
        private static string cogSvcRegion;

        // QnA Azure
        private static string qnaEndpoint;
        private static string qnaKey;
        private static string qnaRegion;
        private static readonly string projectName = "QnALanguagedogs";
        private static readonly string deploymentName = "production";

        // Translate Azure
        private static string translateTextEndpoint;
        private static string translateTextKey;

        static async Task Main(string[] args)
        {
            // Ladda configuration appsettings.json
            IConfigurationBuilder builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            IConfigurationRoot configuration = builder.Build();

            // Set console encoding till Unicode
            Console.InputEncoding = Encoding.Unicode;
            Console.OutputEncoding = Encoding.Unicode;

            // Hämta keys och endpoints från configuration
            cogSvcEndpoint = configuration["CognitiveServicesEndpoint"];
            cogSvcKey = configuration["CognitiveServiceKey"];
            cogSvcRegion = configuration["CognitiveServiceRegion"];

            qnaEndpoint = configuration["LanguageServiceEndpoint"];
            qnaKey = "1746f330354f441da379a5e17b460ba0";
            qnaRegion = configuration["LanguageServiceRegion"];

            translateTextEndpoint = configuration["TranslateTextServiceEndpoint"];
            translateTextKey = configuration["TranslateTextServiceKey"];



            //Translator client
            TextTranslationClient translationClient = new TextTranslationClient(
                new AzureKeyCredential(translateTextKey), new Uri(translateTextEndpoint));

            //BOT
            Uri cogSvcEndpointUri = new Uri(qnaEndpoint);
            AzureKeyCredential cogSvcCredential = new AzureKeyCredential(qnaKey);

            //BotClient
            QuestionAnsweringClient client = new QuestionAnsweringClient(cogSvcEndpointUri, cogSvcCredential);
            QuestionAnsweringProject project = new QuestionAnsweringProject(projectName, deploymentName);

            Console.WriteLine("Ask Anything about dogs in any language or just Chat with Nutri.\n");
            Console.WriteLine("You're chatting with Nutri! (type 'exit' to quit)");

            while (true)
            {
                Console.Write("Me: ");
                string userInput = Console.ReadLine();


                if (userInput.ToLower() == "exit")
                {
                    break;
                }

                // kolla vilket språk som används.
                string detectedLanguage = await DetectLanguage(userInput);

                //OM ENGELSKA
                if (detectedLanguage == "en")
                {
                    try
                    {
                        //hämtar svar och skickar ut ifrån QnA direkt
                        Response<AnswersResult> response = client.GetAnswers(userInput, project);
                        foreach (KnowledgeBaseAnswer answer in response.Value.Answers)
                        {
                            Console.WriteLine($"Nutri: {answer.Answer}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Request error english: {ex.Message}");
                    }
                }

                else // ANNARS, (INTE ENGELSKA)
                     // Översätt  Vad behövs för att överästta, DetectedLanguage -> en . user input? sedan in i boten igen för att fråga frågan och sen få ut svaret
                {
                    try
                    {
                        // översätter userInput till en via TranlatorAzyncText metoden sparar strängen i string
                        string translatedQuestion = await TranslateAzyncText(translationClient, userInput);

                        Console.WriteLine($"(Translating userinput: {userInput} ,from  {detectedLanguage} to en : {translatedQuestion})");
                        // Hämtar svar via QnA
                        Response<AnswersResult> response = await client.GetAnswersAsync(translatedQuestion, project);

                        foreach (KnowledgeBaseAnswer answer in response.Value.Answers)
                        {
                            // sparar botens svar ifårn QnA i string och skriver ut.
                            string botAnswer = answer.Answer;
                            Console.WriteLine($"Nutri : {botAnswer}");
                        }
                    }
                    //undantag
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Request error other language: {ex.Message}");
                    }
                }
            }
        }

        // Metodd för översättning till engelska
        static async Task<string> TranslateAzyncText(TextTranslationClient translatorClient, string text)
        {
            try
            {
                string targetLanguage = "en";
                var response = await translatorClient.TranslateAsync(targetLanguage, new List<string> { text }).ConfigureAwait(false);
                //Returnerar översatt text
                return response.Value[0].Translations[0].Text;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Translation error: {ex.Message}");
                return text; // returnerar originaltext ifall något fel
            }
        }


        static async Task<string> DetectLanguage(string text)
        {
            try
            {
                // Konstruera JSON-begäran
                JObject jsonBody = new JObject(
                    // Skapa en samling av dokument (vi använder bara ett, men fler kan läggas till)
                    new JProperty("documents",
                    new JArray(
                        new JObject(
                            // Varje dokument behöver ett unikt ID och lite text
                            new JProperty("id", 1),
                            new JProperty("text", text)
                    ))));

                // Koda som UTF-8
                UTF8Encoding utf8 = new UTF8Encoding(true, true);
                byte[] encodedBytes = utf8.GetBytes(jsonBody.ToString());

                // Skapa en HTTP-klient för att göra REST-anrop
                var client = new HttpClient();
                var queryString = HttpUtility.ParseQueryString(string.Empty);

                // Lägg till autentiseringsnyckeln i headern
                client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", cogSvcKey);

                // Använd slutpunkten för att komma åt Text Analytics språk-API
                var uri = cogSvcEndpoint + "text/analytics/v3.1/languages?" + queryString;

                // Skicka begäran och få svaret
                HttpResponseMessage response;
                using (var content = new ByteArrayContent(encodedBytes))
                {
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    response = await client.PostAsync(uri, content);
                }

                // Om anropet var framgångsrikt, få svaret
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    // Visa hela JSON-svaret (bara för att vi ska kunna se det)
                    string responseContent = await response.Content.ReadAsStringAsync();
                    JObject results = JObject.Parse(responseContent);


                    // istället för att skriva ut så returnerar vi språket.
                    if (results["documents"] is not JArray { Count: > 0 } jArray) return string.Empty;
                    var language = (JObject)jArray[0];
                    return (string)language["detectedLanguage"]?["iso6391Name"]!;

                }
                else
                {
                    // Något gick fel, skriv hela svaret
                    Console.WriteLine(response.ToString());
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                // Fel, skriv ut undantag
                Console.WriteLine(ex.Message);
                return string.Empty;
            }

        }

    }
}