using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ProWallTools
{
    public enum FinishOffsetMode
    {
        Outside = 0,
        Inside = 1,
        Both = 2
    }

    [DataContract]
    public sealed class WallSettings
    {
        private bool _keepOriginals;

        [DataMember(Order = 1)]
        public double GapTolerance { get; set; } = 10.0;

        [DataMember(Order = 2)]
        public double VertexTolerance { get; set; } = 1e-4;

        [DataMember(Order = 3)]
        public double FinishOffset { get; set; } = 15.0;

        [DataMember(Order = 4)]
        public double SnapRadius { get; set; } = 10.0;

        [DataMember(Order = 5)]
        public double SnapStep { get; set; } = 5.0;

        [DataMember(Order = 6)]
        public string WallLayer { get; set; } = "ABC_A_Nettuong";

        [DataMember(Order = 7)]
        public string FinishLayer { get; set; } = "ABC_A_Netmanh";

        [DataMember(Order = 8)]
        public bool KeepOriginals
        {
            get => _keepOriginals || !StrictMode;
            set => _keepOriginals = value;
        }

        [DataMember(Order = 9)]
        public bool StrictMode { get; set; } = true;

        [DataMember(Order = 10)]
        public FinishOffsetMode FinishOffsetMode { get; set; } = FinishOffsetMode.Outside;

        public WallSettings Clone()
        {
            return (WallSettings)MemberwiseClone();
        }

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();

            ValidateFinitePositive(GapTolerance, nameof(GapTolerance), allowZero: true, errors);
            ValidateFinitePositive(VertexTolerance, nameof(VertexTolerance), allowZero: false, errors);
            ValidateFinitePositive(FinishOffset, nameof(FinishOffset), allowZero: false, errors);
            ValidateFinitePositive(SnapRadius, nameof(SnapRadius), allowZero: true, errors);
            ValidateFinitePositive(SnapStep, nameof(SnapStep), allowZero: false, errors);

            if (string.IsNullOrWhiteSpace(WallLayer))
            {
                errors.Add("Wall Layer không được để trống.");
            }

            if (string.IsNullOrWhiteSpace(FinishLayer))
            {
                errors.Add("Finish Layer không được để trống.");
            }

            if (!Enum.IsDefined(typeof(FinishOffsetMode), FinishOffsetMode))
            {
                errors.Add("Finish Offset Mode không hợp lệ.");
            }

            return errors;
        }

        private static void ValidateFinitePositive(
            double value,
            string name,
            bool allowZero,
            ICollection<string> errors)
        {
            bool invalidRange = allowZero ? value < 0 : value <= 0;
            if (double.IsNaN(value) || double.IsInfinity(value) || invalidRange)
            {
                string range = allowZero ? "không âm" : "lớn hơn 0";
                errors.Add($"{name} phải là số hữu hạn {range}.");
            }
        }
    }
}
