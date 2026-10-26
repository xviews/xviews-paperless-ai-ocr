// SPDX-License-Identifier: MIT
// Copyright (C) 2026 Xviews

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace XviewsPaperlessAiOcr;

/// <summary>A file of a GitHub release, with the SHA-256 digest GitHub reports for it.</summary>
public sealed record UpdateAsset(string Name, string Url, long Size, string? Sha256);

public sealed record UpdateInfo(Version Version, string Url, IReadOnlyList<UpdateAsset> Assets)
{
	public string VersionText => $"{Version.Major}.{Version.Minor}.{Version.Build}";

	public UpdateAsset? Exe => Assets.FirstOrDefault(a => a.Name.Equals(UpdateChecker.AssetName, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Asks GitHub for a newer release and installs it on request. Only the public release page is read,
/// nothing about the user or the documents is sent.
/// </summary>
public static class UpdateChecker
{
	const string Api = "https://api.github.com/repos/xviews/xviews-paperless-ai-ocr/releases/latest";
	public const string ReleasesPage = "https://github.com/xviews/xviews-paperless-ai-ocr/releases/latest";
#if MAC
	/// <summary>One zip per processor: Apple chip (arm64) or Intel (x64).</summary>
	public static readonly string AssetName =
		$"XviewsPaperlessAiOcr-mac-{(RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64")}.zip";
#else
	public const string AssetName = "XviewsPaperlessAiOcr.exe";
#endif

	public static Version Current { get; } = Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

	static readonly HttpClient Http = CreateClient();

	static HttpClient CreateClient()
	{
		var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
		http.DefaultRequestHeaders.UserAgent.ParseAdd($"XviewsPaperlessAiOcr/{Current}");
		return http;
	}

	static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

	/// <summary>Latest release according to GitHub, null when offline or rate limited.</summary>
	public static async Task<UpdateInfo?> GetLatestAsync()
	{
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, Api);
			request.Headers.Accept.ParseAdd("application/vnd.github+json");
			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			using HttpResponseMessage response = await Http.SendAsync(request, cts.Token);
			if (!response.IsSuccessStatusCode) return null;
			using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token));
			JsonElement root = doc.RootElement;
			string tag = root.GetProperty("tag_name").GetString() ?? "";
			string url = root.TryGetProperty("html_url", out JsonElement u) ? u.GetString() ?? ReleasesPage : ReleasesPage;
			var assets = new List<UpdateAsset>();
			if (root.TryGetProperty("assets", out JsonElement list))
				foreach (JsonElement a in list.EnumerateArray())
				{
					string? digest = a.TryGetProperty("digest", out JsonElement d) ? d.GetString() : null;
					assets.Add(new UpdateAsset(
						a.GetProperty("name").GetString() ?? "",
						a.GetProperty("browser_download_url").GetString() ?? "",
						a.TryGetProperty("size", out JsonElement s) ? s.GetInt64() : 0,
						digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : null));
				}
			return Version.TryParse(tag.TrimStart('v', 'V'), out Version? version) ? new UpdateInfo(Normalize(version), url, assets) : null;
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
		{
			return null;
		}
	}

#if MAC
	/// <summary>What an update replaces: the whole .app bundle.</summary>
	static string ExePath => Installation.CurrentApp ?? throw new InvalidOperationException(T("Updates gehen nur in der fertigen App, nicht im Build-Ordner.", "Updates only work in the finished app, not in the build folder."));
#else
	static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException(T("Programmpfad unbekannt.", "Program path unknown."));
