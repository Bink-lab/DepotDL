namespace DepotDL.GUI.Models
{
    public enum SpecStatus { MeetsRecommended, MeetsMinimum, BelowMinimum, Unknown }

    public record SpecRow(
        string Label,
        string MinValue,
        string RecValue,
        string UserValue,
        SpecStatus Status);
}
