using System;

namespace YourNamespace
{
    public enum FuelUnit { Liters, Gallons }
    public enum RefLapSource { Auto, Best, Last, Manual }

    public class PitWindowSettings
    {
        // Базовые ручные времена
        public double Pit_Stop_Base_s { get; set; } = 18.0; // «пит с остановкой» без сервиса
        public double Pit_DT_Base_s { get; set; } = 14.0;   // «чистый проезд»
        public double SG_Stop_Base_s { get; set; } = 18.0;  // базовое для stop-and-go (если отличается)

        // Топливо и шины
        public FuelUnit FuelUnit { get; set; } = FuelUnit.Liters;
        public double Refuel_Rate_Lps { get; set; } = 2.0; // литров в секунду
        public double Tires_Change_Time_s { get; set; } = 8.0; // время смены шин (универсально)

        // Ремонт
        public bool PlanFastRepair { get; set; } = false; // «планирую быстрый ремонт на следующем пите»
        public bool IncludeOptionalRepair { get; set; } = false; // учитывать опциональный ремонт

        // Источник эталонного круга
        public RefLapSource RefLapSource { get; set; } = RefLapSource.Auto;
        public double ManualRefLap_s { get; set; } = 0.0;

        // Авто‑длительность DT
        public bool UseAutoDTDuration { get; set; } = true;

        // Имена свойств для чтения (можно менять в UI под вашу инсталляцию)
        // Если оставить пустым — используем автозначения/фолбеки.
        public string Prop_PitSvFuel { get; set; } = "iRacing.PitSvFuel";
        public string Prop_TireLF { get; set; } = "iRacing.PitSvTireChangeLF";
        public string Prop_TireRF { get; set; } = "iRacing.PitSvTireChangeRF";
        public string Prop_TireLR { get; set; } = "iRacing.PitSvTireChangeLR";
        public string Prop_TireRR { get; set; } = "iRacing.PitSvTireChangeRR";

        public string Prop_FastRepairsLeft { get; set; } = "iRacing.FastRepairAvailable";
        public string Prop_PitRepairLeft { get; set; } = "iRacing.PitRepairLeft";
        public string Prop_PitOptRepairLeft { get; set; } = "iRacing.PitOptRepairLeft";

        public string Prop_EstLapTime { get; set; } = "iRacing.EstimatedLapTime";
        public string Prop_LastLapTime { get; set; } = "iRacing.LastLapTime";
        public string Prop_BestLapTime { get; set; } = "iRacing.BestLapTime";

        // Автоматическое время DT (из Romainrob Extra)
        public string Prop_PitDTDuration { get; set; } = "IRacingExtraProperties.iRacing_PitDriveThroughDuration";

        // Штрафы: пользователь может подставить свои имена (например, свои Expressions)
        public string Prop_DriveThroughActive { get; set; } = ""; // если указано — используем в первую очередь
        public string Prop_StopAndGoActive { get; set; } = "";

        // Автодетект через ExtraProperties (если пользователи не заполнили поля выше)
        public string Prop_DriveThroughActive_Auto { get; set; } = "IRacingExtraProperties.iRacing_HasDriveThroughPenalty";
        public string Prop_StopAndGoActive_Auto { get; set; } = "IRacingExtraProperties.iRacing_HasStopAndGoPenalty";
    }
}
