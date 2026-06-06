// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

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