#endif
	static string OldPath => ExePath + ".alt";
	static string NewPath => ExePath + ".neu";

	/// <summary>
	/// Downloads the exe (Mac: the zipped app), checks its SHA-256 and swaps it for the running one (Windows allows
	/// renaming a running exe, macOS a running app). On any error the old version stays.
	/// </summary>
	public static async Task InstallAsync(UpdateAsset asset, IProgress<double> progress)
	{
		if (asset.Sha256 is null) throw new InvalidOperationException(T("GitHub liefert keine Prüfsumme für diese Datei. Bitte über die Download-Seite aktualisieren.", "GitHub provides no checksum for this file. Please update via the download page."));
#if MAC
		string exe = ExePath, tmp = NewPath + ".zip";
#else
		string exe = ExePath, tmp = NewPath;
#endif
		try
		{
			using (HttpResponseMessage response = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead))
			{
				response.EnsureSuccessStatusCode();
				long total = response.Content.Headers.ContentLength ?? asset.Size;
				await using Stream input = await response.Content.ReadAsStreamAsync();
				await using FileStream output = File.Create(tmp);
				var buffer = new byte[1 << 16];
				long done = 0;
				int read;
				while ((read = await input.ReadAsync(buffer)) > 0)
				{
					await output.WriteAsync(buffer.AsMemory(0, read));
					done += read;
					if (total > 0) progress.Report(done * 100.0 / total);
				}
			}

			string hash;
			await using (FileStream check = File.OpenRead(tmp))
				hash = Convert.ToHexString(await SHA256.HashDataAsync(check)).ToLowerInvariant();
			if (hash != asset.Sha256) throw new InvalidOperationException(T("Die Prüfsumme der heruntergeladenen Datei stimmt nicht. Das Update wurde nicht installiert.", "The checksum of the downloaded file does not match. The update was not installed."));

#if MAC
			SwapApp(exe, tmp);
#else
			if (File.Exists(OldPath)) File.Delete(OldPath);
			File.Move(exe, OldPath);
			try
			{
				File.Move(tmp, exe);
			}
			catch
			{
				File.Move(OldPath, exe);
				throw;
			}
#endif
		}
		finally
		{
			try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
		}
	}

#if MAC
	/// <summary>Unpacks the zip next to the running app and swaps the two bundles.</summary>
	static void SwapApp(string app, string zip)
	{
		string unpacked = NewPath;
		if (Directory.Exists(unpacked)) Directory.Delete(unpacked, recursive: true);
		var psi = new ProcessStartInfo("/usr/bin/ditto") { UseShellExecute = false, RedirectStandardError = true };
		foreach (string a in new[] { "-x", "-k", zip, unpacked }) psi.ArgumentList.Add(a);
		using (Process p = Process.Start(psi) ?? throw new IOException(T("ditto ließ sich nicht starten.", "Could not start ditto.")))
		{
			string error = p.StandardError.ReadToEnd();
			p.WaitForExit();
			if (p.ExitCode != 0) throw new IOException(T("Update ließ sich nicht entpacken", "Could not unpack the update") + $": {error.Trim()}");
		}
		try
		{
			string fresh = Directory.GetDirectories(unpacked, "*.app").SingleOrDefault()
				?? throw new InvalidOperationException("Im Update steckt keine App.");
			if (Directory.Exists(OldPath)) Directory.Delete(OldPath, recursive: true);
			Directory.Move(app, OldPath);
			try
			{
				Directory.Move(fresh, app);
			}
			catch
			{
				Directory.Move(OldPath, app);
				throw;
			}
		}
		finally
		{
			try { if (Directory.Exists(unpacked)) Directory.Delete(unpacked, recursive: true); } catch (IOException) { }
		}
	}

	public static void Restart() => Installation.OpenApp(ExePath, ["--update"]);
#else
	public static void Restart() =>
		Process.Start(new ProcessStartInfo(ExePath, "--update") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(ExePath)! });
#endif

	/// <summary>Removes the old exe left by an update; it can stay locked for a moment.</summary>
	public static async Task CleanupAsync()
	{
		for (int i = 0; i < 10; i++)
		{
			try
			{
#if MAC
				if (Installation.CurrentApp is null) return;
				if (Directory.Exists(OldPath)) Directory.Delete(OldPath, recursive: true);
				if (Directory.Exists(NewPath)) Directory.Delete(NewPath, recursive: true);
#else
				if (Environment.ProcessPath is null) return;
				if (File.Exists(OldPath)) File.Delete(OldPath);
				if (File.Exists(NewPath)) File.Delete(NewPath);
#endif
				return;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				await Task.Delay(1000);
			}
		}
	}
}
