namespace ItmoBot.Mcp.Weather.Models;

public sealed record class GeoCoordinates
{
    public GeoCoordinates(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90
            || !double.IsFinite(longitude) || longitude is < -180 or > 180)
        {
            throw new ArgumentException("Координаты должны быть конечными числами: широта от -90 до 90, долгота от -180 до 180.");
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude
    {
        get;
    }
    public double Longitude
    {
        get;
    }
}
