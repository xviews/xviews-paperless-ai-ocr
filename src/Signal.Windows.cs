// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

namespace XviewsPaperlessAiOcr;

/// <summary>Benanntes Signal zwischen zwei Prozessen des Tools (Windows: Event-Objekt der Sitzung).</summary>
sealed class Signal(string name) : IDisposable
{
    readonly EventWaitHandle handle = new(false, EventResetMode.AutoReset, $@"Local\{name}");
    // Festhalten, sonst räumt der Garbage Collector das Handle weg und das Signal kommt nie an
    RegisteredWaitHandle? wait;

    public void Set() => handle.Set();

    /// <summary>Ruft action bei jedem Signal auf, auf einem Thread-Pool-Thread.</summary>
    public void Listen(Action action) =>
        wait = ThreadPool.RegisterWaitForSingleObject(handle, (_, _) => action(), null, -1, false);

    public void Dispose()
    {
        wait?.Unregister(null);
        handle.Dispose();
    }
}
