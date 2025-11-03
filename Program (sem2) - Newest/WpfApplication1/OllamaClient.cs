using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using WpfApplication1.Helpers;

namespace WpfApplication1
{
    public class OllamaClient
    {
        private readonly string _baseUrl = "http://localhost:11434";
        private readonly int _timeoutMs = 150000;

        // Define separate temperatures for each personality
        private const double TemperatureDefault = 0.05;
        private const double TemperatureFriendly = 0.5;
        private const double TemperatureStrict = 0.2;

        private const double TopP = 0.9;
        private const int NumPredict = 140;
        private const double RepeatPenalty = 1.1;

        public OllamaClient() { }

        // Overload for use by recognizer to include debug flags
        public string ApiCallDeterministicOverall(string prompt, string formattedFlagsForDebug)
        {
            string rawJson = CallChatApi(prompt, _timeoutMs);
            string extracted = ExtractAssistantText(rawJson);

            LogRaw("Overall-only", prompt, rawJson, extracted, formattedFlagsForDebug);

            if (IsNullOrWhiteSpace35(extracted)) extracted = rawJson;
            if (IsNullOrWhiteSpace35(extracted)) return "";
            return NormalizeOutput(extracted);
        }

        // Original method for simple calls like TestCall
        public string ApiCallDeterministicOverall(string prompt)
        {
            return ApiCallDeterministicOverall(prompt, null);
        }

        public string TestCall()
        {
            try
            {
                StringBuilder prompt = new StringBuilder();
                string selectedModel = AppSettings.SelectedCoachModel;
                string selectedLanguage = AppSettings.SelectedLanguage;

                if (selectedLanguage != "English")
                {
                    prompt.AppendLine("IMPORTANT: You must reply in " + selectedLanguage + ".");
                }

                if (selectedModel == "friendly3bmcoach")
                {
                    prompt.AppendLine("REMEMBER: You must respond as a friendly, funny, and sarcastic coach.");
                }
                else if (selectedModel == "strict3bmcoach")
                {
                    prompt.AppendLine("REMEMBER: You must respond as a strict Drill Sergeant addressing a recruit.");
                }

                prompt.AppendLine("You are a coaching assistant that summarises squat analysis.");
                prompt.AppendLine("Follow these rules exactly:");
                prompt.AppendLine("1. Sentence 1 must be exactly \"normal squat with neutral bias.\"");
                prompt.AppendLine("2. Write 1 to 2 additional sentences that concisely summarise the major issues described in the paragraph.");
                prompt.AppendLine("3. Do not invent information not present in the paragraph.");
                prompt.AppendLine("4. Keep the summary to at most 3 sentences in total.");
                prompt.AppendLine("5. Plain text only; no headings or bullet points.");
                prompt.AppendLine("6. Do not include explicit action or prescription sentences.");
                prompt.AppendLine("7. End the entire response with <END>.");
                prompt.AppendLine();
                prompt.AppendLine("JSON:");
                prompt.AppendLine("{\"squatType\":\"normal squat\",\"maxDepthCm\":32.0,\"bottomBias\":\"neutral bias\"}");
                prompt.AppendLine();
                prompt.AppendLine("Issue paragraph:");
                prompt.AppendLine("Legs too wide was observed from standing through ascending before improving, while trunk control stayed solid.");

                string response = ApiCallDeterministicOverall(prompt.ToString());
                if (IsNullOrWhiteSpace35(response))
                {
                    return "Test call produced no response.";
                }
                return response;
            }
            catch (Exception ex)
            {
                return "Test call error: " + ex.Message;
            }
        }

