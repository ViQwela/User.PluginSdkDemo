using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Diagnostics;

namespace YourNamespace
{
    [PluginDescription("Pit window overlay data provider (iRacing)")]
    [PluginAuthor("Ваше имя")]
    [PluginName("PitWindow Data")]
    public class PitWindowPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        private PluginManager _manager;
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        // Настройки (см. ниже класс Settings)
        public PitWindowSettings Settings { get; private set; }

        // Троттлинг «тяжёлых» расчётов (раз в 1 сек)
        private double _lastPlanUpdate = -100.0;

        // Кэш последних расчётов
        private double _pitStopLoss_s = 0;
        private double _dtLoss_s = 0;
        private bool _dtActive = false;
        private bool _sgActive = false;
        private double _refLap_s = 90.0; // дефолт
        private double _playerPct = 0;

        public void Init(PluginManager manager)
        {
            _manager = manager;

            // Загрузить/создать настройки
            Settings = manager.LoadConfiguration<PitWindowSettings>("PitWindow.Settings.json") ?? new PitWindowSettings();

            // Зарегистрировать свойства
            AddProp("PitWindow.PitRejoinPct", 0.0);
            AddProp("PitWindow.DriveThroughPct", 0.0);
            AddProp("PitWindow.PitStopLoss_s", 0.0);
            AddProp("PitWindow.DriveThroughLoss_s", 0.0);
            AddProp("PitWindow.DT_Active", false);
            AddProp("PitWindow.SG_Active", false);
            AddProp("PitWindow.RefLap_s", 0.0);

            // Отладка / вспомогательные
            AddProp("PitWindow.PlayerPct", 0.0);
            AddProp("PitWindow.FuelAdd_L", 0.0);
            AddProp("PitWindow.FuelTime_s", 0.0);
            AddProp("PitWindow.TiresPlanned", false);
            AddProp("PitWindow.TiresTime_s", 0.0);
            AddProp("PitWindow.RepairTime_s", 0.0);
            AddProp("PitWindow.PitBase_s", Settings.Pit_Stop_Base_s);
            AddProp("PitWindow.DTBase_s", Settings.Pit_DT_Base_s);
        }

        public void End(PluginManager manager)
        {
            // Сохраняем настройки
            manager.SaveConfiguration(Settings, "PitWindow.Settings.json");
        }

        public void DataUpdate(PluginManager manager, GameData data)
        {
            if (!data.HasNewData) return;
            if (!string.Equals(data.GameName, "IRacing", StringComparison.OrdinalIgnoreCase)) return;

            // 1) Считываем быстро изменяющееся
            _playerPct = GetDouble("iRacing.PlayerLapDistPct", 0.0);

            // Обновляем эталон круга
            _refLap_s = ComputeRefLapTime();

            // 2) Раз в 1 сек – «логический» план сервиса и штрафов
            double now = _sw.Elapsed.TotalSeconds;
            if (now - _lastPlanUpdate >= 1.0)
            {
                _lastPlanUpdate = now;
                UpdatePlanAndPenalties();
            }

            // 3) Конвертируем в проценты круга
            double pitOff = _refLap_s > 0.001 ? Clamp(_pitStopLoss_s / _refLap_s, 0, 5) : 0;
            double dtOff = _refLap_s > 0.001 ? Clamp(_dtLoss_s / _refLap_s, 0, 5) : 0;

            double pitPct = Wrap01(_playerPct - pitOff);
            double dtPct = Wrap01(_playerPct - dtOff);

            // 4) Публикуем свойства
            SetProp("PitWindow.PlayerPct", _playerPct);
            SetProp("PitWindow.RefLap_s", _refLap_s);
            SetProp("PitWindow.PitStopLoss_s", _pitStopLoss_s);
            SetProp("PitWindow.DriveThroughLoss_s", _dtLoss_s);
            SetProp("PitWindow.DT_Active", _dtActive);
            SetProp("PitWindow.SG_Active", _sgActive);
            SetProp("PitWindow.PitRejoinPct", pitPct);
            SetProp("PitWindow.DriveThroughPct", dtPct);
        }

