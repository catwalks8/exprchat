using HarfBuzzSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Net;
using Avalonia.Media.Imaging;
using System.IO;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace ExprChatAvalonia {
	public struct SamplerConfig {
		public int contextSize = 4096;
		public int outputLength = 64;
		public double temperature = 0.3;
		public double repetitionPenalty = 1.1;
		public double topP = 0.9;
		public int topK = 100;
		public double tnsigma = 0;

		public SamplerConfig() { }
	}

	public struct StoryInfo {
		public string userName = "User";
		public string charName = "AI";
		public string userDesc = "";
		public string charDesc = "";
		public string examples = "";
		public string scenario = "";

		public StoryInfo() { }
	}

	static class Chat {
		public static SamplerConfig config = new SamplerConfig();
		public static StoryInfo story = new StoryInfo();
		public static string memory = "";
		public static string context = "";
		public static string gbnf = "";
		public static string chatTask = "Your task is naturally continuing the conversation below between the characters.\n\n- Write the character speech and actions, separated by a vertical line.\n- The action description is optional, can be omitted if not important.\n- For a silent action, write `...` in place of the speech.\n- Dialogue always comes before the action description.\n- Don't use asterisks.";
		public static string exprTask = "";
		public static (int pre, int post) lastPos = (0, 0);

		public static string imagesDir = @"";
		public static string selectedFolder = "";
		public static Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>();
		public static string apiKey = "0000000000l";
		public static string model = "";
		public static bool useHorde = false;
		public enum reqStat {
			Idle, Waiting, Queued, Processing
		}
		public static reqStat rs = reqStat.Idle;
		public static int rt = 0;

		public static async Task<string> KoboldCppGen(string con = "", string mem = "", int len = 128, double temp = 0, string gbnf = "") {
			rs = reqStat.Waiting;
			HttpClient client = new HttpClient();
			var payload = new {
				max_context_length = config.contextSize,
				max_length = len,
				memory = mem,
				prompt = con,
				quiet = true,
				grammar = gbnf,
				stop_sequence = new string[] { "\r", "\n" },
				rep_pen = config.repetitionPenalty,
				rep_pen_range = 384,
				rep_pen_slope = 0.8,
				temperature = temp,
				tfs = 1,
				top_a = 0,
				top_k = config.topK,
				top_p = config.topP,
				typical = 1,
				nsigma = config.tnsigma
			};

			string jsonPayload = JsonConvert.SerializeObject(payload);

			StringContent httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

			HttpResponseMessage response = new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
			response = await client.PostAsync("http://localhost:5001/api/v1/generate", httpContent);

			response.EnsureSuccessStatusCode();

			string responseContent = await response.Content.ReadAsStringAsync();
			dynamic obj = JsonConvert.DeserializeObject(responseContent);
			rs = reqStat.Idle;

			return obj.results[0].text.ToString();
		}

        public static async Task<string> HordeGen(string prompt = "") {
			rs = reqStat.Waiting;
			HttpClient client = new HttpClient();
            var payload = new {
				prompt = prompt,
				@params = new {
					max_context_length = config.contextSize,
					max_length = config.outputLength,
					stop_sequence = new string[] { "\r", "\n" },
					rep_pen = config.repetitionPenalty,
					rep_pen_range = 384,
					rep_pen_slope = 0.8,
					temperature = config.temperature,
					tfs = 1,
					top_a = 0,
					top_k = config.topK,
					top_p = config.topP,
					typical = 1,
					nsigma = config.tnsigma
				},
				models = new string[] { model }
            };

            string jsonPayload = JsonConvert.SerializeObject(payload);
            StringContent httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://aihorde.net/api/v2/generate/text/async");
			request.Headers.Add("Client-Agent", "ExprChat:1");
			request.Headers.Add("apikey", apiKey);
			request.Content = httpContent;

            HttpResponseMessage response = await client.SendAsync(request);

            response.EnsureSuccessStatusCode();

            string responseContent = await response.Content.ReadAsStringAsync();
            dynamic obj = JsonConvert.DeserializeObject(responseContent);

			string jobID = obj.id;

			while (true) {
				Task.Delay(1000);

                HttpRequestMessage statusRequest = new HttpRequestMessage(HttpMethod.Get, $"https://aihorde.net/api/v2/generate/text/status/{jobID}");
                statusRequest.Headers.Add("Client-Agent", "ExprChat:1");

                response = await client.SendAsync(statusRequest);
                responseContent = await response.Content.ReadAsStringAsync();
                obj = JsonConvert.DeserializeObject(responseContent);

				if (obj.waiting == 1) {
					rs = reqStat.Queued;
					rt = obj.queue_position;
                } else if (obj.processing == 1) {
					rs = reqStat.Processing;
					rt = obj.wait_time;
				} else if (obj.finished == 1) {
					rs = reqStat.Idle;
					break;
				}
            }

            return obj.generations[0].text.ToString();
        }

        public static async Task GenerateResponse() {
			lastPos.pre = context.Length;
			string msg = "";
			if (useHorde) {
				msg = await HordeGen($"{memory}{context}");
			} else {
				msg = await KoboldCppGen(context, memory, config.outputLength, config.temperature);
			}
			msg = Regex.Match(msg, @"^[^\|]+(?:\|[^\|]+)?").Value;
			context += msg;
			lastPos.post = context.Length;
		}

		public static async Task RetryResponse() {
			if (context.Length > lastPos.post) {
				context = context.Substring(0, context.Length - (lastPos.post - lastPos.pre));
			} else if (context.Length > lastPos.pre) {
				context = context.Substring(0, lastPos.pre);
			}

			await GenerateResponse();
		}

		public static async Task<string> GenerateExpression() {
			if (icons.Count == 0) return "";
			string msg = Regex.Matches(context, $"{story.charName}: (.*)").Last().Groups[1].Value;
			return await KoboldCppGen($"{context}\n</conversation>\n\n\n{exprTask.Replace("[MESSAGE]", msg)}", useHorde ? "" : memory, 8, 0.0, gbnf);
		}

		public static async Task<string> GetLocalModel() {
			HttpClient client = new HttpClient();
            HttpResponseMessage response = new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
			try {
				response = await client.GetAsync("http://localhost:5001/api/v1/model");
			}
			catch (Exception ex) {
				return null;
			}
			if (!response.IsSuccessStatusCode) return null;

            string responseContent = await response.Content.ReadAsStringAsync();
            dynamic obj = JsonConvert.DeserializeObject(responseContent);

            return obj.result.ToString();
		}

		public static async Task<(string? name, int kudos)> GetHordeUser(string api) {
			HttpClient client = new HttpClient();
			HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://aihorde.net/api/v2/find_user");
            request.Headers.Add("Client-Agent", "ExprChat:0");
            request.Headers.Add("apikey", api);

            HttpResponseMessage response = new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            try {
                response = await client.SendAsync(request);
            }
            catch (Exception ex) {
                return (name: null, kudos: 0);
            }
            if (!response.IsSuccessStatusCode) return (name: null, kudos: 0);

            string responseContent = await response.Content.ReadAsStringAsync();
            dynamic obj = JsonConvert.DeserializeObject(responseContent);

			return (name: obj.username, kudos: obj.kudos);
		}

		public static async Task<List<string>> GetHordeModels() {
			List<string> models = new List<string>();

			HttpClient client = new HttpClient();
            HttpResponseMessage response = new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            try {
                response = await client.GetAsync("https://aihorde.net/api/v2/status/models?type=text&model_state=all");
            }
            catch (Exception ex) {
                return models;
            }
            if (!response.IsSuccessStatusCode) return models;

            string responseContent = await response.Content.ReadAsStringAsync();
            dynamic obj = JsonConvert.DeserializeObject(responseContent);

			for (int i = 0; i < obj?.Count; i++) {
				models.Add(obj[i].name.ToString());
			}
			
            return models;
        }

        public static void AppendUserMsg(string dialog, string action) {
			context += $"\n{story.userName}: ";
			if (dialog.Length > 0) context += dialog; else context += "...";
			if (action.Length > 0) context += $" | {action}";
			context += $"\n{story.charName}:";
		}

		public static void loadIcons() {
			icons.Clear();
			if (Directory.Exists($@"{imagesDir}\{selectedFolder}")) {
				foreach (string f in Directory.EnumerateFiles($@"{imagesDir}\{selectedFolder}")) {
					if (f.EndsWith("png") || f.EndsWith("jpg") || f.EndsWith("jpeg") || f.EndsWith("bmp") || f.EndsWith("gif")) {
						icons.Add(Regex.Match(f, @"([\w\s]+)\.[a-zA-Z]+$").Groups[1].Value, new Bitmap(f));
					}
				}
			}

			gbnf = "root ::= expr";
			gbnf += "\nexpr ::= (";
			foreach (string k in icons.Keys) {
				if (!gbnf.EndsWith('('))
					gbnf += " | ";
				gbnf += $@"""{k}""";
			}
			gbnf += ")";

			exprTask = $"### Instruction\nDetermine {story.charName}'s latest facial expression, based on their last response.\nPossible expressions: [{string.Join(", ", icons.Keys)}]\n\n### Response\nFor {story.charName} saying \"[MESSAGE]\", the most fitting expression is \"";
		}

		public static void AssembleMemory() {
			memory = "";
			if (story.charDesc.Length > 0) memory += $"<character name=\"{story.charName}\">\n{story.charDesc}\n</character>\n\n";
			if (story.userDesc.Length > 0) memory += $"<character name=\"{story.userName}\">\n{story.userDesc}\n</character>\n\n";
			if (story.scenario.Length > 0) memory += $"<scenario>\n{story.scenario}\n</scenario>\n\n";
			if (chatTask.Length > 0) memory += $"<system>\n{chatTask}\n</system>\n\n";
			if (story.examples.Length > 0) memory += $"<examples>\n{story.examples}\n</examples>\n\n";
			memory += "<conversation>\n";

			exprTask = $"### Instruction\nDetermine {story.charName}'s latest facial expression, based on their last response.\nPossible expressions: [{string.Join(", ", icons.Keys)}]\n\n### Response\nFor {story.charName} saying \"[MESSAGE]\", the most fitting expression is \"";
		}
	}
}
