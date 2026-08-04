using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization.Json;

namespace ProWallTools
{
    public static class WallSettingsService
    {
        private static readonly object SyncRoot = new object();
        private static WallSettings _current = new WallSettings();

        public static WallSettings Current
        {
            get
            {
                lock (SyncRoot)
                {
                    return _current.Clone();
                }
            }
        }

        public static string SettingsPath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "WallJoinerSuper", "settings.json");
            }
        }

        public static IReadOnlyList<string> Load()
        {
            var warnings = new List<string>();
            lock (SyncRoot)
            {
                if (!File.Exists(SettingsPath))
                {
                    _current = new WallSettings();
                    return warnings;
                }

                try
                {
                    var serializer = new DataContractJsonSerializer(typeof(WallSettings));
                    using (var stream = File.OpenRead(SettingsPath))
                    {
                        var loaded = serializer.ReadObject(stream) as WallSettings;
                        if (loaded == null)
                        {
                            throw new InvalidDataException("Tệp cấu hình không chứa dữ liệu hợp lệ.");
                        }

                        var errors = loaded.Validate();
                        if (errors.Count > 0)
                        {
                            warnings.Add("Cấu hình đã lưu không hợp lệ; đã dùng cấu hình mặc định.");
                            foreach (string error in errors)
                            {
                                warnings.Add(error);
                            }
                            _current = new WallSettings();
                        }
                        else
                        {
                            _current = loaded;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _current = new WallSettings();
                    warnings.Add("Không thể đọc cấu hình; đã dùng mặc định: " + ex.Message);
                    Debug.WriteLine("[Wall Joiner Settings Load]" + Environment.NewLine + ex);
                }
            }

            return warnings;
        }

        public static void Save(WallSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var errors = settings.Validate();
            if (errors.Count > 0)
            {
                throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(settings));
            }

            lock (SyncRoot)
            {
                string directory = Path.GetDirectoryName(SettingsPath);
                Directory.CreateDirectory(directory);
                string temporaryPath = SettingsPath + ".tmp";

                try
                {
                    var serializer = new DataContractJsonSerializer(typeof(WallSettings));
                    using (var stream = File.Create(temporaryPath))
                    {
                        serializer.WriteObject(stream, settings);
                        stream.Flush();
                    }

                    if (File.Exists(SettingsPath))
                    {
                        File.Replace(temporaryPath, SettingsPath, null);
                    }
                    else
                    {
                        File.Move(temporaryPath, SettingsPath);
                    }

                    _current = settings.Clone();
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        try
                        {
                            File.Delete(temporaryPath);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("[Wall Joiner Settings Cleanup]" + Environment.NewLine + ex);
                        }
                    }
                }
            }
        }

        public static WallSettings ResetToDefaults()
        {
            var defaults = new WallSettings();
            Save(defaults);
            return defaults.Clone();
        }
    }
}
