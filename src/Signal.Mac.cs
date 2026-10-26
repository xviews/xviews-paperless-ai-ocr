// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using System.Net.Sockets;

namespace XviewsPaperlessAiOcr;

/// <summary>
/// Benanntes Signal zwischen zwei Prozessen des Tools. macOS kennt keine benannten Events,
/// darum lauscht das laufende Tool an einem Unix-Socket im Temp-Ordner des Benutzers; ein Verbindungsaufbau ist das Signal.
/// </summary>
sealed class Signal(string name) : IDisposable
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"{name}.sock");
    Socket? listener;

    public void Set()
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(path));
        }
        catch (SocketException) { }
    }

    /// <summary>Ruft action bei jedem Signal auf, auf einem Thread-Pool-Thread.</summary>
    public void Listen(Action action)
    {
        // Ein Rest vom letzten Lauf blockiert sonst das Binden; wer lauscht, hält schon den Mutex
        try { File.Delete(path); } catch (IOException) { }
        listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(4);
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using Socket client = await listener.AcceptAsync();
                    action();
                }
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException) { }
        });
    }

    public void Dispose()
    {
        if (listener is null) return;
        listener.Dispose();
        try { File.Delete(path); } catch (IOException) { }
    }
}
