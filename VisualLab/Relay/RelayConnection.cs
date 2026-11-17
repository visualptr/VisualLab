using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VisualLab.Logging;
using VisualLab.Security;
using VisualLab.Serialization;
using VisualLab.Storage;

namespace VisualLab.Relay;

/// <summary>Connects a dashboard session to the relay.</summary>
public sealed class RelayConnection : IDisposable
{
    private const int MaxQueuedFrames = 1024;
    private const int MaxFrameBytes = 2 * 1024 * 1024;
    private const int MaxRequestsPerKey = 50000;

    private readonly CancellationTokenSource _stop = new ();
    private readonly ConcurrentQueue<Action> _main;
    private readonly PluginStore _store;
    private readonly HashSet<string> _clients = new ();
    private readonly ConcurrentQueue<string> _outgoing = new ();
    private readonly SemaphoreSlim _signal = new (0);
    private readonly Cipher _cipher;
    private readonly HashSet<string> _received = new ();
    private readonly string _relay;
    private readonly string _code;
    private readonly string _token;
    private readonly JsonObject _info;
    private readonly DateTimeOffset _expires;

    private RelayConnection(string relay, string web, JsonObject session, byte[] key, PluginStore store, ConcurrentQueue<Action> main, JsonObject info, int ttl)
    {
        _relay = relay;
        _store = store;
        _main = main;
        _info = info;
        _code = session.Str("code");
        _token = session.Str("serverToken");
        _cipher = new Cipher(key);
        _expires = DateTimeOffset.FromUnixTimeMilliseconds((long)session.Num("expiresAt"));

        DateTimeOffset localExpiry = DateTimeOffset.UtcNow.AddMinutes(ttl);
        if (localExpiry < _expires)
            _expires = localExpiry;

        Link = web.TrimEnd('/') + "/s/" + _code + "#" + Cipher.Base64Url(key);
    }

    /// <summary>Gets the private dashboard link. The key lives only in its #fragment.</summary>
    public string Link { get; }

    /// <summary>Gets a value indicating whether a dashboard is connected to this session.</summary>
    public bool HasClients => !Closed && _clients.Count > 0;

    /// <summary>Gets a value indicating whether the dashboard session is closed.</summary>
    public bool Closed => _stop.IsCancellationRequested;

