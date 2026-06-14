// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using Avalonia.Media.Imaging;

namespace DepotDL.GUI.ViewModels
{
    public interface ISteamGameViewModel
    {
        Bitmap? HeaderImage { get; set; }
        bool IsImageLoading { get; set; }
    }
}
