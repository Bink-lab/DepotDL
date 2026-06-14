using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace DepotDL.GUI.Helpers
{
    internal sealed class TransformOpsAnimator : InterpolatingAnimator<ITransform?>
    {
        public override ITransform? Interpolate(double progress, ITransform? oldValue, ITransform? newValue)
        {
            if (oldValue is TransformOperations from && newValue is TransformOperations to)
                return TransformOperations.Interpolate(from, to, progress);
            return newValue;
        }
    }
}