    /// <summary>Creates an encrypted dashboard session.</summary>
    /// <param name="config">The plugin settings.</param>
    /// <param name="store">The graph store.</param>
    /// <param name="main">The game-thread action queue.</param>
    /// <param name="info">The server information sent to the dashboard.</param>
    /// <param name="cancellation">The session cancellation token.</param>
    /// <returns>A task containing the new connection.</returns>
    public static async Task<RelayConnection> Create(Config config, PluginStore store, ConcurrentQueue<Action> main, JsonObject info, CancellationToken cancellation)
    {
        string relay = config.RelayUrl.TrimEnd('/');
        using HttpClient http = new () { Timeout = TimeSpan.FromSeconds(20) };
        using HttpResponseMessage response = await http.PostAsync(relay + "/sessions", new StringContent("{}"), cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        JsonObject session = Json.ParseObject(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return new RelayConnection(relay, config.WebsiteUrl, session, Cipher.RandomKey(), store, main, info, Math.Max(5, config.SessionTtlMinutes));
    }

    /// <summary>Starts the relay connection and forwards graph updates.</summary>
    public void Start()
    {
        _store.Changed += Push;
        RuntimeLog.Pushed += Push;
        _ = Task.Run(Run);
    }

    /// <summary>Closes the session and stops forwarding updates.</summary>
    public void Dispose()
    {
        if (Closed)
            return;

        _store.Changed -= Push;
        RuntimeLog.Pushed -= Push;
        _clients.Clear();
        _stop.Cancel();
    }

    private void Send(string to, JsonObject value)
    {
        if (Closed)
            return;

        if (_outgoing.Count >= MaxQueuedFrames)
            return;

        _outgoing.Enqueue(Json.Write(new JsonObject { ["to"] = to, ["data"] = _cipher.Seal(value) }));
        _signal.Release();
    }

    private void Push(JsonObject push)
    {
        foreach (string client in _clients)
            Send(client, push);
    }

    private async Task Run()
    {
        try
        {
            while (!Closed && DateTimeOffset.UtcNow < _expires)
            {
                using (ClientWebSocket socket = new ())
                using (CancellationTokenSource attempt = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
                {
                    attempt.CancelAfter(_expires - DateTimeOffset.UtcNow);
                    try
                    {
                        string url = _relay.Replace("https://", "wss://").Replace("http://", "ws://");
                        await socket.ConnectAsync(new Uri(url + "/s/" + _code + "?role=server&token=" + Uri.EscapeDataString(_token)), attempt.Token).ConfigureAwait(false);
                        Task sending = SendLoop(socket, attempt.Token);
                        try
                        {
                            await ReceiveLoop(socket, attempt.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            attempt.Cancel();
                            socket.Abort();
                            try
                            {
                                await sending.ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                            }
                        }
                    }
                    catch (Exception e) when (e is WebSocketException or OperationCanceledException or HttpRequestException)
                    {
                    }
                }

                if (!Closed && DateTimeOffset.UtcNow < _expires)
                    await Task.Delay(2000, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _main.Enqueue(Dispose);
        }
    }

    private async Task SendLoop(ClientWebSocket socket, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            await _signal.WaitAsync(cancellation).ConfigureAwait(false);
            if (!_outgoing.TryDequeue(out string frame))
                continue;

            byte[] bytes = Encoding.UTF8.GetBytes(frame);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation).ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken cancellation)
    {
        byte[] buffer = new byte[8192];

        using MemoryStream message = new ();
        while (!cancellation.IsCancellationRequested)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return;

            if (result.MessageType != WebSocketMessageType.Text || message.Length + result.Count > MaxFrameBytes)
            {
                socket.Abort();
                return;
            }

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
                continue;

            string raw = Encoding.UTF8.GetString(message.ToArray());
            message.SetLength(0);

            JsonObject envelope;
            try
            {
                envelope = Json.ParseObject(raw);
            }
            catch (FormatException)
            {
                continue;
            }

            if (envelope.Str("relay") == "client_close")
            {
                string id = envelope.Str("id");
                _main.Enqueue(() => _clients.Remove(id));
                continue;
            }

            string client = envelope.Str("from");
            string frame = envelope.Str("data");
            if (client == null || frame == null)
                continue;

            JsonObject request;
            try
            {
                request = _cipher.Open(frame);
            }
            catch
            {
                continue;
            }

            if (!_received.Add(Cipher.Fingerprint(frame)))
                continue;

            if (_received.Count > MaxRequestsPerKey)
            {
                _main.Enqueue(Dispose);
                return;
            }

            if (_main.Count > MaxQueuedFrames)
                continue;

            _main.Enqueue(() => Handle(client, request));
        }
    }

    private void Handle(string client, JsonObject request)
    {
        if (Closed)
            return;

        JsonObject response;
        try
        {
            if (request.Str("type") == "hello")
            {
                if (request.Str("protocol") != "visuallab-ws/1")
                    throw new ProtocolException("unsupported", "Unsupported protocol.");

                if (request.Str("token") != _code)
                    throw new ProtocolException("unauthorized", "Invalid session.");

                _clients.Add(client);
                response = new JsonObject { ["server"] = _info };
            }
            else
            {
                if (!_clients.Contains(client))
                    throw new ProtocolException("unauthorized", "Send hello first.");

                response = _store.Handle(request);
            }

            response["ok"] = true;
        }
        catch (Exception e)
        {
            ProtocolException protocolError = e as ProtocolException;
            JsonObject error = new () { ["code"] = protocolError?.Code ?? "internal", ["message"] = e.Message };
            if (protocolError?.Plugin != null)
                error["plugin"] = protocolError.Plugin;

            response = new JsonObject { ["ok"] = false, ["error"] = error };
        }

        response["rid"] = request.Num("rid");
        Send(client, response);
    }
}
