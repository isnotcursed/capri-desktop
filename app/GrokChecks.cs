using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CapriDesktop
{
    internal static class GrokChecks
    {
        private static string ReadRequest(NetworkStream stream)
        {
            StringBuilder head = new StringBuilder();
            while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                int next = stream.ReadByte();
                if (next < 0) throw new IOException("Grok's request ended early.");
                head.Append((char)next);
            }
            int length = 0;
            foreach (string line in head.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None))
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Substring(15).Trim());
            byte[] body = new byte[length];
            for (int at = 0; at < length;)
            {
                int read = stream.Read(body, at, length - at);
                if (read <= 0) throw new IOException("Grok's request body ended early.");
                at += read;
            }
            return head + Encoding.UTF8.GetString(body);
        }

        internal static void Run(List<string> checks)
        {
            string key = "xai-" + new string('a', 80);
            Cloud cloud = new Cloud(key, "grok-test");
            if (cloud.Kind != "xai" || cloud.Name != "Grok" || cloud.Company != "xAI" || Cloud.MediumKind(cloud.Kind)
                || Cloud.KindOf("xai-") != null || Cloud.KindOf(key + "\n") != null || Cloud.KindOf("xai-bad key") != null
                || !Brain.ModelFits("xai", "grok-test") || Brain.ModelFits("xai", "gpt-test")) throw new Exception("Grok keys or models were sorted wrong.");
            if (Brain.Address("xai", "/models") != "https://api.x.ai/v1/models" || Brain.Address("xai", "/chat/completions") != "https://api.x.ai/v1/chat/completions") throw new Exception("Grok has the wrong address.");
            if (Brain.Redacted("Key " + key).Contains(key)) throw new Exception("A Grok key reached her log.");
            const string catalog = "{\"data\":[null,{\"id\":\"grok-test\",\"created\":2},{\"id\":\"grok-older\",\"created\":1},{\"id\":\"grok-test\"},{\"id\":\"grok-imagine-image\"},{\"id\":\"grok-imagine-video\"},{\"id\":\"grok-audio\"},{\"id\":\"other-model\"}]}";
            List<string> models = Brain.ReadCloudModels("xai", catalog);
            if (models.Count != 2 || Brain.DefaultCloudModel("xai", models) != "grok-test") throw new Exception("Grok's model list kept media models or lost chat models.");
            TcpListener port = new TcpListener(IPAddress.Loopback, 0);
            port.Start();
            string address = "http://127.0.0.1:" + ((IPEndPoint)port.LocalEndpoint).Port;
            string old = Brain.Xai;
            List<string> asked = new List<string>();
            Exception failed = null;
            try
            {
                Thread worker = new Thread((ThreadStart)delegate
                {
                    try
                    {
                        string[] replies = {
                            catalog,
                            "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Hello.\"}}]}",
                            "{\"choices\":[{\"message\":{\"tool_calls\":[{\"id\":\"call_test\",\"type\":\"function\",\"function\":{\"name\":\"done\",\"arguments\":\"{\\\"text\\\":\\\"Finished.\\\"}\"}}]}}]}",
                            "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"A screen.\"}}]}"
                        };
                        foreach (string reply in replies)
                        {
                            using (TcpClient client = port.AcceptTcpClient())
                            using (NetworkStream stream = client.GetStream())
                            {
                                stream.ReadTimeout = stream.WriteTimeout = 5000;
                                asked.Add(ReadRequest(stream));
                                byte[] bytes = Encoding.UTF8.GetBytes(reply);
                                byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
                                stream.Write(head, 0, head.Length);
                                stream.Write(bytes, 0, bytes.Length);
                            }
                        }
                    }
                    catch (Exception error) { failed = error; }
                });
                worker.IsBackground = true;
                worker.Start();
                try
                {
                    Brain.Xai = address;
                    if (Brain.CloudModels(key).Count != 2) throw new Exception("Grok discovery lost its models.");
                    Brain brain = new Brain { Remote = cloud, Model = cloud.Model };
                    if (brain.Ask("Hello") != "Hello.") throw new Exception("Grok chat lost its reply.");
                    List<KeyValuePair<string, ToolArguments>> calls = new List<KeyValuePair<string, ToolArguments>>();
                    brain.Step("Job", "Finish", "[{\"type\":\"function\",\"function\":{\"name\":\"done\",\"parameters\":{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\"}}}}}]", null, "", calls);
                    if (calls.Count != 1 || calls[0].Key != "done" || calls[0].Value.Text != "Finished.") throw new Exception("Grok's tool call was lost.");
                    if (brain.See(new byte[] { 1, 2, 3 }, "Look", "Screen", 90, true) != "A screen.") throw new Exception("Grok lost her screenshot.");
                    if (!worker.Join(3000) || failed != null || asked.Count != 4) throw new Exception("Grok's fake server did not finish.");
                    for (int i = 0; i < asked.Count; i++)
                        if (!asked[i].StartsWith(i == 0 ? "GET /v1/models " : "POST /v1/chat/completions ", StringComparison.Ordinal)
                            || !asked[i].Contains("Bearer " + key) || asked[i].Contains("max_completion_tokens") || asked[i].Contains("reasoning_effort") || asked[i].Contains("chat_template_kwargs") || asked[i].Contains("\"thinking\"")) throw new Exception("Grok used another provider's request settings.");
                    if (!asked[2].Contains("\"tools\":") || !asked[2].Contains("\"max_tokens\":") || !asked[3].Contains("data:image/jpeg;base64,AQID")) throw new Exception("Grok lost her tools or image content.");
                }
                finally { Brain.Xai = old; port.Stop(); worker.Join(3000); }
            }
            finally { port.Stop(); }
            checks.Add("PASS Grok: xAI keys select MAX INTELLIGENCE, chat models load without image or video generators, chat, tools and screenshots use xAI's bearer and routes, and logs redact xai- keys");
        }
    }
}
