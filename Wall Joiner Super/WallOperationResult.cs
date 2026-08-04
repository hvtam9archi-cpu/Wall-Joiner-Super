using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.EditorInput;

namespace ProWallTools
{
    public sealed class WallOperationResult
    {
        private const int MaximumWarnings = 100;
        private readonly List<string> _warnings = new List<string>();
        private readonly HashSet<string> _warningSet = new HashSet<string>(StringComparer.Ordinal);
        private int _suppressedWarnings;

        public WallOperationResult(string operationName)
        {
            OperationName = operationName ?? "Wall Joiner";
        }

        public string OperationName { get; }
        public int SelectedCount { get; set; }
        public int ProcessedCount { get; set; }
        public int CreatedCount { get; set; }
        public int ErasedCount { get; set; }
        public int SkippedCount { get; set; }
        public bool Cancelled { get; set; }
        public bool Committed { get; set; }
        public IReadOnlyList<string> Warnings => _warnings;

        public void AddWarning(string warning)
        {
            if (string.IsNullOrWhiteSpace(warning) || !_warningSet.Add(warning))
            {
                return;
            }

            if (_warnings.Count < MaximumWarnings)
            {
                _warnings.Add(warning);
            }
            else
            {
                _suppressedWarnings++;
            }
        }

        public void WriteSummary(Editor editor)
        {
            if (editor == null)
            {
                return;
            }

            if (Cancelled)
            {
                editor.WriteMessage($"\n[{OperationName}] Đã hủy; bản vẽ không bị thay đổi.");
                return;
            }

            string status = Committed ? "Hoàn tất" : "Không thay đổi bản vẽ";
            editor.WriteMessage(
                $"\n[{OperationName}] {status}. Chọn: {SelectedCount}; xử lý: {ProcessedCount}; " +
                $"tạo: {CreatedCount}; xóa nguồn: {ErasedCount}; bỏ qua: {SkippedCount}.");

            foreach (string warning in _warnings)
            {
                editor.WriteMessage("\n  [Cảnh báo] " + warning);
            }

            if (_suppressedWarnings > 0)
            {
                editor.WriteMessage($"\n  [Cảnh báo] Đã ẩn {_suppressedWarnings} cảnh báo bổ sung.");
            }
        }
    }
}
