namespace ReverseGeocodeApi.Services;

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.IO;
using ReverseGeocodeApi.Models;


/// <summary>
/// Loads CAOP datasets from disk and answers reverse-geocoding queries.
/// V1: In-memory, no database.
/// </summary>
public sealed class CaopDatasetService
{
    private readonly ILogger<CaopDatasetService> _logger;
    private readonly CaopOptions _options;

    private volatile LoadedDataset? _active;
    private readonly object _loadLock = new();

    public CaopDatasetService(IOptions<CaopOptions> options, ILogger<CaopDatasetService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Gets the configured active dataset name.
    /// </summary>
    public string ActiveDatasetName => _options.ActiveDataset ?? "";

    /// <summary>
    /// Gets a value indicating whether the active dataset has already been loaded into memory.
    /// </summary>
    public bool IsLoaded => _active != null;

    /// <summary>
    /// Gets the number of loaded records in the active dataset, or zero if it has not been loaded yet.
    /// </summary>
    public int LoadedRecordCount => _active?.Records.Count ?? 0;

    /// <summary>
    /// Gets the active dataset creation timestamp in UTC, when available and after loading.
    /// </summary>
    public string? LoadedDatasetCreatedAtUtc => _active?.CreatedAtUtc;

    public LoadedDataset GetActiveOrLoad()
    {
        var current = _active;
        if (current != null) return current;

        lock (_loadLock)
        {
            current = _active;
            if (current != null) return current;

            current = LoadDataset(Req(_options.ActiveDataset, "Caop:ActiveDataset"));
            _active = current;
            return current;
        }
    }

    public IReadOnlyList<string> ListDatasets()
    {
        var root = ResolveDataRoot();
        if (!Directory.Exists(root)) return Array.Empty<string>();
        return Directory.GetDirectories(root)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    public ReverseGeocodeResult? ReverseGeocode(double lat, double lon)
    {
        if (!IsInsidePortugalPrecheck(lat, lon))
            return null;

        var ds = GetActiveOrLoad();
        var point = CreatePoint(lat, lon);
        var pointEnvelope = point.EnvelopeInternal;

        var candidates = ds.SpatialIndex.Query(pointEnvelope)
            .OrderBy(c => c.Sequence);

        foreach (var candidate in candidates)
        {
            var r = candidate.Record;
            if (!r.Envelope.Contains(point.Coordinate)) continue;
            if (r.Geometry == null) continue;

            if (r.Geometry.Covers(point))
            {
                return new ReverseGeocodeResult
                {
                    Dataset = ds.DatasetName,
                    DatasetCreatedAtUtc = ds.CreatedAtUtc,
                    Dicofre = r.Dicofre,
                    Freguesia = r.Freguesia,
                    Concelho = r.Concelho,
                    Distrito = r.Distrito,
                    AreaHa = r.AreaHa,
                    Descricao = r.Descricao
                };
            }
        }

        return null;
    }

    private static bool IsInsidePortugalPrecheck(double lat, double lon)
    {
        // Broad safety bounds for mainland Portugal; avoids expensive spatial lookup for obvious misses.
        // Intentional: dataset covers mainland Portugal only.
        // Azores and Madeira are excluded — no geometric data is present for those territories.
        var inContinental = lat >= 36.8 && lat <= 42.3 && lon >= -9.7 && lon <= -6.0;
        return inContinental;
    }

    private LoadedDataset LoadDataset(string datasetName)
    {
        var root = ResolveDataRoot();
        var datasetDir = Path.Combine(root, datasetName);

        var metadataFile = Req(_options.MetadataFile, "Caop:MetadataFile");
        var metaPath = Path.Combine(datasetDir, metadataFile);

        if (!Directory.Exists(datasetDir))
            throw new DirectoryNotFoundException($"Dataset folder not found: {datasetDir}");

        using var metadata = File.Exists(metaPath)
            ? JsonDocument.Parse(File.ReadAllText(metaPath, Encoding.UTF8))
            : null;

        // Determine TSV file name: prefer config, but fallback to metadata.output.tsv/tsvGz if present.
        var tsvFile = Req(_options.TsvFile, "Caop:TsvFile");
        if (metadata != null)
        {
            if (TryGetString(metadata.RootElement, "output", "tsv", out var tsvFromMeta) && !string.IsNullOrWhiteSpace(tsvFromMeta))
                tsvFile = tsvFromMeta!;
            else if (TryGetString(metadata.RootElement, "output", "tsvGz", out var tsvGzFromMeta) && !string.IsNullOrWhiteSpace(tsvGzFromMeta))
                tsvFile = tsvGzFromMeta!;
        }

        var tsvPath = ResolveDatasetFilePath(datasetDir, tsvFile, "TSV");
        if (!File.Exists(tsvPath))
            throw new FileNotFoundException($"TSV file not found: {tsvPath}");

        _logger.LogInformation("Loading dataset {Dataset} from {Path}", datasetName, tsvPath);

        var wktReader = new WKTReader();
        var records = new List<FreguesiaRecord>(capacity: 4096);
        var spatialIndex = new STRtree<IndexedFreguesiaRecord>();

        using var stream = OpenPossiblyGz(tsvPath);
        using var sr = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        string? header = sr.ReadLine();
        if (header == null)
            throw new InvalidDataException("TSV file is empty.");

        // Expected header: DICOFRE\tFREGUESIA\tCONCELHO\tDISTRITO\tAREA_HA\tDESCRICAO\tWKT_4326
        var lineNo = 1;
        while (true)
        {
            var line = sr.ReadLine();
            if (line == null) break;
            lineNo++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var remaining = line.AsSpan();
            if (!TryReadRequiredColumn(ref remaining, out var dicofreSpan) ||
                !TryReadRequiredColumn(ref remaining, out var freguesiaSpan) ||
                !TryReadRequiredColumn(ref remaining, out var concelhoSpan) ||
                !TryReadRequiredColumn(ref remaining, out var distritoSpan) ||
                !TryReadRequiredColumn(ref remaining, out var areaHaSpan) ||
                !TryReadRequiredColumn(ref remaining, out var descricaoSpan))
            {
                _logger.LogWarning("Skipping invalid TSV row at line {LineNo}: expected at least 7 columns", lineNo);
                continue;
            }

            var wktSpan = remaining;
            var nextTab = wktSpan.IndexOf('\t');
            if (nextTab >= 0)
                wktSpan = wktSpan[..nextTab];

            var wkt = wktSpan.ToString();
            Geometry geom;
            try
            {
                geom = wktReader.Read(wkt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping row with bad WKT at line {LineNo}", lineNo);
                continue;
            }

            double areaHa = 0;
            _ = double.TryParse(areaHaSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out areaHa);

            var rec = new FreguesiaRecord
            {
                Dicofre = dicofreSpan.ToString(),
                Freguesia = freguesiaSpan.ToString(),
                Concelho = concelhoSpan.ToString(),
                Distrito = distritoSpan.ToString(),
                AreaHa = areaHa,
                Descricao = descricaoSpan.ToString(),
                Geometry = geom,
                Envelope = geom.EnvelopeInternal
            };

            records.Add(rec);
            spatialIndex.Insert(rec.Envelope, new IndexedFreguesiaRecord(records.Count, rec));
        }

        spatialIndex.Build();

        string? createdAtUtc = null;
        if (metadata != null && TryGetString(metadata.RootElement, "createdAtUtc", out var created))
            createdAtUtc = created;

        _logger.LogInformation("Loaded dataset {Dataset}: {Count} records", datasetName, records.Count);

        return new LoadedDataset(datasetName, createdAtUtc, records, spatialIndex);
    }

    private string ResolveDataRoot()
    {
        var root = Req(_options.DataRoot, "Caop:DataRoot");
        if (Path.IsPathRooted(root)) return root;
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, root));
    }

    private static Stream OpenPossiblyGz(string path)
    {
        var fs = File.OpenRead(path);
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            return new GZipStream(fs, CompressionMode.Decompress);
        return fs;
    }

    private Point CreatePoint(double lat, double lon)
    {
        // Note: WKT uses X,Y. Our builder exports coordinates as (lat, lon) => X=lat, Y=lon.
        // Allow overriding if needed.
        var order = _options.CoordinateOrder ?? "";
        return string.Equals(order, "LonLat", StringComparison.OrdinalIgnoreCase)
            ? new Point(lon, lat) { SRID = 4326 }
            : new Point(lat, lon) { SRID = 4326 };
    }

    private static string Req(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Missing configuration value: {name}");
        return value;
    }

    private static bool TryGetString(JsonElement root, string prop, out string? value)
    {
        value = null;
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (!root.TryGetProperty(prop, out var el)) return false;
        if (el.ValueKind != JsonValueKind.String) return false;
        value = el.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetString(JsonElement root, string p1, string p2, out string? value)
    {
        value = null;
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (!root.TryGetProperty(p1, out var el1)) return false;
        if (el1.ValueKind != JsonValueKind.Object) return false;
        return TryGetString(el1, p2, out value);
    }

    private static bool TryReadRequiredColumn(ref ReadOnlySpan<char> remaining, out ReadOnlySpan<char> column)
    {
        var tabIndex = remaining.IndexOf('\t');
        if (tabIndex < 0)
        {
            column = default;
            return false;
        }

        column = remaining[..tabIndex];
        remaining = remaining[(tabIndex + 1)..];
        return true;
    }

    private static string ResolveDatasetFilePath(string datasetDir, string fileName, string label)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException($"{label} filename is required.");

        if (Path.IsPathRooted(fileName))
            throw new InvalidOperationException($"{label} filename must be relative to dataset directory.");

        if (fileName.Contains(Path.DirectorySeparatorChar) || fileName.Contains(Path.AltDirectorySeparatorChar))
            throw new InvalidOperationException($"{label} filename must not contain path separators.");

        var datasetDirFull = Path.GetFullPath(datasetDir);
        var separator = Path.DirectorySeparatorChar.ToString();
        var datasetDirFullWithSep = datasetDirFull.EndsWith(separator, StringComparison.Ordinal)
            ? datasetDirFull
            : datasetDirFull + separator;

        var candidatePath = Path.GetFullPath(Path.Combine(datasetDirFull, fileName));
        if (!candidatePath.StartsWith(datasetDirFullWithSep, StringComparison.Ordinal))
            throw new InvalidOperationException($"{label} filename resolves outside dataset directory.");

        return candidatePath;
    }
}

public sealed record LoadedDataset(
    string DatasetName,
    string? CreatedAtUtc,
    IReadOnlyList<FreguesiaRecord> Records,
    STRtree<IndexedFreguesiaRecord> SpatialIndex);

public sealed record IndexedFreguesiaRecord(
    int Sequence,
    FreguesiaRecord Record);