        private void UpdatePlanAndPenalties()
        {
            // 2.1) Штрафы
            (_dtActive, _sgActive) = DetectPenalties();

            // 2.2) План сервиса
            // Топливо
            double plannedFuel = Math.Max(0.0, GetDouble(Settings.Prop_PitSvFuel, 0.0)); // по умолчанию "iRacing.PitSvFuel"
            if (Settings.FuelUnit == FuelUnit.Gallons) plannedFuel *= 3.785411784;
            SetProp("PitWindow.FuelAdd_L", plannedFuel);

            double fuelTime = Settings.Refuel_Rate_Lps > 0 ? plannedFuel / Settings.Refuel_Rate_Lps : 0.0;
            SetProp("PitWindow.FuelTime_s", fuelTime);

            // Шины
            bool tiresPlanned = AreTiresPlanned();
            SetProp("PitWindow.TiresPlanned", tiresPlanned);
            double tiresTime = tiresPlanned ? Settings.Tires_Change_Time_s : 0.0;
            SetProp("PitWindow.TiresTime_s", tiresTime);

            // Ремонт
            double repairTime = 0.0;
            bool inPitStall = GetBool("iRacing.PlayerInPitStall", false);
            if (!Settings.PlanFastRepair || GetInt(Settings.Prop_FastRepairsLeft, 0) <= 0)
            {
                if (Settings.IncludeOptionalRepair)
                    repairTime = GetDouble(Settings.Prop_PitOptRepairLeft, 0.0);
                else
                    repairTime = GetDouble(Settings.Prop_PitRepairLeft, 0.0);
            }
            SetProp("PitWindow.RepairTime_s", repairTime);

            // 2.3) Потеря времени на пит
            if (_sgActive)
            {
                // По ТЗ: при S&G учитывать только базовое время «пит с остановкой»
                _pitStopLoss_s = Settings.SG_Stop_Base_s > 0 ? Settings.SG_Stop_Base_s : Settings.Pit_Stop_Base_s;
            }
            else
            {
                _pitStopLoss_s = Settings.Pit_Stop_Base_s + fuelTime + tiresTime + (inPitStall ? repairTime : 0.0);
            }

            // 2.4) Потеря времени на DT
            if (!_dtActive)
            {
                _dtLoss_s = 0.0;
            }
            else
            {
                if (Settings.UseAutoDTDuration)
                {
                    double autoDt = GetDouble(Settings.Prop_PitDTDuration, 0.0); // напр. IRacingExtraProperties.iRacing_PitDriveThroughDuration
                    _dtLoss_s = autoDt > 0 ? autoDt : Settings.Pit_DT_Base_s;
                }
                else
                {
                    _dtLoss_s = Settings.Pit_DT_Base_s;
                }
            }

            // Синхронизируем базовые в свойства (для удобства вывода)
            SetProp("PitWindow.PitBase_s", Settings.Pit_Stop_Base_s);
            SetProp("PitWindow.DTBase_s", Settings.Pit_DT_Base_s);
        }

        private (bool dt, bool sg) DetectPenalties()
        {
            // Режим 1: Явные свойства пользователя (могут быть Expression из SimHub)
            bool? dt = TryGetBool(Settings.Prop_DriveThroughActive);
            bool? sg = TryGetBool(Settings.Prop_StopAndGoActive);

            // Режим 2: Автодетект через ExtraProperties (если пользователь оставил по умолчанию)
            if (dt == null && !string.IsNullOrWhiteSpace(Settings.Prop_DriveThroughActive_Auto))
                dt = TryGetBool(Settings.Prop_DriveThroughActive_Auto);
            if (sg == null && !string.IsNullOrWhiteSpace(Settings.Prop_StopAndGoActive_Auto))
                sg = TryGetBool(Settings.Prop_StopAndGoActive_Auto);

            // Режим 3: Фолбек — отключено
            return (dt ?? false, sg ?? false);
        }

        private double ComputeRefLapTime()
        {
            double manual = Settings.ManualRefLap_s > 0 ? Settings.ManualRefLap_s : 0.0;
            if (Settings.RefLapSource == RefLapSource.Manual && manual > 0) return manual;

            double est = GetDouble(Settings.Prop_EstLapTime, 0.0);   // Пробуем Estimated
            double last = GetDouble(Settings.Prop_LastLapTime, 0.0); // Последний валидный
            double best = GetDouble(Settings.Prop_BestLapTime, 0.0); // Лучший

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

                default:
                    return 90.0;
            }
        }

        // Вспомогательные методы:
        private double Wrap01(double x) { x = x % 1.0; return x < 0 ? x + 1.0 : x; }
        private double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

        private void AddProp(string name, object defaultValue) => _manager.AddProperty(name, this, defaultValue);

        private void SetProp(string name, object value) => _manager.SetPropertyValue(name, value);

        private double GetDouble(string prop, double def) => TryGetDouble(prop) ?? def;
        private int GetInt(string prop, int def) => TryGetInt(prop) ?? def;
        private bool GetBool(string prop, bool def) => TryGetBool(prop) ?? def;

        private double? TryGetDouble(string prop)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(prop)) return null;
                var v = _manager.GetPropertyValue(prop);
                if (v == null) return null;
                if (v is double d) return d;
                if (v is float f) return f;
                if (v is int i) return i;
                if (v is long l) return l;
                if (double.TryParse(v.ToString(), out var p)) return p;
                return null;
            }
            catch { return null; }
        }

        private int? TryGetInt(string prop)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(prop)) return null;
                var v = _manager.GetPropertyValue(prop);
                if (v == null) return null;
                if (v is int i) return i;
                if (v is long l) return (int)l;
                if (int.TryParse(v.ToString(), out var p)) return p;
                return null;
            }
            catch { return null; }
        }

        private bool? TryGetBool(string prop)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(prop)) return null;
                var v = _manager.GetPropertyValue(prop);
                if (v == null) return null;
                if (v is bool b) return b;
                if (v is int i) return i != 0;
                if (bool.TryParse(v.ToString(), out var p)) return p;
                return null;
            }
            catch { return null; }
        }

        // IWPFSettingsV2:
        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager manager)
        {
            return new PitWindowSettingsControl(this);
        }

        public void Dispose() { }

        public void SaveSettingsNow()
        {
            if (_manager != null && Settings != null)
            {
                _manager.SaveConfiguration(Settings, "PitWindow.Settings.json");
            }
        }
    }
}