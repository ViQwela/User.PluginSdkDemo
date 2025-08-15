using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Serialization;
using YourNamespace;

namespace User.PluginSdkDemo
{
    [PluginDescription("Pit window overlay data provider (iRacing)")]
    [PluginAuthor("ViQwela")]
    [PluginName("PitWindow Data")]
    public class PitWindowPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        // Требуется IPlugin
        public PluginManager PluginManager { get; set; }

        // Настройки (сохраняем сами в файл)
        public PitWindowSettings Settings { get; set; } = new PitWindowSettings();

        // Требуется IWPFSettingsV2
        public string LeftMenuTitle => "PitWindow Data";
        public ImageSource PictureIcon => null; // сюда можно подложить ImageSource из ресурсов, если нужно

        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private double _lastPlanUpdate = -100.0;

        // Кэш расчётов
        private double _pitStopLoss_s = 0;
        private double _dtLoss_s = 0;
        private bool _dtActive = false;
        private bool _sgActive = false;
        private double _refLap_s = 90.0; // запас по умолчанию
        private double _playerPct = 0;

        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;
            LoadSettings();
        }

        public void End(PluginManager pluginManager)
        {
            SaveSettings();
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            // В некоторых версиях демо GameData.GameRunning/NewData — не bool, поэтому не используем !
            if (!string.Equals(data.GameName, "IRacing", StringComparison.OrdinalIgnoreCase))
                return;

            // Быстрые данные
            _playerPct = GetDouble(pluginManager, "iRacing.PlayerLapDistPct", 0.0);

            // Эталон круга
            _refLap_s = ComputeRefLapTime(pluginManager);

            // Раз в 1 сек логика плана/штрафов
            double now = _sw.Elapsed.TotalSeconds;
            if (now - _lastPlanUpdate >= 1.0)
            {
                _lastPlanUpdate = now;
                UpdatePlanAndPenalties(pluginManager);
            }

            // Проценты круга для маркеров
            double pitOff = _refLap_s > 0.001 ? Clamp(_pitStopLoss_s / _refLap_s, 0, 5) : 0;
            double dtOff = _refLap_s > 0.001 ? Clamp(_dtLoss_s / _refLap_s, 0, 5) : 0;

            double pitPct = Wrap01(_playerPct - pitOff);
            double dtPct = Wrap01(_playerPct - dtOff);

            // Публикуем свойства
            pluginManager.SetPropertyValue<PitWindowPlugin>("PitWindow.PlayerPct", _playerPct);
            pluginManager.SetPropertyValue<PitWindowPlugin>("PitWindow.RefLap_s", _refLap_s);
            pluginManager.SetPropertyValue<PitWindowPlugin>("PitWindow.PitStopLoss_s", _pitStopLoss_s);
            pluginManager.SetPropertyValue<PitWindowPlugin>("PitWindow.DriveThroughLoss_s", _dtLoss_s);
            pluginManager.SetPropertyValue<PitWindowPlugin>("PitWindow.DT_Active", _dtActive);
            pluginManager.SetPropertyValue<PitWindowPlugin>("PitWindow.SG_Active", _sgActive);
            pluginManager.SetPropertyValue<PitWindowPlugin>("PitWindow.PitRejoinPct", pitPct);
            pluginManager.SetPropertyValue<PitWindowPlugin>("PitWindow.DriveThroughPct", dtPct);
        }

        public Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new PitWindowSettingsControl(this);
        }

        // Публичный метод для кнопки "Сохранить сейчас"
        public void SaveSettingsNow() => SaveSettings();

        // ============================
        // Настройки: сохранение/загрузка
        // ============================

        private string GetSettingsFilePath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                   "SimHub", "Plugins", "PitWindowData");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "PitWindow.Settings.xml");
        }

        private void SaveSettings()
        {
            try
            {
                var path = GetSettingsFilePath();
                using (var fs = File.Create(path))
                {
                    var ser = new XmlSerializer(typeof(PitWindowSettings));
                    ser.Serialize(fs, Settings);
                }
            }
            catch
            {
                // ignore
            }
        }

        private void LoadSettings()
        {
            try
            {
                var path = GetSettingsFilePath();
                if (File.Exists(path))
                {
                    using (var fs = File.OpenRead(path))
                    {
                        var ser = new XmlSerializer(typeof(PitWindowSettings));
                        var loaded = ser.Deserialize(fs) as PitWindowSettings;
                        if (loaded != null) Settings = loaded;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        // ============================
        // Логика расчётов
        // ============================

        private void UpdatePlanAndPenalties(PluginManager manager)
        {
            (_dtActive, _sgActive) = DetectPenalties(manager);

            // Топливо
            double plannedFuel = Math.Max(0.0, GetDouble(manager, Settings.Prop_PitSvFuel, 0.0));
            if (Settings.FuelUnit == FuelUnit.Gallons) plannedFuel *= 3.785411784;
            manager.SetPropertyValue<PitWindowPlugin>("PitWindow.FuelAdd_L", plannedFuel);

            double fuelTime = Settings.Refuel_Rate_Lps > 0 ? plannedFuel / Settings.Refuel_Rate_Lps : 0.0;
            manager.SetPropertyValue<PitWindowPlugin>("PitWindow.FuelTime_s", fuelTime);

            // Шины
            bool tiresPlanned = AreTiresPlanned(manager);
            manager.SetPropertyValue<PitWindowPlugin>("PitWindow.TiresPlanned", tiresPlanned);
            double tiresTime = tiresPlanned ? Settings.Tires_Change_Time_s : 0.0;
            manager.SetPropertyValue<PitWindowPlugin>("PitWindow.TiresTime_s", tiresTime);

            // Ремонт
            double repairTime = 0.0;
            bool inPitStall = GetBool(manager, "iRacing.PlayerInPitStall", false);
            bool useFastRepair = Settings.PlanFastRepair && GetInt(manager, Settings.Prop_FastRepairsLeft, 0) > 0;

            if (!useFastRepair)
            {
                if (Settings.IncludeOptionalRepair)
                    repairTime = GetDouble(manager, Settings.Prop_PitOptRepairLeft, 0.0);
                else
                    repairTime = GetDouble(manager, Settings.Prop_PitRepairLeft, 0.0);
            }
            manager.SetPropertyValue<PitWindowPlugin>("PitWindow.RepairTime_s", repairTime);

            // Потеря на пит
            if (_sgActive)
            {
                _pitStopLoss_s = Settings.SG_Stop_Base_s > 0 ? Settings.SG_Stop_Base_s : Settings.Pit_Stop_Base_s;
            }
            else
            {
                _pitStopLoss_s = Settings.Pit_Stop_Base_s + fuelTime + tiresTime + (inPitStall ? repairTime : 0.0);
            }

            // Потеря на DT
            if (_dtActive)
            {
                if (Settings.UseAutoDTDuration)
                {
                    double autoDt = GetDouble(manager, Settings.Prop_PitDTDuration, 0.0);
                    _dtLoss_s = autoDt > 0 ? autoDt : Settings.Pit_DT_Base_s;
                }
                else
                {
                    _dtLoss_s = Settings.Pit_DT_Base_s;
                }
            }
            else
            {
                _dtLoss_s = 0.0;
            }

            // Для удобства вывода
            manager.SetPropertyValue<PitWindowPlugin>("PitWindow.PitBase_s", Settings.Pit_Stop_Base_s);
            manager.SetPropertyValue<PitWindowPlugin>("PitWindow.DTBase_s", Settings.Pit_DT_Base_s);
        }

        private (bool dt, bool sg) DetectPenalties(PluginManager manager)
        {
            bool? dt = TryGetBool(manager, Settings.Prop_DriveThroughActive);
            bool? sg = TryGetBool(manager, Settings.Prop_StopAndGoActive);

            if (dt == null && !string.IsNullOrWhiteSpace(Settings.Prop_DriveThroughActive_Auto))
                dt = TryGetBool(manager, Settings.Prop_DriveThroughActive_Auto);
            if (sg == null && !string.IsNullOrWhiteSpace(Settings.Prop_StopAndGoActive_Auto))
                sg = TryGetBool(manager, Settings.Prop_StopAndGoActive_Auto);

            return (dt ?? false, sg ?? false);
        }

        private double ComputeRefLapTime(PluginManager manager)
        {
            double manual = Settings.ManualRefLap_s > 0 ? Settings.ManualRefLap_s : 0.0;
            if (Settings.RefLapSource == RefLapSource.Manual && manual > 0) return manual;

            double est = GetDouble(manager, Settings.Prop_EstLapTime, 0.0);
            double last = GetDouble(manager, Settings.Prop_LastLapTime, 0.0);
            double best = GetDouble(manager, Settings.Prop_BestLapTime, 0.0);

            switch (Settings.RefLapSource)
            {
                case RefLapSource.Auto:
                    if (est > 1) return est;
                    if (last > 1) return last;
                    if (best > 1) return best;
                    return manual > 0 ? manual : 90.0;

                case RefLapSource.Best:
                    return best > 1 ? best : (est > 1 ? est : (last > 1 ? last : (manual > 0 ? manual : 90.0)));

                case RefLapSource.Last:
                    return last > 1 ? last : (est > 1 ? est : (best > 1 ? best : (manual > 0 ? manual : 90.0)));

                case RefLapSource.Manual:
                    return manual > 0 ? manual : 90.0;

                default:
                    return 90.0;
            }
        }

        private bool AreTiresPlanned(PluginManager manager)
        {
            // Явные булевые свойства, если заданы
            bool? lf = TryGetBool(manager, Settings.Prop_TireLF);
            bool? rf = TryGetBool(manager, Settings.Prop_TireRF);
            bool? lr = TryGetBool(manager, Settings.Prop_TireLR);
            bool? rr = TryGetBool(manager, Settings.Prop_TireRR);

            if (lf != null || rf != null || lr != null || rr != null)
                return (lf ?? false) || (rf ?? false) || (lr ?? false) || (rr ?? false);

            // Фолбек: битовая маска
            int? flags = TryGetInt(manager, "iRacing.PitSvFlags");
            if (flags.HasValue)
            {
                const int LF = 0x0001;
                const int RF = 0x0002;
                const int LR = 0x0004;
                const int RR = 0x0008;
                int f = flags.Value;
                return (f & (LF | RF | LR | RR)) != 0;
            }

            return false;
        }

        // ============================
        // Helpers
        // ============================

        private static double Wrap01(double x)
        {
            x %= 1.0;
            return x < 0 ? x + 1.0 : x;
        }

        private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

        private static object GetRaw(PluginManager manager, string prop)
        {
            if (manager == null || string.IsNullOrWhiteSpace(prop)) return null;
            try { return manager.GetPropertyValue(prop); } catch { return null; }
        }

        private static double GetDouble(PluginManager manager, string prop, double def)
        {
            var v = GetRaw(manager, prop);
            if (v is double d) return d;
            if (v is float f) return f;
            if (v is int i) return i;
            if (v is long l) return l;
            if (v != null && double.TryParse(v.ToString(), out var p)) return p;
            return def;
        }

        private static int GetInt(PluginManager manager, string prop, int def)
        {
            var v = GetRaw(manager, prop);
            if (v is int i) return i;
            if (v is long l) return (int)l;
            if (v != null && int.TryParse(v.ToString(), out var p)) return p;
            return def;
        }

        private static bool GetBool(PluginManager manager, string prop, bool def)
        {
            var v = GetRaw(manager, prop);
            if (v is bool b) return b;
            if (v is int i) return i != 0;
            if (v != null && bool.TryParse(v.ToString(), out var p)) return p;
            return def;
        }

        private static double? TryGetDouble(PluginManager manager, string prop)
        {
            var v = GetRaw(manager, prop);
            if (v is double d) return d;
            if (v is float f) return f;
            if (v is int i) return i;
            if (v is long l) return l;
            if (v != null && double.TryParse(v.ToString(), out var p)) return p;
            return null;
        }

        private static int? TryGetInt(PluginManager manager, string prop)
        {
            var v = GetRaw(manager, prop);
            if (v is int i) return i;
            if (v is long l) return (int)l;
            if (v != null && int.TryParse(v.ToString(), out var p)) return p;
            return null;
        }

        private static bool? TryGetBool(PluginManager manager, string prop)
        {
            var v = GetRaw(manager, prop);
            if (v is bool b) return b;
            if (v is int i) return i != 0;
            if (v != null && bool.TryParse(v.ToString(), out var p)) return p;
            return null;
        }
    }
}
