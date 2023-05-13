namespace AcControl.Server.Data.Models
{
    using AcControl.Server.Data;

    public class AcUnitModel
    {
        public delegate void ChangedEventHandler();
        public event ChangedEventHandler? Changed;

        public AcUnitModel(AirConditionerUnitDetails initialDetails)
        {
            this.Name = initialDetails.Name;
            this.Id = initialDetails.Id;
            this.DeviceUniqueId = initialDetails.DeviceUniqueId;
            this.StateValue = new(initialDetails.ACStateData);

            var state = UnitState.Parse(initialDetails.ACStateData);

            this.PowerStatus = new(state.PowerStatus);
            this.Mode = new(state.Mode);
            this.TargetTemperature = new(state.TargetTemperature);
            this.FanMode = new(state.FanMode);
            this.IndoorTemperature = new(state.IndoorTemperature);
            this.OutdoorTemperature = new(state.OutdoorTemperature);

            this.PowerStatus.Changed += this.Property_Changed;
            this.Mode.Changed += this.Property_Changed;
            this.TargetTemperature.Changed += this.Property_Changed;
            this.FanMode.Changed += this.Property_Changed;
            this.IndoorTemperature.Changed += this.Property_Changed;
            this.OutdoorTemperature.Changed += this.Property_Changed;
        }

        public string Name { get; }

        public string Id { get; }

        public string DeviceUniqueId { get; }

        public Property<string> StateValue { get; }

        public Property<PowerState> PowerStatus { get; }

        public Property<AirConditionerMode> Mode { get; }

        public Property<int> TargetTemperature { get; }

        public Property<FanMode> FanMode { get; }

        public Property<int> IndoorTemperature { get; }

        public Property<int> OutdoorTemperature { get; }

        public AcUnitModel Update(string stateValue)
        {
            this.StateValue.Reset(stateValue);

            var state = UnitState.Parse(stateValue);

            // TODO: should really just do away with all these properties now and just get the value sfrom the StateValue (now that it's a property too)
            this.PowerStatus.Reset(state.PowerStatus);
            this.Mode.Reset(state.Mode);
            this.TargetTemperature.Reset(state.TargetTemperature);
            this.FanMode.Reset(state.FanMode);
            this.IndoorTemperature.Reset(state.IndoorTemperature);
            this.OutdoorTemperature.Reset(state.OutdoorTemperature);

            return this;
        }

        public string TogglePower()
        {
            switch (this.PowerStatus.Target)
            {
                case PowerState.On:
                    {
                        this.PowerStatus.Target = PowerState.Off;
                        this.StateValue.Target = "31" + this.StateValue.Target[2..];
                        return this.StateValue.Target;
                    }
                default:
                    {
                        this.PowerStatus.Target = PowerState.On;
                        this.StateValue.Target = "30" + this.StateValue.Target[2..];
                        return this.StateValue.Target;
                    }
            }
        }

        public string SetTargetTemp(int temp)
        {
            this.TargetTemperature.Target = temp;
            this.StateValue.Target = this.StateValue.Target[..4] + temp.ToString("X") + this.StateValue.Target[6..];

            return this.StateValue.Target;
        }

        public string SetMode(AirConditionerMode mode)
        {
            var modeCode = mode switch
            {
                AirConditionerMode.Cool => "42",
                AirConditionerMode.Heat => "43",
                AirConditionerMode.Dry => "44",
                AirConditionerMode.Fan => "45",
                _ => "41",
            };

            this.Mode.Target = mode;

            this.StateValue.Target = this.StateValue.Target[..2] + modeCode + this.StateValue.Target[4..];
            return this.StateValue.Target;
        }

        public string SetFan(FanMode mode)
        {
            var modeCode = mode switch
            {
                Models.FanMode.Auto => "41",
                Models.FanMode.Quiet => "31",
                Models.FanMode.Low => "32",
                Models.FanMode.MediumLow => "33",
                Models.FanMode.Medium => "34",
                Models.FanMode.MediumHigh => "35",
                Models.FanMode.High => "36",
                Models.FanMode.None => "00",
                _ => "41",
            };

            this.FanMode.Target = mode;

            this.StateValue.Target = this.StateValue.Target[..6] + modeCode + this.StateValue.Target[8..];
            return this.StateValue.Target;
        }

        private void Property_Changed() => this.Changed?.Invoke();
    }

    public readonly record struct UnitState(PowerState PowerStatus, AirConditionerMode Mode, int TargetTemperature, FanMode FanMode, int IndoorTemperature, int OutdoorTemperature)
    {
        public static UnitState Parse(string state)
        {
            var padded = state[..12] + "0" + state[12..13] + "0" + state[13..];

            var powerStatus = padded[0..2] switch
            {
                "30" => PowerState.On,
                "31" => PowerState.Off,
                _ => PowerState.Unknown,
            };

            var mode = padded[2..4] switch
            {
                "41" => AirConditionerMode.Auto,
                "42" => AirConditionerMode.Cool,
                "43" => AirConditionerMode.Heat,
                "44" => AirConditionerMode.Dry,
                "45" => AirConditionerMode.Fan,
                _ => AirConditionerMode.Unknown,
            };

            var targetTemperature = Convert.ToInt32(padded[4..6], 16);

            var fanMode = padded[6..8] switch
            {
                "41" => FanMode.Auto,
                "31" => FanMode.Quiet,
                "32" => FanMode.Low,
                "33" => FanMode.MediumLow,
                "34" => FanMode.Medium,
                "35" => FanMode.MediumHigh,
                "36" => FanMode.High,
                "00" => FanMode.None,
                _ => FanMode.Unknown,
            };

            var indoorTemp = Convert.ToInt32(padded[18..20], 16);

            var outdoorTemp = FixSilly(Convert.ToInt32(padded[20..22], 16));

            return new UnitState(powerStatus, mode, targetTemperature, fanMode, indoorTemp, outdoorTemp);
        }

        private static int FixSilly(int value)
        {
            while (value > 128)
            {
                value -= 128;
            }

            return value;
        }
    }

    public class Property<T>
    {
        private T mTarget;
        private T mCurrent;

        public delegate void ChangedEventHandler();
        public event ChangedEventHandler? Changed;

        public Property(T initialValue)
        {
            mCurrent = initialValue;
            mTarget = initialValue;
        }

        public T Target
        {
            get => mTarget;
            set
            {
                if (mTarget!.Equals(value))
                {
                    return;
                }

                mTarget = value;

                this.Changed?.Invoke();
            }
        }

        public T Current
        {
            get => mCurrent;
            set
            {
                if (mCurrent!.Equals(value))
                {
                    return;
                }

                mCurrent = value;

                this.Changed?.Invoke();
            }
        }

        internal void Reset(T value)
        {
            var hasChanged = false;
            if (!mCurrent!.Equals(value))
            {
                mCurrent = value;
                hasChanged = true;
            }

            if (!mTarget!.Equals(value))
            {
                mTarget = value;
                hasChanged = true;
            }

            if (hasChanged)
            {
                this.Changed?.Invoke();
            }
        }
    }

    public enum PowerState
    {
        On,
        Off,
        Unknown
    }

    public enum AirConditionerMode
    {
        Auto,
        Unknown,
        Fan,
        Dry,
        Heat,
        Cool
    }

    public enum FanMode
    {
        None,
        Auto,
        Quiet,
        Low,
        MediumLow,
        Medium,
        MediumHigh,
        High,
        Unknown
    }
}