        private string CallChatApi(string userContent, int timeoutMs)
        {
            try
            {
                string url = _baseUrl.TrimEnd('/') + "/api/chat";
                string selectedModel = AppSettings.SelectedCoachModel;

                double temperatureForCall = TemperatureDefault;
                if (selectedModel == "friendly3bmcoach")
                {
                    temperatureForCall = TemperatureFriendly;
                }
                else if (selectedModel == "strict3bmcoach")
                {
                    temperatureForCall = TemperatureStrict;
                }

                StringBuilder body = new StringBuilder();
                body.Append("{");
                body.Append("\"model\":\"").Append(JsonEscape(selectedModel)).Append("\",");
                body.Append("\"stream\":false,");
                body.Append("\"options\":{");
                body.Append("\"temperature\":").Append(temperatureForCall.ToString(CultureInfo.InvariantCulture)).Append(",");
                body.Append("\"top_p\":").Append(TopP.ToString(CultureInfo.InvariantCulture)).Append(",");
                body.Append("\"num_predict\":").Append(NumPredict.ToString(CultureInfo.InvariantCulture)).Append(",");
                body.Append("\"repeat_penalty\":").Append(RepeatPenalty.ToString(CultureInfo.InvariantCulture)).Append(",");
                body.Append("\"presence_penalty\":0.0,");
                body.Append("\"frequency_penalty\":0.0");
                body.Append("},");
                body.Append("\"messages\":[");
                body.Append("{\"role\":\"user\",\"content\":\"").Append(JsonEscape(userContent)).Append("\"}");
                body.Append("]}");

                byte[] data = Encoding.UTF8.GetBytes(body.ToString());

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = timeoutMs;

                using (Stream reqStream = request.GetRequestStream())
                {
                    reqStream.Write(data, 0, data.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader sr = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
            catch (WebException wex)
            {
                string errBody = "";
                if (wex.Response != null)
                {
                    try
                    {
                        using (HttpWebResponse er = (HttpWebResponse)wex.Response)
                        using (StreamReader sr = new StreamReader(er.GetResponseStream(), Encoding.UTF8))
                        {
                            errBody = sr.ReadToEnd();
                        }
                    }
                    catch { }
                }
                return "HTTP error: " + wex.Message + (string.IsNullOrEmpty(errBody) ? "" : " | Body: " + errBody);
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }

        private static void LogRaw(string tag, string prompt, string httpJson, string assistantText, string formattedFlags)
        {
            try
            {
                Console.WriteLine("========== Ollama Debug [" + tag + "] ==========");
                if (!IsNullOrWhiteSpace35(prompt))
                {
                    Console.WriteLine("Prompt:");
                    Console.WriteLine(prompt);

                    if (!IsNullOrWhiteSpace35(formattedFlags))
                    {
                        Console.WriteLine("Phase Flags:");
                        Console.WriteLine(formattedFlags);
                    }

                    Console.WriteLine("-----------------------------------------------");
                }

                Console.WriteLine("HTTP JSON (full):");
                Console.WriteLine(httpJson ?? "");
                Console.WriteLine("-----------------------------------------------");

                Console.WriteLine("Assistant extracted text:");
                Console.WriteLine(IsNullOrWhiteSpace35(assistantText) ? "(empty)" : assistantText);
                Console.WriteLine("===============================================\n");
            }
            catch { }
        }

        public static string NormalizeOutput(string text)
        {
            if (text == null) return "";
            string s = text.Trim();

            // Find the first "<END>" (case-sensitive to match stop token)
            int idxEndTag = s.IndexOf("<END>", StringComparison.Ordinal);
            if (idxEndTag >= 0)
            {
                // Remove any standalone "END" tokens that appear BEFORE "<END>"
                // This handles variants like "END", "END.", "END," as accidental echoes.
                StringBuilder cleaned = new StringBuilder(s.Length);
                int i = 0;
                while (i < s.Length)
                {
                    if (i >= idxEndTag) break;

                    // Detect a bare END word with optional trailing punctuation/spaces
                    if (IsWordENDAt(s, i))
                    {
                        // Skip the word END and any immediate punctuation like . , ; : ! ?
                        int j = i + 3;
                        while (j < s.Length)
                        {
                            char pc = s[j];
                            if (pc == '.' || pc == ',' || pc == ';' || pc == ':' || pc == '!' || pc == '?' || pc == ' ')
                                j++;
                            else break;
                        }
                        i = j;
                        continue; // do not append this token
                    }

                    cleaned.Append(s[i]);
                    i++;
                }

                // Append the untouched part up to idxEndTag (if any tokens left after removals)
                if (i < idxEndTag)
                    cleaned.Append(s.Substring(i, idxEndTag - i));

                // Append the canonical terminator and ignore anything after it
                cleaned.Append("<END>");
                return cleaned.ToString().Trim();
            }
            else
            {
                // No "<END>" present; still remove trailing bare END if the model added it
                string withoutBareEnd = StripTrailingBareEND(s);
                return withoutBareEnd.TrimEnd() + " <END>";
            }
        }

        // Helper: detect a standalone word "END" at position pos (word boundaries)
        private static bool IsWordENDAt(string s, int pos)
        {
            if (pos < 0 || pos + 3 > s.Length) return false;
            if (s[pos] != 'E' || s[pos + 1] != 'N' || s[pos + 2] != 'D') return false;

            bool leftBoundary = (pos == 0) || !char.IsLetterOrDigit(s[pos - 1]);
            bool rightBoundary = (pos + 3 >= s.Length) || !char.IsLetterOrDigit(s[pos + 3]);
            return leftBoundary && rightBoundary;
        }

        // Remove a trailing bare END (and punctuation) when no "<END>" exists
        private static string StripTrailingBareEND(string s)
        {
            int i = s.Length - 1;

            // Trim trailing whitespace first
            while (i >= 0 && char.IsWhiteSpace(s[i])) i--;
            int endTrim = i;

            // Optionally trim trailing punctuation
            while (i >= 0 && (s[i] == '.' || s[i] == ',' || s[i] == ';' || s[i] == ':' || s[i] == '!' || s[i] == '?'))
                i--;

            // Check if the preceding token is bare END
            int start = i - 2; // length of "END" is 3
            if (start >= 0 && s.Length >= 3 && s.Substring(start, 3) == "END")
            {
                bool leftBoundary = (start == 0) || !char.IsLetterOrDigit(s[start - 1]);
                bool rightBoundary = (endTrim + 1 >= s.Length) || (endTrim + 1 == s.Length);
                if (leftBoundary && rightBoundary)
                {
                    // Remove from start to original end
                    return s.Substring(0, start).TrimEnd();
                }
            }
            return s;
        }

        private static string JsonEscape(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 32)
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string ExtractAssistantText(string ollamaJson)
        {
            if (string.IsNullOrEmpty(ollamaJson)) return "";

            const string key = "\"content\":\"";
            int start = ollamaJson.LastIndexOf(key, StringComparison.Ordinal);
            if (start < 0) return "";
            start += key.Length;

            StringBuilder sb = new StringBuilder();
            int i = start;
            while (i < ollamaJson.Length)
            {
                char c = ollamaJson[i++];
                if (c == '\\')
                {
                    if (i >= ollamaJson.Length) break;
                    char e = ollamaJson[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case '\\': sb.Append('\\'); break;
                        case '\"': sb.Append('\"'); break;
                        case 'u':
                            if (i + 4 <= ollamaJson.Length)
                            {
                                string hex = ollamaJson.Substring(i, 4);
                                ushort code;
                                if (ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                {
                                    sb.Append((char)code);
                                    i += 4;
                                }
                            }
                            break;
                        default:
                            sb.Append(e); break;
                    }
                }
                else if (c == '\"')
                {
                    break;
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        public static bool IsNullOrWhiteSpace35(string s)
        {
            if (s == null) return true;
            for (int i = 0; i < s.Length; i++)
            {
                if (!char.IsWhiteSpace(s[i])) return false;
            }
            return true;
        }
    }
}