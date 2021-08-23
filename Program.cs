using System;
using System.Net.WebSockets;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace com.attackforge
{
    class Program
    {
        static ClientWebSocket webSocket = null;
        static Dictionary<string, TaskCompletionSource<JsonRpcResponse>> pendingRequests;
        static System.Timers.Timer heartbeatTimer;


        static void Notification(String method, SsApiEventParams parameters)
        {
            Console.WriteLine("method: {0}", method);
            Console.WriteLine("params");

            JsonSerializerOptions options = new JsonSerializerOptions();
            options.WriteIndented = true;

            Console.WriteLine(JsonSerializer.Serialize(parameters, options));

            /* ENTER YOUR INTEGRATION CODE HERE */
            /* method contains the event type e.g. vulnerability-created */
            /* parameters contains the event body e.g. JSON object with timestamp & vulnerability details */
	    }


        static void Main(string[] args)
        {
            pendingRequests = new Dictionary<string, TaskCompletionSource<JsonRpcResponse>>();

            heartbeatTimer = new System.Timers.Timer(30000 + 1000);

            heartbeatTimer.Elapsed += (source, e) =>
            {
                TerminateWebSocket();
            };

            Connect();
        }

        private static void Connect()
        {
            Console.Write("Connecting...");

            if (Environment.GetEnvironmentVariable("HOSTNAME") == null)
            {
                Console.WriteLine("Environment variable HOSTNAME is undefined");
                Environment.Exit(1);
            }

            if (Environment.GetEnvironmentVariable("EVENTS") == null)
            {
                Console.WriteLine("Environment variable EVENTS is undefined");
                Environment.Exit(1);
            }

            if (Environment.GetEnvironmentVariable("X_SSAPI_KEY") == null)
            {
                Console.WriteLine("Environment variable X_SSAPI_KEY is undefined");
                Environment.Exit(1);
            }

            string port = "443";

            if (Environment.GetEnvironmentVariable("PORT") != null)
            {
                port = Environment.GetEnvironmentVariable("PORT");
            }

            webSocket = new ClientWebSocket();

            webSocket.Options.SetRequestHeader("X-SSAPI-KEY", Environment.GetEnvironmentVariable("X_SSAPI_KEY"));

            // This is to trust all certificates including self signed - not recommended for production
            //webSocket.Options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
            //{
            //    return true;
            //};

            Uri uri = new Uri(string.Format("wss://{0}:{1}/api/ss/events", Environment.GetEnvironmentVariable("HOSTNAME"), port));

            try
            {
                webSocket.ConnectAsync(uri, new CancellationTokenSource().Token).Wait();

                Console.WriteLine("success");

                Subscribe();
                Heartbeat();

                StartReceiving();
            }
            catch (Exception e)
            {
                Console.WriteLine("failed");
                Console.WriteLine(e);

                Thread.Sleep(1000);
                Connect();
            }
        }

        private static void Heartbeat()
        {
            heartbeatTimer.Stop();
            heartbeatTimer.Start();
        }

        private static string LoadReplayTimestamp()
        { 
            string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss'.'FFFzzz");

            try
            {
                using (FileStream fs = File.OpenRead(".replay_timestamp"))
                {
                    byte[] buffer = new byte[24];

                    if (fs.Read(buffer, 0, 24) == 24) 
		            {
                        timestamp = new UTF8Encoding().GetString(buffer);

                        Console.WriteLine("Loaded replay timestamp from storage: {0}", timestamp);
		            }
                    else
                    {
                        Console.WriteLine("Invalid timestamp stored in \".replay_timestamp\"");
		            }
                }
            }
            catch
            {
                if (Environment.GetEnvironmentVariable("FROM") != null)
                {
                    timestamp = Environment.GetEnvironmentVariable("FROM");
                    Console.WriteLine("Loaded replay timestamp from environment: {0}", timestamp);
                }
            }


            return timestamp;
	    }

        private static async void ProcessCompleteMessage(string text)
        {
            try
            {
                JsonDocument json = JsonDocument.Parse(text);

                if (json.RootElement.ValueKind == JsonValueKind.Object)
                {
                    JsonElement jsonrpc = new JsonElement();

                    if (json.RootElement.TryGetProperty("jsonrpc", out jsonrpc) && jsonrpc.GetString() == "2.0")
                    {
                        JsonElement method = new JsonElement();
                        bool methodExists = json.RootElement.TryGetProperty("method", out method) && method.ValueKind == JsonValueKind.String;

                        JsonElement parameters = new JsonElement();
                        bool parametersExists = json.RootElement.TryGetProperty("params", out parameters) && parameters.ValueKind == JsonValueKind.Object;

                        JsonElement result = new JsonElement();
                        bool resultExists = json.RootElement.TryGetProperty("result", out result);

                        JsonElement error = new JsonElement();
                        bool errorExists = json.RootElement.TryGetProperty("error", out error);

                        JsonElement id = new JsonElement();
                        bool idExists = json.RootElement.TryGetProperty("id", out id) && id.ValueKind == JsonValueKind.String;


                        if (methodExists && parametersExists && !idExists)
                        {
                            SsApiEventParams payload = JsonSerializer.Deserialize<SsApiEventParams>(parameters.GetRawText());

                            if (payload.Timestamp != null)
                            {
                                StoreReplayTimestamp(payload.Timestamp);
			                }

                            Notification(method.GetString(), payload);
                        }
                        else if (methodExists && idExists)
                        {
                            if (method.GetString() == "heartbeat")
                            {
                                Heartbeat();

                                HeartbeatResponse heartbeatResponse = new HeartbeatResponse
                                {
                                    JsonRPC = "2.0",
                                    ID = id.GetString(),
                                    Result = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss'.'FFFzzz")
                                };

                                byte[] buffer = new UTF8Encoding().GetBytes(JsonSerializer.Serialize(heartbeatResponse));

                                try
                                {
                                    await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, new CancellationTokenSource().Token);
                                }
                                catch
                                {
                                    Console.WriteLine("Failed to respond to Heartbeat");
                                    TerminateWebSocket();
                                }
			                }
                        }
                        else if (resultExists && idExists)
                        {
                            JsonRpcResponse response = new JsonRpcResponse
                            {
                                Id = id.GetString(),
                                Result = result
                            };

                            if (pendingRequests.ContainsKey(id.GetString())) 
			                {
                                pendingRequests[id.ToString()].SetResult(response);
			                }
                        }
                        else if (errorExists && idExists)
                        {
				            JsonRpcResponse response = new JsonRpcResponse
				            {
				                Id = id.GetString(),
				                Error = error
				            };

                            if (pendingRequests.ContainsKey(id.GetString())) 
			                {
                                pendingRequests[id.ToString()].SetResult(response);
			                }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("JSON parse error - ignoring message");
                Console.WriteLine(e);
            }
        }

        private static void StartReceiving()
        {
            bool run = true;

            byte[] buffer = new byte[1024];
            StringBuilder builder = new StringBuilder();

            while (run)
            {
                try
                {
                    Task<WebSocketReceiveResult> task = webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), new CancellationTokenSource().Token);
                    task.Wait();

                    WebSocketReceiveResult result = task.Result;

                    builder.Append(new UTF8Encoding().GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        if (builder.Length > 0)
                        { 
                            ProcessCompleteMessage(builder.ToString());
			            }

                        builder.Clear();
                    }

                }
                catch
                {
                    run = false;
                }
            }

            Connect();
        }

        private static void StoreReplayTimestamp(String timestamp)
        { 
            try {
                using (FileStream fs = File.Create(".replay_timestamp"))
                {
                    byte[] buffer = new byte[24];

                    if (new UTF8Encoding().GetBytes(timestamp, 0, 24, buffer, 0) == 24)
                    {
                        fs.Write(buffer, 0, 24);
		            }
                    else
                    {
                        Console.WriteLine("Supplied replay timestamp not of expected length");
		            }
		        }
	        }
            catch (Exception e)
            {
                Console.WriteLine("Failed to store replay timestamp");
                Console.WriteLine(e.Message);
	        }
	    }

        private static async void Subscribe()
        {
            string[] events = Environment.GetEnvironmentVariable("EVENTS").Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            Guid requestId = Guid.NewGuid();

            var request = new SubscriptionRequest
            {
                JsonRPC = "2.0",
                Method = "subscribe",
                Params = new SubscriptionParams { Events = events, From = LoadReplayTimestamp() },
                ID = requestId.ToString()
            };

            string jsonString = JsonSerializer.Serialize(request);

            byte[] buffer = new UTF8Encoding().GetBytes(jsonString);

            try
            {
                TaskCompletionSource<JsonRpcResponse> subscriptionCompletionSource = new TaskCompletionSource<JsonRpcResponse>();
                pendingRequests.Add(requestId.ToString(), subscriptionCompletionSource);


                await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, new CancellationTokenSource().Token);
                Task completed = await Task.WhenAny(subscriptionCompletionSource.Task, Task.Delay(5000));

                if (completed == subscriptionCompletionSource.Task)
                {
                    JsonRpcResponse response = subscriptionCompletionSource.Task.Result;

                    if (response.Result != null) {
                        Console.WriteLine("Subscribed to the following events: {0}", response.Result.ToString());
                        pendingRequests.Remove(requestId.ToString());
		            }
                    else if (response.Error != null) { 
                        Console.WriteLine("Subscription request {0} failed - exiting", response.Id);
                        Console.WriteLine(response.Error.ToString());
                        pendingRequests.Remove(requestId.ToString());
                        Environment.Exit(1);
		            }
                }
                else
                {
                    pendingRequests.Remove(requestId.ToString());
                    Console.WriteLine("Subscription request {0} timed out - exiting", requestId.ToString());
                    Environment.Exit(1);
                }
            }
            catch
            {
                pendingRequests.Remove(requestId.ToString());

                Thread.Sleep(1000);
                TerminateWebSocket();
            }
        }

        private static void TerminateWebSocket()
        {
            try
            {
                webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", new CancellationTokenSource().Token).Wait();
                webSocket.Abort();
            }
            catch { }
        }
    }
}
