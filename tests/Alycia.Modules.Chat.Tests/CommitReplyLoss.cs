using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Npgsql;

namespace Alycia.Modules.Chat.Tests;

// PostgreSQL v3 over test loopback. On the first committed transaction, discard
// the server's CommandComplete reply and close that connection. Later clients pass through.
internal sealed class CommitReplyLoss : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Task _server;
    private int _drops;
    internal string ConnectionString { get; }
    internal int Drops => Volatile.Read(ref _drops);

    internal CommitReplyLoss(string database)
    {
        var settings = new NpgsqlConnectionStringBuilder(database);
        if (settings.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("Le test de coupure utilise un serveur de boucle locale.");
        var upstream = settings.Port;
        _listener.Start();
        settings.Host = "127.0.0.1";
        settings.Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        settings.Pooling = false;
        settings.SslMode = SslMode.Disable;
        settings.GssEncryptionMode = GssEncryptionMode.Disable;
        ConnectionString = settings.ConnectionString;
        _server = ServeAsync(upstream, _stop.Token);
    }

    private async Task ServeAsync(int upstream, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var client = await _listener.AcceptTcpClientAsync(ct);
            using var postgres = new TcpClient();
            await postgres.ConnectAsync(IPAddress.Loopback, upstream, ct);
            using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var sending = client.GetStream().CopyToAsync(postgres.GetStream(), session.Token);
            var receiving = ForwardRepliesAsync(postgres.GetStream(), client.GetStream(), session.Token);
            await Task.WhenAny(sending, receiving);
            await session.CancelAsync();
            postgres.Close();
            client.Close();
            try { await Task.WhenAll(sending, receiving); }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    private async Task ForwardRepliesAsync(NetworkStream source, NetworkStream destination, CancellationToken ct)
    {
        var header = new byte[5];
        while (true)
        {
            await source.ReadExactlyAsync(header, ct);
            var payload = new byte[checked(BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1)) - 4)];
            await source.ReadExactlyAsync(payload, ct);
            if (header[0] == (byte)'C' && payload.AsSpan().SequenceEqual("COMMIT\0"u8)
                && Interlocked.CompareExchange(ref _drops, 1, 0) == 0) return;
            await destination.WriteAsync(header, ct);
            await destination.WriteAsync(payload, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try { await _server; }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        _stop.Dispose();
    }
}
