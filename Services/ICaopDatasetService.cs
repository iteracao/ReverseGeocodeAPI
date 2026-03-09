using ReverseGeocodeApi.Models;

namespace ReverseGeocodeApi.Services;

public interface ICaopDatasetService
{
    LoadedDataset GetActiveOrLoad();
    IReadOnlyList<string> ListDatasets();
    ReverseGeocodeResult? ReverseGeocode(double lat, double lon);
}
