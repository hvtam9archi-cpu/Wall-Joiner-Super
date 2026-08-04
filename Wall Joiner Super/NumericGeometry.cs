using System;

namespace ProWallTools
{
    public static class NumericGeometry
    {
        public static double RoundToStep(double value, double step)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Giá trị phải hữu hạn.");
            }

            if (double.IsNaN(step) || double.IsInfinity(step) || step <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(step), "Bước lưới phải là số hữu hạn lớn hơn 0.");
            }

            return Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
        }

        public static bool NearlyEqual(double first, double second, double tolerance)
        {
            return Math.Abs(first - second) <= tolerance;
        }
    }
}
