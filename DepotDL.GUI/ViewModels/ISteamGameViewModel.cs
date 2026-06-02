using System.Windows.Media.Imaging;

namespace DepotDL.GUI.ViewModels
{
    public interface ISteamGameViewModel
    {
        BitmapImage? HeaderImage { get; set; }
        bool IsImageLoading { get; set; }
    }
}
