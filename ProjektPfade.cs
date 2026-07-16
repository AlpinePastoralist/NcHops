using System;
using System.IO;

namespace NCHops;

/// <summary>
/// Ermittelt die Speicherorte für Daten, die zusammen mit dem Projekt versioniert
/// werden sollen (Werkzeugliste, Werkzeugbilder), damit sie bei "git pull" automatisch
/// bei allen mitkommen, statt nur lokal pro Rechner in %LocalAppData% zu liegen.
///
/// Zur Laufzeit wird ausgehend vom Ausführungsverzeichnis nach oben nach der
/// NCHops.csproj gesucht (funktioniert für "dotnet run"/Visual Studio direkt aus dem
/// Quellcode). Wird sie nicht gefunden (z. B. bei einer eigenständigen Installation ohne
/// Quellcode), wird auf %LocalAppData%\NCHops zurückgefallen.
/// </summary>
internal static class ProjektPfade
{
    private static readonly Lazy<string> _basisOrdner = new(FindBasisOrdner);

    public static string WerkzeugDatei        => Path.Combine(_basisOrdner.Value, "werkzeuge.json");
    public static string WerkzeugBilderOrdner => Path.Combine(_basisOrdner.Value, "WerkzeugBilder");

    private static string FindBasisOrdner()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("NCHops.csproj").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NCHops");
    }
}
