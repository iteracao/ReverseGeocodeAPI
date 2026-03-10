using ReverseGeocodeApi.Models;

namespace ReverseGeocodeApi.Services;

public interface ICaopDatasetService
{
    DatasetInfo GetActiveDatasetInfo();
    IReadOnlyList<string> ListDatasets();
    ReverseGeocodeResult? ReverseGeocode(double lat, double lon);
}
